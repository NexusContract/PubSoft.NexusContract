// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;

namespace NexusContract.Abstractions.Contracts
{
    /// <summary>
    /// 租户上下文：ITenantIdentity 的标准实现
    /// 
    /// 职责：
    /// - 作为 ITenantIdentity 接口的具体载体
    /// - 在 Pipeline 中传递租户身份信息
    /// - 支持序列化/反序列化（日志、缓存、消息队列）
    /// 
    /// 设计原则：
    /// - 纯数据容器（POCO）：无业务逻辑
    /// - 可变对象：支持工厂模式和序列化框架
    /// - 零依赖：只使用 System.* 命名空间
    /// - 扩展性：通过 Metadata 字典支持临时数据
    /// 
    /// 使用场景：
    /// 1. TenantContextFactory 创建实例
    /// 2. Engine 传递给 Provider
    /// 3. 序列化到分布式追踪系统
    /// 4. 记录到审计日志
    /// 
    /// Metadata 用途：
    /// - TraceId：分布式追踪标识
    /// - ClientIp：客户端 IP 地址
    /// - UserAgent：客户端类型
    /// - RequestTime：请求时间戳
    /// - 其他临时上下文数据
    /// 
    /// 设计约束：
    /// - .NET Standard 2.0 兼容
    /// - 零外部依赖（纯 Abstractions）
    /// </summary>
    public class TenantContext : ITenantIdentity
    {
        /// <summary>
        /// 无参构造函数（支持序列化/反序列化）
        /// </summary>
        public TenantContext()
        {
            Metadata = new Dictionary<string, object>();
        }

        /// <summary>
        /// 快速构造函数
        /// </summary>
        /// <param name="realmId">域标识（sys_id / sp_mchid）</param>
        /// <param name="profileId">档案标识（app_id / sub_mchid）</param>
        /// <param name="providerName">Provider 标识（可选）</param>
        public TenantContext(string realmId, string profileId, string providerName = null)
        {
            RealmId = realmId ?? throw new ArgumentNullException(nameof(realmId));
            ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
            ProviderName = providerName;
            Metadata = new Dictionary<string, object>();
        }

        /// <inheritdoc />
        public string RealmId { get; set; }

        /// <inheritdoc />
        public string ProfileId { get; set; }

        /// <inheritdoc />
        public string ProviderName { get; set; }

        /// <summary>
        /// 扩展元数据字典
        /// 
        /// 用途：
        /// - 存放非身份信息（环境、追踪、临时数据）
        /// - Provider 特定参数（不适合放在接口中）
        /// - 分布式追踪上下文（TraceId, SpanId）
        /// 
        /// 示例：
        /// <code>
        /// context.Metadata["TraceId"] = "trace-12345";
        /// context.Metadata["ClientIp"] = "192.168.1.100";
        /// context.Metadata["Region"] = "cn-hangzhou";
        /// </code>
        /// 
        /// 注意：
        /// - 不要存放敏感信息（会被序列化）
        /// - 键名建议使用 PascalCase
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; }

        /// <summary>
        /// 便于调试的字符串表示
        /// </summary>
        public override string ToString()
        {
            string provider = string.IsNullOrEmpty(ProviderName) ? "Unknown" : ProviderName;
            return $"[{provider}] Realm:{RealmId}, Profile:{ProfileId}";
        }
    }
}
