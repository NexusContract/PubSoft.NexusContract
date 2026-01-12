// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace NexusContract.Abstractions.Security
{
    /// <summary>
    /// 安全提供程序：加密/解密敏感数据
    /// 
    /// 职责：
    /// - 加密商户私钥（存储到 Redis 时）
    /// - 解密商户私钥（从 Redis 读取时）
    /// 
    /// 实现约束：
    /// - 必须使用强加密算法（AES256）
    /// - 主密钥必须从安全源获取（环境变量、Key Vault）
    /// - 存储格式为 Base64 编码的 [IV(16)+Cipher]，版本迁移通过运维脚本执行数据迁移
    /// 
    /// 使用场景：
    /// - HybridConfigResolver 序列化配置到 Redis
    /// - ProtectedPrivateKeyConverter 透明加解密
    /// </summary>
    public interface ISecurityProvider
    {
        /// <summary>
        /// 加密明文
        /// </summary>
        /// <param name="plainText">明文字符串</param>
        /// <returns>加密后的字符串（Base64 编码的 IV+Cipher，无任何前缀）</returns>
        string Encrypt(string plainText);
        /// <summary>
        /// 解密密文
        /// </summary>
        /// <param name="cipherText">加密字符串</param>
        /// <returns>明文字符串</returns>
        string Decrypt(string cipherText);
    }
}
