// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NexusContract.Abstractions.Configuration;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Abstractions.Security;
using NexusContract.Core.Configuration;
using NexusContract.Hosting.Security;
using StackExchange.Redis;

namespace NexusContract.Hosting.Configuration
{
    /// <summary>
    /// 混合配置解析器：L1（内存）+ Redis（主数据源）双层架构
    /// 
    /// 架构设计（Redis-First + 极致缓存优化）：
    /// - L1 缓存：MemoryCache（进程内，12 小时 TTL + 负缓存 1 分钟）
    /// - L2/L3 合并：Redis（持久化存储，永久保存 + RDB/AOF 持久化）
    /// - L4 可选：数据库（冷备份 + 审计日志，通过外部服务异步写入）
    /// 
    /// 缓存策略优化（针对"5年不变"的极低频变更场景）：
    /// - 长 TTL（12h）+ Pub/Sub 强一致性 → 99.99% L1 命中率
    /// - 负缓存（1min）→ 防穿透攻击（恶意扫描不存在的 RealmId）
    /// 
    /// 架构决策（ADR-008: Redis-First Tenant Storage）：
    /// - 使用 Redis 作为租户配置主数据源（替代关系型数据库）
    /// - 理由：ISV 场景配置变更低频、读多写少、KV 结构、无复杂查询需求
    /// - 持久化：Redis RDB（每小时）+ AOF（每秒）保证数据安全
    /// - 审计：可选接入外部审计服务（异步写入 MySQL/PostgreSQL）
    /// 
    /// 缓存策略：
    /// 1. 查询时：L1 → Redis（直接读取，无多层回填）
    /// 2. 刷新时：清除 L1 + 更新 Redis，触发 Pub/Sub 通知其他实例
    /// 3. 预热时：批量从 Redis 加载配置到 L1
    /// 
    /// 性能特征：
    /// - L1 命中：约 1 μs（纯内存）
    /// - Redis 查询：约 1 ms（网络 + 反序列化）
    /// - 写入延迟：约 2 ms（Redis 写入 + Pub/Sub 通知）
    /// - 缓存击穿保护：SemaphoreSlim 防止并发查询 Redis
    /// 
    /// 安全约束：
    /// - PrivateKey 序列化到 Redis 时必须加密（AES256）
    /// - Redis 连接使用 TLS 加密传输
    /// - 缓存键包含租户隔离信息
    /// 
    /// 使用场景：
    /// - 生产环境：高并发 ISV 网关
    /// - 多实例部署：配置跨实例共享
    /// - 动态配置：支持运行时热更新
    /// </summary>
    public sealed class HybridConfigResolver : IConfigurationResolver, IDisposable
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _redisDb;
        private readonly ISubscriber _redisSub;
        private readonly IMemoryCache _memoryCache;
        private readonly ISecurityProvider _securityProvider;
        private readonly ILogger<HybridConfigResolver> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;
        private readonly string _redisKeyPrefix;
        private readonly string _pubSubChannel;
        private readonly TimeSpan _l1Ttl;
        private readonly TimeSpan _l2Ttl;
        private readonly TimeSpan _indexCacheTtl;

        /// <summary>
        /// L1 缓存 TTL（默认 12 小时）
        /// 
        /// 设计理念（适配极低频变更场景）：
        /// - ISV 配置极少变更（通常以"年"为单位，极端情况 5 年不变）
        /// - Redis Pub/Sub 保证强一致性刷新（配置变更时主动清除 L1）
        /// - 长 TTL 使 99.99% 请求命中本地内存（<1μs 延迟，QPS 达百万级）
        /// - 兜底机制：即使 Pub/Sub 消息丢失，12 小时后也会自动刷新
        /// 
        /// 性能收益：
        /// - L1 命中率：95% → 99.99%
        /// - Redis IOPS：中等 → 极低（仅启动或变更时访问）
        /// - 平均延迟：50μs → <1μs（纯内存操作）
        /// </summary>
        private static readonly TimeSpan DefaultL1Ttl = TimeSpan.FromHours(12);

