// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NexusContract.Abstractions.Security;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Hosting.Security
{
    /// <summary>
    /// AES256 安全提供程序：高性能对称加密
    /// 
    /// 算法特征：
    /// - 加密算法：AES256-CBC
    /// - 密钥长度：256 位（32 字节）
    /// - 初始化向量：随机生成（16 字节）
    /// - 填充模式：PKCS7
    /// 
    /// 性能特征：
    /// - 硬件加速：CPU AES-NI 指令集支持
    /// - 加密耗时：~5μs（2KB 密钥）
    /// - 对比网络 IO：1ms Redis 延迟 >> 5μs 加密
    /// 
    /// 安全约束：
    /// - 主密钥来源：环境变量 NEXUS_MASTER_KEY
    /// - IV 随机生成：每次加密使用不同 IV（防止模式攻击）
    /// - 单一标准：所有加密数据存储为 Base64 编码的密文，密钥升级通过运维脚本完成数据迁移（代码不参与版本判断）
    /// 
    /// 使用场景：
    /// - Redis L2 缓存中的 PrivateKey 加密
    /// - 配置文件中的敏感字段加密
    /// </summary>
    public sealed class AesSecurityProvider : ISecurityProvider
    {
        private readonly byte[] _masterKey;

        /// <summary>
        /// 构造 AES 安全提供程序
        /// </summary>
        /// <param name="masterKeyBase64">主密钥（Base64 编码，32 字节）</param>
        /// <exception cref="ArgumentException">主密钥无效</exception>
        public AesSecurityProvider(string masterKeyBase64)
        {
            // Validate base64 and length using NexusGuard (NXC codes)
            NexusGuard.EnsureValidBase64(masterKeyBase64);
            _masterKey = Convert.FromBase64String(masterKeyBase64!);
            NexusGuard.EnsureByteLength(_masterKey, 32);
        }

        /// <summary>
        /// 加密明文
        /// </summary>
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            using Aes aes = Aes.Create();
            aes.Key = _masterKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV(); // 随机 IV（每次加密不同）

            using ICryptoTransform encryptor = aes.CreateEncryptor();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // 格式: [IV(16字节)][密文]（Base64 编码）
            byte[] result = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

            return Convert.ToBase64String(result);
        }

        /// <summary>
        /// 解密密文
        /// </summary>
        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            // 单一标准：直接作为 Base64 编码的 [IV+密文]
            byte[] fullCipher = Convert.FromBase64String(cipherText);

            if (fullCipher.Length < 16)
                throw new CryptographicException("Invalid cipher text: too short");

            using Aes aes = Aes.Create();
            aes.Key = _masterKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // 提取 IV（前 16 字节）
            byte[] iv = new byte[16];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
            aes.IV = iv;

            // 提取密文（剩余部分）
            byte[] cipherBytes = new byte[fullCipher.Length - 16];
            Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
