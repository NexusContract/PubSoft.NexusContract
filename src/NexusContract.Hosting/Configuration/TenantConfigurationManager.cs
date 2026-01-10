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
        private readonly string _keyPrefix;

        /// <summary>
        /// 构造租户配置管理器
        /// </summary>
        public TenantConfigurationManager(HybridConfigResolver resolver, IConnectionMultiplexer redis)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _redisDb = redis?.GetDatabase() ?? throw new ArgumentNullException(nameof(redis));
            _keyPrefix = "nexus:config:";
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

            // 1. 写入配置
            await _resolver.SetConfigurationAsync(identity, configuration, ct)
                .ConfigureAwait(false);

            // 2. 更新 AppId 组索引
            string groupKey = BuildGroupKey(providerName, realmId);
            await _redisDb.HashSetAsync(groupKey, profileId, DateTime.UtcNow.ToString("O"))
                .ConfigureAwait(false);

            // 3. 如果标记为默认，设置默认 AppId
            if (isDefault)
            {
                await _redisDb.HashSetAsync(groupKey, "default", profileId)
                    .ConfigureAwait(false);
            }
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

            // 1. 删除配置
            await _resolver.DeleteConfigurationAsync(identity, ct)
                .ConfigureAwait(false);

            // 2. 从 AppId 组中移除
            string groupKey = BuildGroupKey(providerName, realmId);
            await _redisDb.HashDeleteAsync(groupKey, profileId)
                .ConfigureAwait(false);

            // 3. 如果删除的是默认 AppId，清除默认标记
            RedisValue currentDefault = await _redisDb.HashGetAsync(groupKey, "default")
                .ConfigureAwait(false);
            if (currentDefault.HasValue && currentDefault.ToString() == profileId)
            {
                await _redisDb.HashDeleteAsync(groupKey, "default")
                    .ConfigureAwait(false);
            }
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

            string groupKey = BuildGroupKey(providerName, realmId);
            HashEntry[] entries = await _redisDb.HashGetAllAsync(groupKey)
                .ConfigureAwait(false);

            // 排除 "default" 键
            return entries
                .Where(e => e.Name != "default")
                .Select(e => e.Name.ToString())
                .ToList();
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

            string groupKey = BuildGroupKey(providerName, realmId);
            RedisValue defaultAppId = await _redisDb.HashGetAsync(groupKey, "default")
                .ConfigureAwait(false);

            return defaultAppId.HasValue ? defaultAppId.ToString() : null;
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

            string groupKey = BuildGroupKey(providerName, realmId);

            // 验证该 AppId 是否存在
            bool exists = await _redisDb.HashExistsAsync(groupKey, profileId)
                .ConfigureAwait(false);
            if (!exists)
            {
                throw new InvalidOperationException(
                    $"AppId '{profileId}' does not exist under {providerName}:{realmId}");
            }

            // 设置默认 AppId
            await _redisDb.HashSetAsync(groupKey, "default", profileId)
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
        /// 构建 AppId 组键
        /// </summary>
        private string BuildGroupKey(string providerName, string realmId)
        {
            return $"{_keyPrefix}group:{providerName}:{realmId}";
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
