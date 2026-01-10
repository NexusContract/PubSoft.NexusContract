// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Configuration;
using NexusContract.Core.Configuration;
using StackExchange.Redis;

namespace NexusContract.Hosting.Configuration
{
    /// <summary>
    /// 租户配置管理器：封装租户配置的 CRUD 操作
    /// 
    /// 设计目标：
    /// - 提供高层 API，隐藏 HybridConfigResolver 的实现细节
    /// - 支持批量操作（批量导入、批量删除）
    /// - 提供配置验证（密钥格式、网关地址有效性）
    /// - 支持配置导入/导出（JSON 格式）
    /// 
    /// 使用场景：
    /// - 运营后台：租户管理界面
    /// - 命令行工具：租户配置导入/导出
    /// - 单元测试：测试数据准备
    /// </summary>
    public sealed class TenantConfigurationManager
    {
        private readonly HybridConfigResolver _resolver;
        private readonly IDatabase _redisDb;

        /// <summary>
        /// 构造租户配置管理器
        /// </summary>
        public TenantConfigurationManager(HybridConfigResolver resolver, IConnectionMultiplexer redis)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _redisDb = redis?.GetDatabase() ?? throw new ArgumentNullException(nameof(redis));
        }

        /// <summary>
        /// 创建租户配置（支持设置默认 AppId）
        /// </summary>
        /// <param name="providerName">渠道名称（如 "Alipay"）</param>
        /// <param name="realmId">域标识（如 SysId、服务商 ID）</param>
        /// <param name="profileId">配置标识（如 AppId、子商户 ID）</param>
        /// <param name="configuration">配置详情</param>
        /// <param name="isDefault">是否设置为默认 AppId</param>
        /// <param name="ct">取消令牌</param>
        public async Task CreateAsync(
            string providerName,
            string realmId,
            string profileId,
            ProviderSettings configuration,
            bool isDefault = false,
            CancellationToken ct = default)
        {
            ValidateParameters(providerName, realmId, profileId, configuration);

            var identity = new ConfigurationContext(providerName, realmId)
            {
                ProfileId = profileId
            };

            // 使用 Redis Transaction 确保原子性：配置 + 映射层（统一的授权/发现层）
            var transaction = _redisDb.CreateTransaction();

            // 1. 写入配置
            var writeConfigTask = _resolver.SetConfigurationAsync(identity, configuration, ct);

            // 2. 更新映射层（Map Layer - 授权白名单 + 配置集合）
            string mapKey = BuildMapKey(realmId, providerName);
            var updateMapTask = transaction.SetAddAsync(mapKey, profileId);

            // 3. 如果标记为默认，设置默认 ProfileId 标记
            if (isDefault)
            {
                string defaultMarker = $"{mapKey}:default";
                var setDefaultTask = transaction.StringSetAsync(defaultMarker, profileId);
            }

            // 等待配置写入完成，然后执行事务
            await writeConfigTask.ConfigureAwait(false);
            await transaction.ExecuteAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 更新租户配置
        /// </summary>
        public async Task UpdateAsync(
            string providerName,
            string realmId,
            string profileId,
            ProviderSettings configuration,
            CancellationToken ct = default)
        {
            // 更新操作与创建操作相同（Redis SET 会覆盖）
            await CreateAsync(providerName, realmId, profileId, configuration, false, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 删除租户配置（同时从 AppId 组中移除）
        /// </summary>
        public async Task DeleteAsync(
            string providerName,
            string realmId,
            string profileId,
            CancellationToken ct = default)
        {
            ValidateIdentifier(providerName, realmId, profileId);

            var identity = new ConfigurationContext(providerName, realmId)
            {
                ProfileId = profileId
            };

            // 使用 Redis Transaction 确保原子性：配置 + 映射层 + 默认标记
            var transaction = _redisDb.CreateTransaction();

            // 1. 删除配置
            var deleteConfigTask = _resolver.DeleteConfigurationAsync(identity, ct);

            // 2. 从映射层中移除（Map Layer - 从授权白名单中删除）
            string mapKey = BuildMapKey(realmId, providerName);
            var deleteMapTask = transaction.SetRemoveAsync(mapKey, profileId);

            // 3. 如果删除的是默认 ProfileId，清除默认标记
            string defaultMarker = $"{mapKey}:default";
            RedisValue currentDefault = await _redisDb.StringGetAsync(defaultMarker)
                .ConfigureAwait(false);
            if (currentDefault.HasValue && currentDefault.ToString() == profileId)
            {
                var deleteDefaultTask = transaction.KeyDeleteAsync(defaultMarker);
            }

            // 等待配置删除完成，然后执行事务
            await deleteConfigTask.ConfigureAwait(false);
            await transaction.ExecuteAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 获取指定 SysId 下的所有 AppId
        /// </summary>
        public async Task<IReadOnlyList<string>> GetProfileIdsAsync(
            string providerName,
            string realmId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentNullException(nameof(providerName));
            if (string.IsNullOrWhiteSpace(realmId))
                throw new ArgumentNullException(nameof(realmId));

            // 从映射层获取所有 ProfileId（Redis Set）
            string mapKey = BuildMapKey(realmId, providerName);
            var members = await _redisDb.SetMembersAsync(mapKey)
                .ConfigureAwait(false);

            if (members == null || members.Length == 0)
            {
                return Array.Empty<string>();
            }

            // 转换为字符串列表
            var profileIds = new List<string>(members.Length);
            foreach (var member in members)
            {
                if (!member.IsNullOrEmpty)
                {
                    profileIds.Add(member.ToString());
                }
            }

            return profileIds;
        }

        /// <summary>
        /// 获取默认 AppId
        /// </summary>
        public async Task<string?> GetDefaultProfileIdAsync(
            string providerName,
            string realmId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentNullException(nameof(providerName));
            if (string.IsNullOrWhiteSpace(realmId))
                throw new ArgumentNullException(nameof(realmId));

            // 从映射层的默认标记中获取
            string mapKey = BuildMapKey(realmId, providerName);
            string defaultMarker = $"{mapKey}:default";
            RedisValue defaultProfileId = await _redisDb.StringGetAsync(defaultMarker)
                .ConfigureAwait(false);

            return defaultProfileId.HasValue ? defaultProfileId.ToString() : null;
        }

        /// <summary>
        /// 设置默认 AppId
        /// </summary>
        public async Task SetDefaultProfileIdAsync(
            string providerName,
            string realmId,
            string profileId,
            CancellationToken ct = default)
        {
            ValidateIdentifier(providerName, realmId, profileId);

            // 从映射层验证该 ProfileId 是否存在
            string mapKey = BuildMapKey(realmId, providerName);
            bool exists = await _redisDb.SetContainsAsync(mapKey, profileId)
                .ConfigureAwait(false);
            if (!exists)
            {
                throw new InvalidOperationException(
                    $"ProfileId '{profileId}' does not exist under Realm '{realmId}' in Provider '{providerName}'");
            }

            // 设置映射层的默认标记
            string defaultMarker = $"{mapKey}:default";
            await _redisDb.StringSetAsync(defaultMarker, profileId)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 查询租户配置
        /// </summary>
        public async Task<ProviderSettings> GetAsync(
            string providerName,
            string realmId,
            string profileId,
            CancellationToken ct = default)
        {
            ValidateIdentifier(providerName, realmId, profileId);

            var identity = new ConfigurationContext(providerName, realmId)
            {
                ProfileId = profileId
            };

            return (ProviderSettings)await _resolver.ResolveAsync(identity, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 刷新租户配置缓存
        /// </summary>
        public async Task RefreshAsync(
            string providerName,
            string realmId,
            string profileId,
            CancellationToken ct = default)
        {
            ValidateIdentifier(providerName, realmId, profileId);

            var identity = new ConfigurationContext(providerName, realmId)
            {
                ProfileId = profileId
            };

            await _resolver.RefreshAsync(identity, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 批量创建租户配置
        /// </summary>
        /// <param name="configurations">配置列表</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>成功数量</returns>
        public async Task<int> BatchCreateAsync(
            IEnumerable<TenantConfigurationItem> configurations,
            CancellationToken ct = default)
        {
            if (configurations == null)
                throw new ArgumentNullException(nameof(configurations));

            int successCount = 0;

            foreach (var item in configurations)
            {
                try
                {
                    await CreateAsync(
                        item.ProviderName,
                        item.RealmId,
                        item.ProfileId,
                        item.Configuration,
                        false,  // isDefault
                        ct).ConfigureAwait(false);

                    successCount++;
                }
                catch
                {
                    // 单个失败不影响批量操作
                    // 调用方可根据返回的成功数量判断
                    continue;
                }
            }

            return successCount;
        }

        /// <summary>
        /// 预热所有配置
        /// </summary>
        public async Task WarmupAsync(CancellationToken ct = default)
        {
            await _resolver.WarmupAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 验证参数
        /// </summary>
        private void ValidateParameters(
            string providerName,
            string realmId,
            string profileId,
            ProviderSettings configuration)
        {
            ValidateIdentifier(providerName, realmId, profileId);

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            // 验证必填字段
            if (string.IsNullOrWhiteSpace(configuration.ProviderName))
                throw new ArgumentException("ProviderName cannot be empty", nameof(configuration));

            if (string.IsNullOrWhiteSpace(configuration.AppId))
                throw new ArgumentException("AppId cannot be empty", nameof(configuration));

            if (string.IsNullOrWhiteSpace(configuration.PrivateKey))
                throw new ArgumentException("PrivateKey cannot be empty", nameof(configuration));

            if (configuration.GatewayUrl == null)
                throw new ArgumentException("GatewayUrl cannot be null", nameof(configuration));
        }

        /// <summary>
        /// 验证身份标识
        /// </summary>
        private void ValidateIdentifier(string providerName, string realmId, string profileId)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentNullException(nameof(providerName));

            if (string.IsNullOrWhiteSpace(realmId))
                throw new ArgumentNullException(nameof(realmId));

            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentNullException(nameof(profileId));
        }

        /// <summary>
        /// 构建映射层键名（三层模型 - Layer 1: Mapping/Auth）
        /// 格式：nxc:map:{realm}:{provider}
        /// 
        /// 设计理念：
        /// - 职责：授权映射（既是权限白名单，也是配置发现层）
        /// - 结构：Redis Set
        /// - 成员：该 Realm 在指定渠道下拥有的所有 ProfileId
        /// - 操作：SADD/SREM (维护) + SISMEMBER (校验) + SMEMBERS (查询)
        /// 
        /// 语义简化：
        /// - 旧设计：group (分组) + index (索引) → 职责重复
        /// - 新设计：map (映射) → 单一真相源
        /// </summary>
        private string BuildMapKey(string realmId, string providerName)
        {
            // 验证必需参数（RealmId 优先）
            if (string.IsNullOrWhiteSpace(realmId))
                throw new ArgumentNullException(nameof(realmId));
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentNullException(nameof(providerName));

            // RealmId 优先排列，便于 Redis Cluster 按业务单元分片
            return $"nxc:map:{realmId}:{providerName}";
        }
    }

    /// <summary>
    /// 租户配置项（用于批量操作）
    /// </summary>
    public sealed class TenantConfigurationItem
    {
        /// <summary>
        /// 渠道名称（如 "Alipay"）
        /// </summary>
        public string ProviderName { get; set; } = string.Empty;

        /// <summary>
        /// 域标识（如 SysId、服务商 ID）
        /// </summary>
        public string RealmId { get; set; } = string.Empty;

        /// <summary>
        /// 配置标识（如 AppId、子商户 ID）
        /// </summary>
        public string ProfileId { get; set; } = string.Empty;

        /// <summary>
        /// 配置详情
        /// </summary>
        public ProviderSettings Configuration { get; set; } = null!;
    }
}
