// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace NexusContract.Abstractions.Contracts
{
    /// <summary>
    /// 租户身份抽象：ISV 多租户场景的最小身份标识
    /// 
    /// 职责：
    /// - 定义租户唯一标识（RealmId + ProfileId）
    /// - 支持跨平台术语映射（Alipay/WeChat/UnionPay）
    /// - 提供 Provider 路由信息
    /// 
    /// 术语映射：
    /// - RealmId: 域标识
    ///   * Alipay: sys_id (服务商模式) / partner_id
    ///   * WeChat: sp_mchid (服务商商户号)
    ///   * UnionPay: inst_id (机构号)
    /// 
    /// - ProfileId: 档案标识
    ///   * Alipay: app_id (应用ID)
    ///   * WeChat: sub_mchid (特约商户号)
    ///   * UnionPay: mer_id (商户号)
    /// 
    /// 设计约束：
    /// - 纯接口定义，无实现逻辑
    /// - .NET Standard 2.0 兼容
    /// - 零外部依赖
    /// </summary>
    public interface ITenantIdentity
    {
        /// <summary>
        /// 域标识（Realm ID）
        /// 对应 Alipay 的 sys_id 或 WeChat 的 sp_mchid
        /// </summary>
        string RealmId { get; }

        /// <summary>
        /// 档案标识（Profile ID）
        /// 对应 Alipay 的 app_id 或 WeChat 的 sub_mchid
        /// </summary>
        string ProfileId { get; }

        /// <summary>
        /// Provider 标识（用于路由）
        /// 例如: "Alipay", "WeChat", "UnionPay"
        /// 可选字段，如果为空则通过 OperationId 推断
        /// </summary>
        string ProviderName { get; }
    }
}
