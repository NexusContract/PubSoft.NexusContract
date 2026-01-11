// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Core.Configuration
{
    /// <summary>
    /// 配置上下文：物理配置查询的数据载体（v1.0 宪法版本）
    ///
    /// 职责：
    /// - 封装配置查询的物理标识（ProviderName + ProfileId）
    /// - 支持扩展元数据（用于配置分片、多环境等场景）
    ///
    /// 设计约束（宪法）：
    /// - 宪法 002（URL 资源寻址）：ProviderName 来自 URL 路径
    /// - 宪法 003（物理槽位隔离）：ProfileId 来自 URL 参数（绝对权威）
    /// - 宪法 007（性能优先）：消除抽象包装，直接使用字符串参数
    /// - .NET Standard 2.0 兼容（不使用 required、init 等 C# 9+ 特性）
    /// - 强制构造函数校验（防止无效查询）
    ///
    /// 术语映射（物理寻址）：
    /// - **ProviderName**: 渠道标识 (e.g., "Alipay", "WeChat")
    ///   * 用于 Redis 键路由和协议选择
    ///   * 必填字段（Redis-First 架构要求）
    ///
    /// - **ProfileId**: 档案/执行单元（Profile = 物理配置实例）
    ///   * Alipay: app_id (应用ID)
    ///   * WeChat: sub_mchid (特约商户号)
    ///   * 业务含义：具体的配置实例标识
    ///   * 必填字段：v1.0 要求显式指定
    ///
    /// 使用场景：
    /// <code>
    /// // v1.0 方式：直接使用字符串参数
    /// var settings = await _configResolver.ResolveAsync(providerName, profileId, ct);
    ///
    /// // 可选：构造上下文用于扩展元数据
    /// var context = new ConfigurationContext(providerName, profileId)
    ///     .WithMetadata("environment", "production");
    /// </code>
    /// </summary>
    public sealed class ConfigurationContext
    {
        /// <summary>
        /// 渠道标识 (e.g., "Alipay", "WeChat", "UnionPay")
        /// 用于 IConfigurationResolver 路由到对应的配置源
        /// 必填字段（Redis-First 架构中用于构造配置键）
        /// </summary>
        public string ProviderName { get; }

        /// <summary>
        /// 档案/执行单元（Profile ID）
        /// - Alipay: app_id (应用ID)
        /// - WeChat: sub_mchid (特约商户号)
        ///
        /// 必填字段，v1.0 要求显式指定（宪法 003）
        /// </summary>
        public string ProfileId { get; }

        /// <summary>
        /// 扩展元数据（用于配置分片、多环境等）
        /// 例如：
        /// - Environment: "sandbox" / "production"
        /// - Region: "cn" / "us" / "eu"
        /// - BusinessLine: "b2c" / "b2b"
        /// </summary>
        // 默认不分配内存，只有用到时才创建
        private IDictionary<string, object>? _metadata;
        public IDictionary<string, object> Metadata 
        { 
            get => _metadata ??= new Dictionary<string, object>();
            set => _metadata = value;
        }

        /// <summary>
        /// 构造配置上下文（必需参数）
        /// </summary>
        /// <param name="providerName">渠道标识（必需）</param>
        /// <param name="profileId">档案标识（必需）</param>
        /// <exception cref="ContractIncompleteException">NXC201 - 物理寻址参数缺失</exception>
        public ConfigurationContext(string providerName, string profileId)
        {
            // 宪法 012（诊断主权）：统一出口，统一抛出 NXC201
            // 宪法 007（性能主权）：不在热路径上重复造轮子
            NexusGuard.EnsurePhysicalAddress(providerName, profileId, nameof(ConfigurationContext));

            ProviderName = providerName;
            ProfileId = profileId;
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
            if (_metadata != null && _metadata.TryGetValue(key, out object? value) && value is T typedValue)
                return typedValue;

            return defaultValue;
        }

        /// <summary>
        /// 检查是否包含指定的元数据键
        /// </summary>
        public bool HasMetadata(string key)
        {
            return _metadata != null && _metadata.ContainsKey(key);
        }

        /// <summary>
        /// 添加元数据（流式 API）
        /// </summary>
        public ConfigurationContext WithMetadata(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                NexusGuard.EnsureNonEmptyString(key);

            Metadata[key] = value;
            return this;
        }

        /// <summary>
        /// 转换为字符串（用于日志）
        /// </summary>
        public override string ToString()
        {
            return $"ConfigurationContext[Provider={ProviderName}, Profile={ProfileId}]";
        }

        /// <summary>
        /// 相等性比较（基于标识二元组）
        /// 注意：ProviderName 使用大小写不敏感比较（"Alipay" == "alipay"）
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is ConfigurationContext other)
            {
                return string.Equals(ProviderName, other.ProviderName, StringComparison.OrdinalIgnoreCase)
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
                hash = hash * 31 + (ProfileId?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
