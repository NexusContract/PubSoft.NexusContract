// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using NexusContract.Abstractions.Configuration;

namespace NexusContract.Core.Configuration
{
    /// <summary>
    /// Provider 物理配置（含私钥）：ISV 多租户场景的配置实体
    /// 
    /// 职责：
    /// - 封装支付平台的物理配置（密钥、证书、网关地址）
    /// - 支持运行时动态加载（JIT 模式）
    /// - 提供配置完整性校验
    /// 
    /// 安全约束：
    /// - 私钥字段必须加密存储（AES256）
    /// - 禁止序列化到日志或响应
    /// - 禁止传递给 URL Builder（防止泄露）
    /// 
    /// 配置来源：
    /// - 数据库：ITenantRepository.GetAsync()
    /// - Redis：L2 缓存（System.Text.Json 序列化）
    /// - 内存：L1 缓存
    /// - 配置文件：appsettings.json（仅开发环境）
    /// 
    /// 设计约束：
    /// - .NET Standard 2.0 兼容（不使用 required、init 等 C# 9+ 特性）
    /// - 不可变对象（属性只读，但有 private set 支持反序列化）
    /// - 强制构造函数校验（防止无效配置）
    /// </summary>
    public sealed class ProviderSettings : IProviderConfiguration
    {
        /// <summary>
        /// Provider 标识（如 "Alipay", "WeChat", "UnionPay"）
        /// </summary>
        public string ProviderName { get; private set; }

        /// <summary>
        /// 应用ID（对应 Alipay 的 AppId / WeChat 的 MchId）
        /// </summary>
        public string AppId { get; private set; }

        /// <summary>
        /// 商户ID（对应 Alipay 的 MerchantId / WeChat 的 SpMchId）
        /// </summary>
        public string MerchantId { get; private set; }

        /// <summary>
        /// 商户私钥（用于签名请求）
        /// ⚠️ 敏感字段：必须加密存储，禁止序列化到日志
        /// </summary>
        public string PrivateKey { get; private set; }

        /// <summary>
        /// 平台公钥（用于验证响应）
        /// Alipay: 支付宝 RSA 公钥
        /// WeChat: 平台证书（Wechatpay-Serial）
        /// </summary>
        public string PublicKey { get; private set; }

        /// <summary>
        /// API 网关地址
        /// Alipay: https://openapi.alipay.com/ 或 https://openapi-sandbox.dl.alipaydev.com/
        /// WeChat: https://api.mch.weixin.qq.com/
        /// </summary>
        public Uri GatewayUrl { get; private set; }

        /// <summary>
        /// 是否沙箱环境
        /// </summary>
        public bool IsSandbox { get; private set; }

        /// <summary>
        /// 扩展配置（平台特定参数）
        /// 例如：
        /// - Alipay: sign_type (RSA2), charset (UTF-8), format (JSON)
        /// - WeChat: api_key (用于 V2 签名), serial_no (证书序列号)
        /// </summary>
        public IReadOnlyDictionary<string, object> ExtendedSettings { get; private set; }

        /// <summary>
        /// 无参构造函数（支持 System.Text.Json 反序列化）
        /// </summary>
        public ProviderSettings()
        {
            // 反序列化后需要手动调用 Validate 确保完整性
            ExtendedSettings = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());
            ProviderName = string.Empty;
            AppId = string.Empty;
            MerchantId = string.Empty;
            PrivateKey = string.Empty;
            PublicKey = string.Empty;
            GatewayUrl = new Uri("http://localhost");
        }

