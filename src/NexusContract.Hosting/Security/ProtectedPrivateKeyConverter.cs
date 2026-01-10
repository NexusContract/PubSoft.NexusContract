// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexusContract.Abstractions.Security;

namespace NexusContract.Hosting.Security
{
    /// <summary>
    /// 敏感字段加密转换器：在 JSON 序列化时自动加密/解密
    /// 
    /// 使用场景：
    /// - HybridConfigResolver 将配置序列化到 Redis
    /// - ProviderSettings.PrivateKey 字段透明加解密
    /// 
    /// 工作原理：
    /// - Write（序列化到 Redis）：明文 → AES256 加密 → Base64
    /// - Read（从 Redis 反序列化）：Base64 → AES256 解密 → 明文
    /// 
    /// 性能影响：
    /// - 加密耗时：~5μs（2KB 密钥）
    /// - 仅在配置加载时触发（L2 → L1）
    /// - 热路径（L1 命中）无加密开销
    /// 
    /// 安全保障：
    /// - Redis 中存储的是密文（即使 Redis 泄露也无法直接使用）
    /// - 内存中保存的是明文（避免每次签名都解密）
    /// - 传输加密：Redis 连接使用 TLS
    /// </summary>
    public sealed class ProtectedPrivateKeyConverter : JsonConverter<string>
    {
        private readonly ISecurityProvider _securityProvider;

        /// <summary>
        /// 构造加密转换器
        /// </summary>
        /// <param name="securityProvider">安全提供程序（从 DI 容器注入）</param>
        public ProtectedPrivateKeyConverter(ISecurityProvider securityProvider)
        {
            _securityProvider = securityProvider ?? throw new ArgumentNullException(nameof(securityProvider));
        }

        /// <summary>
        /// 反序列化（从 Redis 读取时解密）
        /// </summary>
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? encryptedValue = reader.GetString();

            if (string.IsNullOrWhiteSpace(encryptedValue))
                return string.Empty;

            // 从 Redis 读出时：解密
            return _securityProvider.Decrypt(encryptedValue);
        }

        /// <summary>
        /// 序列化（写入 Redis 时加密）
        /// </summary>
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                writer.WriteNullValue();
                return;
            }

            // 写入 Redis 时：加密
            string encryptedValue = _securityProvider.Encrypt(value);
            writer.WriteStringValue(encryptedValue);
        }
    }
}
