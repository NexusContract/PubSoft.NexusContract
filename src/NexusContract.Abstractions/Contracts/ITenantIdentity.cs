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
    /// - 构成配置查询的三维坐标（Provider + Realm + Profile）
    /// 
    /// 术语映射（渠道适配层）：
    /// - **RealmId**: 域标识（权属/隔离边界）
    ///   * Alipay: sys_id (服务商模式) / partner_id (合作伙伴模式)
    ///   * WeChat: sp_mchid (服务商商户号) - 服务商自身的商户号
    ///   * UnionPay: inst_id (机构号)
    ///   * 业务场景: 特约商户、POS 设备、小程序等的归属方
    /// 
    /// - **ProfileId**: 档案标识（业务实例/子账户）
    ///   * Alipay: app_id (应用ID) - ISV 应用或特约商户专属应用
    ///   * WeChat: sub_mchid (特约商户号) - 服务商下挂的子商户
    ///   * UnionPay: mer_id (商户号)
    ///   * 业务场景: 具体的设备号、门店号、应用实例
    /// 
    /// - **ProviderName**: 渠道标识（路由/协议选择）
    ///   * 必填字段（在 Redis-First 架构中用于构造配置键）
    ///   * 合法值: "Alipay", "WeChat", "UnionPay"
    ///   * 如果为空，某些场景可通过 OperationId 推断（但不推荐）
    /// 
    /// 使用场景：
    /// - 直连模式: RealmId = app_id, ProfileId = null/same（服务商本身应用）
    /// - 特约商户: RealmId = sp_mchid, ProfileId = sub_mchid（WeChat 特约商户）
    /// - ISV 应用: RealmId = sys_id, ProfileId = app_id（Alipay ISV 应用）
    /// 
    /// 设计约束：
    /// - 纯接口定义，无实现逻辑
    /// - .NET Standard 2.0 兼容
    /// - 零外部依赖
    /// </summary>
    public interface ITenantIdentity
    {
        /// <summary>
        /// 域标识（Realm ID）- 权属/隔离边界
        /// 
        /// 术语映射：
        /// - Alipay: sys_id (服务商系统ID)
        /// - WeChat: sp_mchid (服务商商户号)
        /// - 业务含义: 标识一个逻辑隔离的业务领域（服务商、机构、代理商）
        /// </summary>
        string RealmId { get; }

        /// <summary>
        /// 档案标识（Profile ID）- 业务实例/子账户
        /// 
        /// 术语映射：
        /// - Alipay: app_id (应用ID)
        /// - WeChat: sub_mchid (特约商户号)
        /// - 业务含义: 标识 Realm 下的具体业务实例（子商户、应用、设备）
        /// 
        /// 可选性说明：
        /// - 某些直连场景下可能为 null（如服务商本身应用）
        /// - 配置解析时可通过默认规则自动补全（参见 HybridConfigResolver.ResolveDefaultProfileAsync）
        /// </summary>
        string ProfileId { get; }

        /// <summary>
        /// Provider 标识（用于路由/协议选择）
        /// 
        /// 合法值: "Alipay", "WeChat", "UnionPay"
        /// 
        /// 必填性说明：
        /// - **在 Redis-First 架构中为必填**（用于构造配置键: nexus:config:{provider}:{realm}:{profile}）
        /// - 某些场景可通过 OperationId 推断（但会降低配置查询的确定性，不推荐）
        /// - 建议在 TenantContextFactory 中强制校验非空
        /// 
        /// 用途：
        /// - 配置键构造（Map/Inst/Pool 三层模型）
        /// - Provider 路由选择（NexusEngine.DispatchAsync）
        /// - 安全审计日志（识别渠道）
        /// </summary>
        string ProviderName { get; }
    }
}