        /// <summary>
        /// 构造 Provider 配置（完整参数）
        /// </summary>
        /// <param name="providerName">Provider 标识（必需）</param>
        /// <param name="appId">应用ID（必需）</param>
        /// <param name="merchantId">商户ID（必需）</param>
        /// <param name="privateKey">商户私钥（必需）</param>
        /// <param name="publicKey">平台公钥（必需）</param>
        /// <param name="gatewayUrl">API 网关地址（必需）</param>
        /// <param name="isSandbox">是否沙箱环境（默认 false）</param>
        /// <param name="extendedSettings">扩展配置（可选）</param>
        /// <exception cref="ArgumentNullException">必需参数为空</exception>
        /// <exception cref="ArgumentException">参数格式无效</exception>
        public ProviderSettings(
            string providerName,
            string appId,
            string merchantId,
            string privateKey,
            string publicKey,
            Uri gatewayUrl,
            bool isSandbox = false,
            IDictionary<string, object> extendedSettings = null)
        {
            // 使用统一验证逻辑
            InternalValidate(providerName, appId, merchantId, privateKey, publicKey, gatewayUrl);

            ProviderName = providerName;
            AppId = appId;
            MerchantId = merchantId;
            PrivateKey = privateKey;
            PublicKey = publicKey;
            GatewayUrl = gatewayUrl;
            IsSandbox = isSandbox;

            // 使用 ReadOnlyDictionary 防御性拷贝
            ExtendedSettings = extendedSettings != null
                ? new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(extendedSettings))
                : new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());
        }

        /// <summary>
        /// 内部验证逻辑（统一入口，避免重复）
        /// </summary>
        private static void InternalValidate(
            string providerName,
            string appId,
            string merchantId,
            string privateKey,
            string publicKey,
            Uri gatewayUrl)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentNullException(nameof(providerName), "ProviderName cannot be null or empty");

            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentNullException(nameof(appId), "AppId cannot be null or empty");

            if (string.IsNullOrWhiteSpace(merchantId))
                throw new ArgumentNullException(nameof(merchantId), "MerchantId cannot be null or empty");

            if (string.IsNullOrWhiteSpace(privateKey))
                throw new ArgumentNullException(nameof(privateKey), "PrivateKey cannot be null or empty for signing");

            if (string.IsNullOrWhiteSpace(publicKey))
                throw new ArgumentNullException(nameof(publicKey), "PublicKey cannot be null or empty for verification");

            if (gatewayUrl == null)
                throw new ArgumentNullException(nameof(gatewayUrl), "GatewayUrl cannot be null");

            if (!gatewayUrl.IsAbsoluteUri)
                throw new ArgumentException("GatewayUrl must be an absolute URI", nameof(gatewayUrl));
        }

        /// <summary>
        /// 获取扩展配置值（泛型版本）
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值（如果不存在）</param>
        /// <returns>配置值</returns>
        public T GetExtendedSetting<T>(string key, T defaultValue = default)
        {
            if (ExtendedSettings.TryGetValue(key, out object? value) && value is T typedValue)
                return typedValue;

            return defaultValue;
        }

        /// <summary>
        /// 检查是否包含指定的扩展配置键
        /// </summary>
        public bool HasExtendedSetting(string key)
        {
            return ExtendedSettings.ContainsKey(key);
        }

        /// <summary>
        /// 创建新的配置实例（添加扩展配置）
        /// 不可变对象模式：返回新实例而非修改现有实例
        /// </summary>
        public ProviderSettings WithExtendedSetting(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            var newExtendedSettings = new Dictionary<string, object>(ExtendedSettings)
            {
                [key] = value
            };

            return new ProviderSettings(
                ProviderName,
                AppId,
                MerchantId,
                PrivateKey,
                PublicKey,
                GatewayUrl,
                IsSandbox,
                newExtendedSettings);
        }

        /// <summary>
        /// 转换为字符串（用于日志）
        /// ⚠️ 注意：不包含私钥，防止泄露
        /// </summary>
        public override string ToString()
        {
            string env = IsSandbox ? "Sandbox" : "Production";
            return $"ProviderSettings[Provider={ProviderName}, AppId={AppId}, MerchantId={MerchantId}, Env={env}]";
        }

        /// <summary>
        /// 验证配置完整性（用于启动期检查或反序列化后验证）
        /// </summary>
        /// <returns>验证是否通过</returns>
        public bool Validate(out string errorMessage)
        {
            try
            {
                // 复用统一验证逻辑
                InternalValidate(ProviderName, AppId, MerchantId, PrivateKey, PublicKey, GatewayUrl);
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