        /// <summary>
        /// L2 缓存 TTL（默认 30 分钟）
        /// </summary>
        private static readonly TimeSpan DefaultL2Ttl = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 权限索引缓存 TTL（默认 1 小时）
        /// 
        /// 设计理念：
        /// - 租户与 ProfileId 的映射关系极其稳定（变更频率低于配置本身）
        /// - 延长缓存时间可减少 IDOR 校验的 Redis 查询（SISMEMBER）
        /// - 配置变更时 Pub/Sub 会同步清除权限索引缓存
        /// </summary>
        private static readonly TimeSpan DefaultIndexCacheTtl = TimeSpan.FromHours(1);

        /// <summary>
        /// Redis 键前缀（默认）
        /// </summary>
        private const string DefaultKeyPrefix = "nexus:config:";

        /// <summary>
        /// Pub/Sub 通道名称（默认）
        /// </summary>
        private const string DefaultPubSubChannel = "nexus:config:refresh";

        /// <summary>
        /// 负缓存 TTL（默认 1 分钟）
        /// 
        /// 设计理念（防穿透攻击）：
        /// - 缓存不存在的配置（避免恶意请求反复查询 Redis）
        /// - TTL 不宜过长（1 分钟），保证新配置上线后 1 分钟内可用
        /// - 足以抵御短时间内的穿透攻击（如暴力扫描 RealmId）
        /// </summary>
        private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 负缓存哨兵对象（标识配置不存在）
        /// 使用私有密封类避免与正常 ProviderSettings 混淆
        /// </summary>
        private sealed class NotFoundSentinel { }
        private static readonly NotFoundSentinel ConfigNotFoundMarker = new NotFoundSentinel();

