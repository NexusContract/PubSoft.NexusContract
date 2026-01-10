// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
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
    /// 混合配置解析器：L1（内存）+ L2（Redis）双层缓存架构
    /// 
    /// 架构设计：
    /// - L1 缓存：ConcurrentDictionary（进程内，5 分钟 TTL）
    /// - L2 缓存：Redis（跨实例共享，30 分钟 TTL）
    /// - L3 数据源：ITenantRepository（数据库，按需查询）
    /// 
    /// 缓存策略：
    /// 1. 查询时：L1 → L2 → L3（逐层回填）
    /// 2. 刷新时：清除 L1 + L2，触发 Pub/Sub 通知其他实例
    /// 3. 预热时：批量加载高频配置到 L1/L2
    /// 
    /// 性能特征：
    /// - L1 命中：&lt;1μs（纯内存）
    /// - L2 命中：~1ms（Redis 网络开销）
    /// - L3 查询：~10ms（数据库延迟）
    /// - 缓存击穿保护：SemaphoreSlim 防止并发查询数据库
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
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;
        private readonly string _redisKeyPrefix;
        private readonly string _pubSubChannel;
        private readonly TimeSpan _l1Ttl;
        private readonly TimeSpan _l2Ttl;

        /// <summary>
        /// L1 缓存 TTL（默认 5 分钟）
        /// </summary>
        private static readonly TimeSpan DefaultL1Ttl = TimeSpan.FromMinutes(5);

        /// <summary>
        /// L2 缓存 TTL（默认 30 分钟）
        /// </summary>
        private static readonly TimeSpan DefaultL2Ttl = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Redis 键前缀（默认）
        /// </summary>
        private const string DefaultKeyPrefix = "nexus:config:";

        /// <summary>
        /// Pub/Sub 通道名称（默认）
        /// </summary>
        private const string DefaultPubSubChannel = "nexus:config:refresh";

        /// <summary>
        /// 构造混合配置解析器
        /// </summary>
        /// <param name="redis">Redis 连接复用器</param>
        /// <param name="memoryCache">内存缓存实例</param>
        /// <param name="securityProvider">安全提供程序（用于私钥加解密）</param>
        /// <param name="redisKeyPrefix">Redis 键前缀（可选）</param>
        /// <param name="l1Ttl">L1 缓存 TTL（可选）</param>
        /// <param name="l2Ttl">L2 缓存 TTL（可选）</param>
        public HybridConfigResolver(
            IConnectionMultiplexer redis,
            IMemoryCache memoryCache,
            ISecurityProvider securityProvider,
            string? redisKeyPrefix = null,
            TimeSpan? l1Ttl = null,
            TimeSpan? l2Ttl = null)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _securityProvider = securityProvider ?? throw new ArgumentNullException(nameof(securityProvider));
            _redisDb = redis.GetDatabase();
            _redisSub = redis.GetSubscriber();
            _locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            _redisKeyPrefix = redisKeyPrefix ?? DefaultKeyPrefix;
            _pubSubChannel = DefaultPubSubChannel;
            _l1Ttl = l1Ttl ?? DefaultL1Ttl;
            _l2Ttl = l2Ttl ?? DefaultL2Ttl;

            // 订阅配置刷新通知
            _redisSub.Subscribe(new RedisChannel(_pubSubChannel, RedisChannel.PatternMode.Literal), OnConfigRefreshMessage);
        }

        /// <summary>
        /// JIT 解析配置（L1 → L2 → L3 逐层查询）
        /// </summary>
        public async Task<IProviderConfiguration> ResolveAsync(
            ITenantIdentity identity,
            CancellationToken ct = default)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            string cacheKey = BuildCacheKey(identity);

            // 1. 尝试 L1 缓存（内存）
            if (_memoryCache.TryGetValue(cacheKey, out ProviderSettings? l1Config) && l1Config != null)
            {
                return l1Config;
            }

            // 2. 缓存击穿保护（SemaphoreSlim）
            SemaphoreSlim cacheLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await cacheLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 双重检查：可能其他线程已加载
                if (_memoryCache.TryGetValue(cacheKey, out l1Config) && l1Config != null)
                {
                    return l1Config;
                }

                // 3. 尝试 L2 缓存（Redis）
                RedisValue l2Value = await _redisDb.StringGetAsync(cacheKey).ConfigureAwait(false);
                if (l2Value.HasValue)
                {
                    ProviderSettings? l2Config = DeserializeConfig(l2Value!);
                    if (l2Config != null)
                    {
                        // 回填 L1 缓存
                        SetL1Cache(cacheKey, l2Config);
                        return l2Config;
                    }
                }

                // 4. L3 数据源查询（数据库）
                // TODO: 集成 ITenantRepository
                // var config = await _tenantRepository.GetConfigurationAsync(identity, ct);

                // 暂时抛出异常（等待数据源集成）
                throw NexusTenantException.NotFound(
                    $"{identity.ProviderName}:{identity.RealmId}:{identity.ProfileId}");
            }
            finally
            {
                cacheLock.Release();
            }
        }

        /// <summary>
        /// 刷新配置缓存（清除 L1 + L2 + Pub/Sub 通知）
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

            // 2. 清除 L2 缓存
            await _redisDb.KeyDeleteAsync(cacheKey).ConfigureAwait(false);

            // 3. 发布刷新通知（其他实例收到后清除 L1）
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
        /// 预热配置缓存（批量加载高频配置）
        /// </summary>
        public Task WarmupAsync(CancellationToken ct = default)
        {
            // TODO: 实现批量预热逻辑
            // 1. 从数据库查询高频配置列表
            // 2. 批量加载到 L1 + L2 缓存
            return Task.CompletedTask;
        }

        /// <summary>
        /// 构建缓存键
        /// </summary>
        private string BuildCacheKey(ITenantIdentity identity)
        {
            // 格式: "nexus:config:Alipay:2088123456789012:2021001234567890"
            return $"{_redisKeyPrefix}{identity.ProviderName}:{identity.RealmId}:{identity.ProfileId}";
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
        /// 设置 L2 缓存
        /// </summary>
        private async Task SetL2CacheAsync(string key, ProviderSettings config)
        {
            string json = SerializeConfig(config);
            await _redisDb.StringSetAsync(key, json, _l2Ttl).ConfigureAwait(false);
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

                string cacheKey = BuildCacheKey(identity);
                _memoryCache.Remove(cacheKey);
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
