// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace NexusContract.Abstractions.Security
{
    /// <summary>
    /// 敏感数据保护器：保护/恢复机密信息
    /// 
    /// 职责：
    /// - 保护敏感数据（存储到 Redis 前：私钥、密钥、配置）
    /// - 恢复敏感数据（从 Redis 读取后）
    /// 
    /// 实现约束：
    /// - 必须使用强加密算法（AES256）
    /// - 主密钥必须从安全源获取（环境变量、Key Vault）
    /// - 存储格式为 Base64 编码的 [IV(16)+Cipher]，版本迁移通过运维脚本执行数据迁移
    /// 
    /// 使用场景：
    /// - HybridConfigResolver 序列化配置到 Redis
    /// - ProtectedPrivateKeyConverter 透明保护/恢复私钥
    /// - 未来可扩展到保护 AppSecret、MerchantPassword 等其他敏感信息
    /// 
    /// 宪法依据：
    /// - 宪法 011（单一标准加密）：采用统一的 AES256-CBC + Base64 方案
    /// - 宪法 012（诊断主权）：所有保护/恢复失败都应抛出结构化诊断异常
    /// </summary>
    public interface ISecretProtector
    {
        /// <summary>
        /// 保护敏感数据（加密）
        /// </summary>
        /// <param name="plainText">明文字符串</param>
        /// <returns>保护后的字符串（Base64 编码的 IV+Cipher，无任何前缀）</returns>
        string Protect(string plainText);

        /// <summary>
        /// 恢复敏感数据（解密）
        /// </summary>
        /// <param name="protectedText">保护后的字符串</param>
        /// <returns>明文字符串</returns>
        string Unprotect(string protectedText);
    }
}