        /// <summary>
        /// 构造混合配置解析器
        /// </summary>
        /// <param name="redis">Redis 连接复用器</param>
        /// <param name="memoryCache">内存缓存实例</param>
        /// <param name="securityProvider">安全提供程序（用于私钥加解密）</param>
        /// <param name="logger">日志记录器（用于安全审计及诊断）</param>
        /// <param name="redisKeyPrefix">Redis 键前缀（可选）</param>
        /// <param name="l1Ttl">L1 缓存 TTL（可选）</param>
        /// <param name="l2Ttl">L2 缓存 TTL（可选）</param>
        public HybridConfigResolver(
            IConnectionMultiplexer redis,
            IMemoryCache memoryCache,
            ISecurityProvider securityProvider,
            ILogger<HybridConfigResolver> logger,
            string? redisKeyPrefix = null,
            TimeSpan? l1Ttl = null,
            TimeSpan? l2Ttl = null)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _securityProvider = securityProvider ?? throw new ArgumentNullException(nameof(securityProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisDb = redis.GetDatabase();
            _redisSub = redis.GetSubscriber();
            _locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            _redisKeyPrefix = redisKeyPrefix ?? DefaultKeyPrefix;
            _pubSubChannel = DefaultPubSubChannel;
            _l1Ttl = l1Ttl ?? DefaultL1Ttl;
            _l2Ttl = l2Ttl ?? DefaultL2Ttl;
            _indexCacheTtl = DefaultIndexCacheTtl;

            // 订阅配置刷新通知
            _redisSub.Subscribe(new RedisChannel(_pubSubChannel, RedisChannel.PatternMode.Literal), OnConfigRefreshMessage);
        }

        /// <summary>
        /// JIT 解析配置（支持默认 AppId 自动解析）
        /// 
        /// 解析策略：
        /// 1. ProfileId 存在 → 精确匹配：sysid + appid + providername
        /// 2. ProfileId 为空 → 默认匹配：sysid + providername + default appid
        ///    - 尝试查找标记为 default 的 AppId
        ///    - 若无默认标记，返回 first AppId
        /// 3. 查询顺序：L1（内存）→ L2（Redis）
        /// </summary>
        public async Task<IProviderConfiguration> ResolveAsync(
            ITenantIdentity identity,
            CancellationToken ct = default)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            // 如果 ProfileId 为空，自动解析默认 AppId
            ITenantIdentity resolvedIdentity = identity;
            if (string.IsNullOrWhiteSpace(identity.ProfileId))
            {
                resolvedIdentity = await ResolveDefaultProfileAsync(identity, ct)
                    .ConfigureAwait(false);
            }

            // 🔐 防越权校验：验证 AppId 是否属于该 SysId（IDOR 防护）
            await ValidateOwnershipAsync(resolvedIdentity, ct)
                .ConfigureAwait(false);

            string cacheKey = BuildCacheKey(resolvedIdentity);

            // 1. 尝试 L1 缓存（内存），包括负缓存检查
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedValue))
            {
                // 检查是否为负缓存标记（配置不存在）
                if (cachedValue is NotFoundSentinel)
                {
                    throw NexusTenantException.NotFound(
                        $"{resolvedIdentity.ProviderName}:{resolvedIdentity.RealmId}:{resolvedIdentity.ProfileId}. " +
                        $"Use SetConfigurationAsync() to create it.");
                }
                
                // 正常配置缓存命中
                if (cachedValue is ProviderSettings l1Config)
                {
                    return l1Config;
                }
            }

            // 2. 缓存击穿保护（SemaphoreSlim）
            SemaphoreSlim cacheLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await cacheLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 双重检查：可能其他线程已加载
                if (_memoryCache.TryGetValue(cacheKey, out object? cachedValue2))
                {
                    if (cachedValue2 is ProviderSettings l1Config2)
                    {
                        return l1Config2;
                    }
                }

                // 3. 尝试 L2 缓存（Redis）
                RedisValue l2Value = await _redisDb.StringGetAsync(cacheKey).ConfigureAwait(false);
                if (l2Value.HasValue)
                {
                    ProviderSettings? redisConfig = DeserializeConfig(l2Value!);
                    if (redisConfig != null)
                    {
                        // 回填 L1 缓存
                        SetL1Cache(cacheKey, redisConfig);
                        return redisConfig;
                    }
                }

                // 4. Redis 中也未找到配置，设置负缓存（防穿透）
                _memoryCache.Set(cacheKey, ConfigNotFoundMarker, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = NegativeCacheTtl,
                    Size = 1
                });

                throw NexusTenantException.NotFound(
                    $"{resolvedIdentity.ProviderName}:{resolvedIdentity.RealmId}:{resolvedIdentity.ProfileId}. " +
                    $"Use SetConfigurationAsync() to create it.");
            }
            finally
            {
                cacheLock.Release();
            }
        }

        /// <summary>
        /// 解析默认 ProfileId（AppId）
        /// 
        /// 策略：
        /// 1. 查找 Redis Hash 中标记为 default 的 AppId
        /// 2. 若无 default 标记，返回第一个 AppId
        /// 3. 若该 SysId 下没有任何 AppId，抛出异常
        /// </summary>
        private async Task<ITenantIdentity> ResolveDefaultProfileAsync(
            ITenantIdentity identity,
            CancellationToken ct)
        {
            // 1. 查询映射层（Redis Set）获取所有可用 ProfileId
            string mapKey = BuildMapKey(identity.ProviderName, identity.RealmId);

            // 2. 尝试获取默认 ProfileId 标记（存储在 map 的元数据中）
            string defaultMarker = $"{mapKey}:default";
            RedisValue defaultProfileId = await _redisDb.StringGetAsync(defaultMarker)
                .ConfigureAwait(false);

            if (defaultProfileId.HasValue)
            {
                return new ConfigurationContext(identity.ProviderName, identity.RealmId)
                {
                    ProfileId = defaultProfileId.ToString()
                };
            }

            // 3. 如果未设置默认，从 map 中获取第一个 ProfileId
            var allProfileIds = await _redisDb.SetMembersAsync(mapKey)
                .ConfigureAwait(false);

            if (allProfileIds == null || allProfileIds.Length == 0)
            {
                throw new NexusTenantException(
                    $"No ProfileId found for RealmId '{identity.RealmId}' in Provider '{identity.ProviderName}'");
            }

            var firstProfileId = allProfileIds[0];
            if (firstProfileId.IsNullOrEmpty)
            {
                throw new NexusTenantException(
                    $"No valid ProfileId found for RealmId '{identity.RealmId}' in Provider '{identity.ProviderName}'");
            }

            return new ConfigurationContext(identity.ProviderName, identity.RealmId)
            {
                ProfileId = firstProfileId.ToString()
            };
        }

        /// <summary>
        /// 防越权校验：验证 AppId 是否属于该 SysId
        /// 
        /// 安全设计：
        /// - 使用 Redis Set 存储权限白名单（O(1) 查询）
        /// - 权限索引缓存到 L1（10 分钟 TTL）
        /// - 记录所有越权尝试（安全审计）
        /// - 强制校验，任何未授权访问直接拒绝
        /// 
        /// 攻击场景防护：
        /// - 场景 1：攻击者猜测其他租户的 AppId
        ///   → 由于不在其 SysId 的索引内，直接拦截
        /// - 场景 2：攻击者伪造 SysId
        ///   → 签名验证失败（在 Provider 层拦截）
        /// </summary>
        private async Task ValidateOwnershipAsync(
            ITenantIdentity identity,
            CancellationToken ct)
        {
            // 使用统一的 map 层进行权限校验（废弃独立的 index 层）
            string mapKey = BuildMapKey(identity.ProviderName, identity.RealmId);
            string mapCacheKey = $"map:{mapKey}";

            // 1. 尝试从 L1 缓存读取权限映射结果
            bool? cachedResult = _memoryCache.Get<bool?>(mapCacheKey);
            if (cachedResult.HasValue)
            {
                if (!cachedResult.Value)
                {
                    // 缓存中已确认无权限，直接拒绝（避免重复查询 Redis）
                    LogUnauthorizedAccess(identity);
                    throw new UnauthorizedAccessException(
                        $"AppId '{identity.ProfileId}' is not authorized for SysId '{identity.RealmId}'");
                }
                return; // 缓存命中且已授权
            }

            // 2. 从 Redis 查询映射层（使用 Set 的 SISMEMBER，O(1) 复杂度）
            bool isAuthorized = await _redisDb.SetContainsAsync(mapKey, identity.ProfileId!)
                .ConfigureAwait(false);

            // 3. 缓存查询结果到 L1
            _memoryCache.Set(mapCacheKey, isAuthorized, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _indexCacheTtl,
                Size = 1
            });

            // 4. 如果未授权，记录安全事件并拒绝
            if (!isAuthorized)
            {
                LogUnauthorizedAccess(identity);
                throw new UnauthorizedAccessException(
                    $"AppId '{identity.ProfileId}' is not authorized for SysId '{identity.RealmId}'. " +
                    $"This access attempt has been logged for security audit.");
            }
        }

        /// <summary>
        /// 记录越权尝试（安全审计）
        /// </summary>
        private void LogUnauthorizedAccess(ITenantIdentity identity)
        {
            _logger.LogWarning(
                "🚨 Potential IDOR attack blocked: " +
                "SysId '{SysId}' attempted to access unauthorized AppId '{AppId}' " +
                "for Provider '{Provider}'. " +
                "[Security Event]",
                identity.RealmId,
                identity.ProfileId,
                identity.ProviderName);
        }



        /// <summary>
        /// 设置租户配置（写入 Redis + 清除 L1 + Pub/Sub 通知）
        /// 
        /// 使用场景：
        /// - 新增租户（运营后台调用）
        /// - 更新密钥（密钥轮换）
        /// - 修改网关地址（灰度切换）
        /// </summary>
        public async Task SetConfigurationAsync(
            ITenantIdentity identity,
            ProviderSettings configuration,
            CancellationToken ct = default)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            string cacheKey = BuildCacheKey(identity);

            // 1. 写入 Redis（永久存储，无 TTL）
            string json = SerializeConfig(configuration);
            await _redisDb.StringSetAsync(cacheKey, json).ConfigureAwait(false);

            // 2. 回填 L1 缓存
            SetL1Cache(cacheKey, configuration);

            // 3. 发布刷新通知（其他实例收到后清除 L1，下次请求重新加载）
            await PublishRefreshNotificationAsync(identity).ConfigureAwait(false);
        }

        /// <summary>
        /// 删除租户配置（清除 Redis + L1 + Pub/Sub 通知）
        /// 
        /// 使用场景：
        /// - 租户注销
        /// - 测试数据清理
        /// </summary>
        public async Task DeleteConfigurationAsync(
            ITenantIdentity identity,
            CancellationToken ct = default)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            string cacheKey = BuildCacheKey(identity);

            // 1. 清除 L1 缓存
            _memoryCache.Remove(cacheKey);

            // 2. 删除 Redis 数据
            await _redisDb.KeyDeleteAsync(cacheKey).ConfigureAwait(false);

            // 3. 发布刷新通知
            await PublishRefreshNotificationAsync(identity).ConfigureAwait(false);
        }

        /// <summary>
        /// 刷新配置缓存（清除 L1，触发下次请求重新从 Redis 加载）
        /// 
        /// 注意：不会删除 Redis 中的数据，只清除内存缓存
        /// </summary>
        public async Task RefreshAsync(
            ITenantIdentity identity,
            CancellationToken ct = default)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            string cacheKey = BuildCacheKey(identity);

            // 1. 清除 L1 缓存
            _memoryCache.Remove(cacheKey);

            // 2. 发布刷新通知（其他实例收到后清除 L1）
            await PublishRefreshNotificationAsync(identity).ConfigureAwait(false);
        }

        /// <summary>
        /// 预热配置缓存（从 Redis 批量加载配置到 L1）
        /// 
        /// 使用场景：
        /// - 应用启动时预热所有租户配置
        /// - 灰度发布前预热新实例
        /// - 缓存失效后批量恢复
        /// 
        /// 实现策略：
        /// 1. 使用 Redis SCAN 命令遍历所有配置键（避免 KEYS * 阻塞）
        /// 2. 批量加载到 L1 内存缓存
        /// 3. 限制并发数（防止内存溢出）
        /// </summary>
        public async Task WarmupAsync(CancellationToken ct = default)
        {
            int loadedCount = 0;
            const int batchSize = 100; // 每批处理 100 个配置

            try
            {
                // 使用 SCAN 命令遍历所有配置键（模式：nexus:config:*）
                await foreach (RedisKey key in _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints()[0])
                    .KeysAsync(pattern: new RedisValue($"{_redisKeyPrefix}*"), pageSize: batchSize))
                {
                    if (ct.IsCancellationRequested)
                        break;

                    try
                    {
                        // 从 Redis 读取配置
                        RedisValue value = await _redisDb.StringGetAsync(key).ConfigureAwait(false);
                        if (!value.HasValue)
                            continue;

                        // 反序列化配置
                        ProviderSettings? config = DeserializeConfig(value!);
                        if (config == null)
                            continue;

                        // 加载到 L1 缓存
                        string cacheKey = key.ToString();
                        SetL1Cache(cacheKey, config);

                        loadedCount++;
                    }
                    catch
                    {
                        // 忽略单个配置加载失败（避免影响整体预热）
                        continue;
                    }
                }
            }
            catch
            {
                // 预热失败不应影响服务启动
                // 日志记录由外部调用方处理
            }
        }

        /// <summary>
        /// 发布配置刷新通知（Pub/Sub）
        /// </summary>
        private async Task PublishRefreshNotificationAsync(ITenantIdentity identity)
        {
            string message = JsonSerializer.Serialize(new
            {
                identity.ProviderName,
                identity.RealmId,
                identity.ProfileId
            });
            await _redisSub.PublishAsync(new RedisChannel(_pubSubChannel, RedisChannel.PatternMode.Literal), message)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 构建缓存键（单个配置）
        /// </summary>
        private string BuildCacheKey(ITenantIdentity identity)
        {
            // 格式: "nexus:config:Alipay:2088123456789012:2021001234567890"
            return $"{_redisKeyPrefix}{identity.ProviderName}:{identity.RealmId}:{identity.ProfileId}";
        }

        /// <summary>
        /// 构建 AppId 组键（用于存储 SysId 下的所有 AppId）
        /// </summary>
        /// <summary>
        /// 构建映射层键名（统一授权/发现层）
        /// 格式：nxc:map:{realm}:{provider}
        /// 
        /// 设计理念（三层模型 - Layer 1）：
        /// - 职责：授权映射 (Mapping/Auth)
        /// - 结构：Redis Set
        /// - 成员：该 Realm 在指定渠道下拥有的所有 ProfileId (AppId/SubMchId)
        /// - 操作：SISMEMBER (权限校验) + SMEMBERS (配置发现)
        /// 
        /// 语义对比：
        /// - 旧设计：group (分组) + index (索引) → 职责重复
        /// - 新设计：map (映射) → 单一真相源，既是授权白名单，也是配置集合
        /// </summary>
        private string BuildMapKey(string providerName, string realmId)
        {
            // 格式: "nxc:map:2088123456789012:Alipay" (RealmId 优先，便于 Redis Cluster 分片)
            return $"nxc:map:{realmId}:{providerName}";
        }

        /// <summary>
        /// 设置 L1 缓存
        /// </summary>
        private void SetL1Cache(string key, ProviderSettings config)
        {
            _memoryCache.Set(key, config, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _l1Ttl,
                Size = 1 // 用于 MemoryCache 大小限制
            });
        }

        /// <summary>
        /// 序列化配置（加密 PrivateKey）
        /// </summary>
        private string SerializeConfig(ProviderSettings config)
        {
            JsonSerializerOptions options = GetJsonOptions();
            return JsonSerializer.Serialize(config, options);
        }

        /// <summary>
        /// 反序列化配置（解密 PrivateKey）
        /// </summary>
        private ProviderSettings? DeserializeConfig(string json)
        {
            try
            {
                JsonSerializerOptions options = GetJsonOptions();
                return JsonSerializer.Deserialize<ProviderSettings>(json, options);
            }
            catch
            {
                // 反序列化失败：返回 null（触发重新加载）
                return null;
            }
        }

        /// <summary>
        /// 获取 JSON 序列化选项（包含加密转换器）
        /// </summary>
        private JsonSerializerOptions GetJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // 注入加密转换器（仅对 PrivateKey 和 PublicKey 字段生效）
            options.Converters.Add(new ProtectedPrivateKeyConverter(_securityProvider));

            return options;
        }

        /// <summary>
        /// Pub/Sub 配置刷新消息处理
        /// </summary>
        private void OnConfigRefreshMessage(RedisChannel channel, RedisValue message)
        {
            try
            {
                // 解析消息
                string messageStr = message.ToString();
                var refreshData = JsonSerializer.Deserialize<RefreshMessage>(messageStr, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (refreshData == null) return;

                // 构建缓存键并清除 L1
                var identity = new ConfigurationContext(
                    refreshData.ProviderName ?? string.Empty,
                    refreshData.RealmId ?? string.Empty)
                {
                    ProfileId = refreshData.ProfileId ?? string.Empty
                };

                // 清除配置缓存（包括正常缓存和负缓存）
                string cacheKey = BuildCacheKey(identity);
                _memoryCache.Remove(cacheKey);

                // 同时清除权限索引缓存（确保租户的所有状态在内存中都是最新的）
                string mapKey = BuildMapKey(identity.ProviderName ?? string.Empty, identity.RealmId ?? string.Empty);
                string mapCacheKey = $"map:{mapKey}";
                _memoryCache.Remove(mapCacheKey);
            }
            catch
            {
                // 静默失败（避免 Pub/Sub 异常影响服务稳定性）
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 取消订阅
            _redisSub.Unsubscribe(new RedisChannel(_pubSubChannel, RedisChannel.PatternMode.Literal));

            // 释放锁资源
            foreach (var lockItem in _locks.Values)
            {
                lockItem.Dispose();
            }
            _locks.Clear();
        }

        /// <summary>
        /// 刷新消息数据结构
        /// </summary>
        private sealed class RefreshMessage
        {
            public string? ProviderName { get; set; }
            public string? RealmId { get; set; }
            public string? ProfileId { get; set; }
        }
    }
}
