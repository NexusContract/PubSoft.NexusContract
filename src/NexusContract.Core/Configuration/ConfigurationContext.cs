// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using NexusContract.Abstractions.Contracts;

namespace NexusContract.Core.Configuration
{
    /// <summary>
    /// 配置上下文：将业务身份映射为物理配置的查询凭证
    /// 
    /// 职责：
    /// - 封装配置查询的业务标识（ProviderName + RealmId + ProfileId）
    /// - 作为 IConfigurationResolver.ResolveAsync() 的输入参数
    /// - 支持扩展元数据（用于配置分片、多环境等场景）
    /// 
    /// 术语映射：
    /// - ProviderName: 渠道标识 (e.g., "Alipay")
    /// - RealmId: 域/归属权 (对应 SysId / SpMchId)
    /// - ProfileId: 档案/执行单元 (对应 AppId / SubMchId)
    /// 
    /// 使用场景：
    /// <code>
    /// // 在 Provider 中构造配置上下文
    /// var configCtx = new ConfigurationContext("Alipay", tenantCtx.RealmId)
    /// {
    ///     ProfileId = tenantCtx.ProfileId
    /// };
    /// 
    /// // 解析配置
    /// var settings = await _configResolver.ResolveAsync(configCtx, ct);
    /// </code>
    /// 
    /// 设计约束：
    /// - .NET Standard 2.0 兼容（不使用 required、init 等 C# 9+ 特性）
    /// - 强制构造函数校验（防止无效查询）
    /// </summary>
    public sealed class ConfigurationContext : ITenantIdentity
    {
        /// <summary>
        /// 渠道标识 (e.g., "Alipay", "WeChat", "UnionPay")
        /// 用于 IConfigurationResolver 路由到对应的配置源
        /// </summary>
        public string ProviderName { get; }

        /// <summary>
        /// 域/归属权 (对应 SysId / SpMchId)
        /// 必需字段，用于多租户配置隔离
        /// </summary>
        public string RealmId { get; }

        /// <summary>
        /// 档案/执行单元 (对应 AppId / SubMchId)
        /// 可选字段，某些场景下可能由 RealmId 推导
        /// </summary>
        public string ProfileId { get; set; }

        /// <summary>
        /// 扩展元数据（用于配置分片、多环境等）
        /// 例如：
        /// - Environment: "sandbox" / "production"
        /// - Region: "cn" / "us" / "eu"
        /// - BusinessLine: "b2c" / "b2b"
        /// </summary>
        public IDictionary<string, object> Metadata { get; set; }

        /// <summary>
        /// 构造配置上下文（必需参数）
        /// </summary>
        /// <param name="providerName">渠道标识（必需）</param>
        /// <param name="realmId">域/归属权（必需）</param>
        /// <exception cref="ArgumentNullException">必需参数为空</exception>
        public ConfigurationContext(string providerName, string realmId)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentNullException(nameof(providerName), "ProviderName cannot be null or empty");

            if (string.IsNullOrWhiteSpace(realmId))
                throw new ArgumentNullException(nameof(realmId), "RealmId cannot be null or empty");

            ProviderName = providerName;
            RealmId = realmId;
            ProfileId = string.Empty;
            Metadata = new Dictionary<string, object>();
        }

        /// <summary>
        /// 获取扩展元数据值（泛型版本）
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="key">元数据键</param>
        /// <param name="defaultValue">默认值（如果不存在）</param>
        /// <returns>元数据值</returns>
        public T GetMetadata<T>(string key, T defaultValue = default)
        {
            if (Metadata.TryGetValue(key, out object? value) && value is T typedValue)
                return typedValue;

            return defaultValue;
        }

        /// <summary>
        /// 检查是否包含指定的元数据键
        /// </summary>
        public bool HasMetadata(string key)
        {
            return Metadata.ContainsKey(key);
        }

        /// <summary>
        /// 添加元数据（流式 API）
        /// </summary>
        public ConfigurationContext WithMetadata(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            Metadata[key] = value;
            return this;
        }

        /// <summary>
        /// 设置 ProfileId（流式 API）
        /// </summary>
        public ConfigurationContext WithProfileId(string profileId)
        {
            ProfileId = profileId;
            return this;
        }

        /// <summary>
        /// 转换为字符串（用于日志）
        /// </summary>
        public override string ToString()
        {
            string profile = string.IsNullOrWhiteSpace(ProfileId) ? "N/A" : ProfileId;
            return $"ConfigurationContext[Provider={ProviderName}, Realm={RealmId}, Profile={profile}]";
        }

        /// <summary>
        /// 相等性比较（基于标识三元组）
        /// 注意：ProviderName 使用大小写不敏感比较（"Alipay" == "alipay"）
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is ConfigurationContext other)
            {
                return string.Equals(ProviderName, other.ProviderName, StringComparison.OrdinalIgnoreCase)
                    && RealmId == other.RealmId
                    && ProfileId == other.ProfileId;
            }
            return false;
        }

        /// <summary>
        /// 获取哈希码（用于字典键）
        /// 注意：ProviderName 使用大小写不敏感哈希（保证缓存命中）
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (ProviderName != null
                    ? StringComparer.OrdinalIgnoreCase.GetHashCode(ProviderName)
                    : 0);
                hash = hash * 31 + (RealmId?.GetHashCode() ?? 0);
                hash = hash * 31 + (ProfileId?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
