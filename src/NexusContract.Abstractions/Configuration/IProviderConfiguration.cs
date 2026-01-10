// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace NexusContract.Abstractions.Configuration
{
    /// <summary>
    /// Provider 配置抽象：物理配置的只读视图
    /// 
    /// 职责：
    /// - 提供 Provider 执行所需的物理配置（密钥、网关地址等）
    /// - 支持运行时动态配置（JIT 模式）
    /// - 隐藏实现细节（配置来源、缓存策略）
    /// 
    /// 安全约束：
    /// - 私钥字段只能通过接口访问，实现类负责加密存储
    /// - 禁止序列化到日志或响应
    /// 
    /// 设计约束：
    /// - 纯接口定义，只读属性
    /// - .NET Standard 2.0 兼容
    /// - 零外部依赖
    /// </summary>
    public interface IProviderConfiguration
    {
        /// <summary>
        /// Provider 标识（如 "Alipay", "WeChat"）
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// 应用ID（对应 Alipay 的 AppId / WeChat 的 MchId）
        /// </summary>
        string AppId { get; }

        /// <summary>
        /// 商户ID（对应 Alipay 的 MerchantId / WeChat 的 SpMchId）
        /// </summary>
        string MerchantId { get; }

        /// <summary>
        /// 商户私钥（用于签名请求）
        /// ⚠️ 敏感字段：必须加密存储，禁止序列化到日志
        /// </summary>
        string PrivateKey { get; }

        /// <summary>
        /// 平台公钥（用于验证响应）
        /// </summary>
        string PublicKey { get; }

        /// <summary>
        /// API 网关地址
        /// </summary>
        Uri GatewayUrl { get; }

        /// <summary>
        /// 是否沙箱环境
        /// </summary>
        bool IsSandbox { get; }

        /// <summary>
        /// 获取扩展配置值（平台特定参数）
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值</returns>
        T GetExtendedSetting<T>(string key, T defaultValue = default);

        /// <summary>
        /// 检查是否包含指定的扩展配置键
        /// </summary>
        bool HasExtendedSetting(string key);
    }
}
