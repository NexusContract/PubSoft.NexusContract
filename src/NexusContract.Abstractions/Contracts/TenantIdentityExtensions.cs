// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace NexusContract.Abstractions.Contracts
{
    /// <summary>
    /// ITenantIdentity 扩展方法：提供便捷的身份标识格式化能力
    /// 
    /// 设计目标：
    /// - 生成标准化的身份标识字符串（用于缓存 key、日志、追踪）
    /// - 避免在各处重复实现相同的格式化逻辑
    /// - 保证身份标识的全局一致性
    /// 
    /// 使用场景：
    /// - L1/L2 缓存键：_memoryCache.Set(identity.GetIdentityTag(), config, ...)
    /// - 日志记录：_logger.LogInformation("Resolving config for {Identity}", identity.GetIdentityTag())
    /// - 分布式追踪：span.SetTag("tenant.identity", identity.GetIdentityTag())
    /// - Redis 键构造：$"nexus:config:{identity.GetIdentityTag(':')}"
    /// 
    /// 设计约束：
    /// - .NET Standard 2.0 兼容
    /// - 零外部依赖（纯 System.* 命名空间）
    /// </summary>
    public static class TenantIdentityExtensions
    {
        /// <summary>
        /// 获取身份标识标签（格式：Provider:Realm:Profile）
        /// 
        /// 生成规则：
        /// - 标准格式：{ProviderName}:{RealmId}:{ProfileId}
        /// - ProviderName 为空时：Unknown:{RealmId}:{ProfileId}
        /// - ProfileId 为空时：{ProviderName}:{RealmId}
        /// 
        /// 示例输出：
        /// - Alipay:2021001234:2088123456789012
        /// - WeChat:1234567890:1900123456
        /// - Alipay:2021001234 (ProfileId 为 null 时)
        /// 
        /// 用途：
        /// - 缓存键：适用于 L1 MemoryCache、L2 Redis
        /// - 日志标识：结构化日志的 Identity 字段
        /// - 追踪标签：分布式追踪的 tenant.identity 属性
        /// </summary>
        /// <param name="identity">租户身份</param>
        /// <param name="separator">分隔符（默认为冒号）</param>
        /// <returns>格式化的身份标识字符串</returns>
        /// <exception cref="ArgumentNullException">identity 为 null 时抛出</exception>
        public static string GetIdentityTag(this ITenantIdentity identity, char separator = ':')
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            string provider = string.IsNullOrEmpty(identity.ProviderName) 
                ? "Unknown" 
                : identity.ProviderName;

            string realm = identity.RealmId ?? "null";

            // ProfileId 为空时省略（对应"直连模式"或"默认 Profile"场景）
            if (string.IsNullOrEmpty(identity.ProfileId))
            {
                return $"{provider}{separator}{realm}";
            }

            return $"{provider}{separator}{realm}{separator}{identity.ProfileId}";
        }

        /// <summary>
        /// 获取简化的身份标识（仅包含 Realm:Profile）
        /// 
        /// 用途：
        /// - 日志输出（不关心渠道时）
        /// - 业务层标识（Provider 已知的上下文）
        /// 
        /// 示例输出：
        /// - 2021001234:2088123456789012
        /// - 2021001234 (ProfileId 为 null 时)
        /// </summary>
        public static string GetShortIdentityTag(this ITenantIdentity identity, char separator = ':')
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            string realm = identity.RealmId ?? "null";

            if (string.IsNullOrEmpty(identity.ProfileId))
            {
                return realm;
            }

            return $"{realm}{separator}{identity.ProfileId}";
        }

        /// <summary>
        /// 验证身份标识的完整性（用于防御性编程）
        /// 
        /// 验证规则：
        /// - ProviderName 必需（Redis-First 架构要求）
        /// - RealmId 必需（多租户隔离要求）
        /// - ProfileId 可选（某些场景可通过默认规则补全）
        /// 
        /// 使用场景：
        /// - TenantContextFactory 创建后校验
        /// - HybridConfigResolver 解析前校验
        /// - API 层参数校验
        /// </summary>
        /// <param name="identity">租户身份</param>
        /// <param name="requireProfile">是否强制要求 ProfileId</param>
        /// <returns>验证结果</returns>
        public static bool IsValid(this ITenantIdentity identity, bool requireProfile = false)
        {
            if (identity == null)
                return false;

            // ProviderName 必需（Redis-First 架构中用于构造配置键）
            if (string.IsNullOrWhiteSpace(identity.ProviderName))
                return false;

            // RealmId 必需（多租户隔离的核心标识）
            if (string.IsNullOrWhiteSpace(identity.RealmId))
                return false;

            // ProfileId 可选（某些场景可通过默认规则补全）
            if (requireProfile && string.IsNullOrWhiteSpace(identity.ProfileId))
                return false;

            return true;
        }

        /// <summary>
        /// 获取用于 Redis 键构造的路径片段
        /// 
        /// 生成规则：
        /// - 格式：{provider}/{realm}/{profile}
        /// - 用于构造 Redis 键：$"nexus:config:{identity.GetRedisPath()}"
        /// 
        /// 示例输出：
        /// - alipay/2021001234/2088123456789012
        /// - wechat/1234567890/1900123456
        /// 
        /// 用途：
        /// - Redis 键构造（Map/Inst/Pool 层）
        /// - 文件路径构造（配置导出场景）
        /// </summary>
        public static string GetRedisPath(this ITenantIdentity identity)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            string provider = string.IsNullOrEmpty(identity.ProviderName)
                ? "unknown"
                : identity.ProviderName.ToLowerInvariant();

            string realm = identity.RealmId ?? "null";

            if (string.IsNullOrEmpty(identity.ProfileId))
            {
                return $"{provider}/{realm}";
            }

            return $"{provider}/{realm}/{identity.ProfileId}";
        }
    }
}
