// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using NexusContract.Hosting.Security;
using NexusContract.Abstractions.Exceptions;
using Xunit;

namespace NexusContract.Hosting.Tests.Security;

/// <summary>
/// AesSecurityProvider 单元测试
/// 
/// 测试范围：
/// - 加解密往返（roundtrip）
/// - 无效密钥格式
/// - 密钥长度校验
/// - 空输入处理
/// - 格式验证（密文为 Base64 编码的 IV+密文）
/// - 加密确定性（每次加密应不同，因 IV 随机）
/// </summary>
public class AesSecurityProviderTests
{
    [Fact]
    public void Constructor_ValidKey_ShouldSucceed()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // Act
        var provider = new AesSecurityProvider(masterKey);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_NullKey_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ContractIncompleteException>(() => new AesSecurityProvider(null!));
    }

    [Fact]
    public void Constructor_EmptyKey_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ContractIncompleteException>(() => new AesSecurityProvider(string.Empty));
    }

    [Fact]
    public void Constructor_InvalidBase64_ShouldThrow()
    {
        // Arrange
        string invalidKey = "not-a-valid-base64!!!";

        // Act & Assert
        Assert.Throws<ContractIncompleteException>(() => new AesSecurityProvider(invalidKey));
    }

    [Fact]
    public void Constructor_WrongKeyLength_ShouldThrow()
    {
        // Arrange - 16 bytes (128 bit) instead of 32 bytes (256 bit)
        string shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        // Act & Assert
        Assert.Throws<ContractIncompleteException>(() => new AesSecurityProvider(shortKey));
    }

    [Fact]
    public void Encrypt_ValidInput_ShouldReturnBase64Cipher()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);
        string plainText = "test-private-key-12345";

        // Act
        string encrypted = provider.Encrypt(plainText);

        // Assert
        Assert.DoesNotContain(':', encrypted);
        Assert.NotEqual(plainText, encrypted);
    }

    [Fact]
    public void Encrypt_EmptyString_ShouldReturnEmptyString()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);

        // Act
        string encrypted = provider.Encrypt(string.Empty);

        // Assert
        Assert.Equal(string.Empty, encrypted);
    }

    [Fact]
    public void Decrypt_ValidCipherText_ShouldReturnOriginalPlainText()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);
        string originalPlainText = "my-secret-private-key-2024";

        // Act
        string encrypted = provider.Encrypt(originalPlainText);
        string decrypted = provider.Decrypt(encrypted);

        // Assert
        Assert.Equal(originalPlainText, decrypted);
    }

    [Fact]
    public void Decrypt_EmptyString_ShouldReturnEmptyString()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);

        // Act
        string decrypted = provider.Decrypt(string.Empty);

        // Assert
        Assert.Equal(string.Empty, decrypted);
    }

    [Fact]
    public void Decrypt_InvalidBase64_ShouldThrow()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);
        string invalidCipher = "not-base64!!!";

        // Act & Assert
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
            provider.Decrypt(invalidCipher));
    }

    [Fact]
    public void Decrypt_TooShortCipherText_ShouldThrow()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);
        string tooShort = Convert.ToBase64String(RandomNumberGenerator.GetBytes(8)); // Less than 16 bytes

        // Act & Assert
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
            provider.Decrypt(tooShort));
    }

    [Fact]
    public void Encrypt_SameInputTwice_ShouldProduceDifferentCipherTexts()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);
        string plainText = "determinism-test-input";

        // Act
        string encrypted1 = provider.Encrypt(plainText);
        string encrypted2 = provider.Encrypt(plainText);

        // Assert - Different IV means different ciphertexts
        Assert.NotEqual(encrypted1, encrypted2);

        // But both should decrypt to the same plaintext
        Assert.Equal(plainText, provider.Decrypt(encrypted1));
        Assert.Equal(plainText, provider.Decrypt(encrypted2));
    }

    [Fact]
    public void Encrypt_Decrypt_LongInput_ShouldSucceed()
    {
        // Arrange
        string masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);
        string longPlainText = new string('A', 2048); // 2KB private key

        // Act
        string encrypted = provider.Encrypt(longPlainText);
        string decrypted = provider.Decrypt(encrypted);

        // Assert
        Assert.Equal(longPlainText, decrypted);
    }

    [Fact]
    public void Decrypt_WrongKey_ShouldThrow()
    {
        // Arrange
        string masterKey1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string masterKey2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider1 = new AesSecurityProvider(masterKey1);
        var provider2 = new AesSecurityProvider(masterKey2);
        string plainText = "secret-data";

        // Act
        string encrypted = provider1.Encrypt(plainText);

        // Assert - Decrypting with wrong key should fail
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
            provider2.Decrypt(encrypted));
    }
}
