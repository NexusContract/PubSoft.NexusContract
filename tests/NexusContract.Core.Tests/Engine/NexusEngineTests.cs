// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Moq;
using NexusContract.Abstractions.Configuration;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Abstractions.Providers;
using NexusContract.Core.Engine;
using Xunit;

namespace NexusContract.Core.Tests.Engine;

// Helper test types - Must be outside class for Moq proxy generation
public class TestResponse
{
    public string? Message { get; set; }
}

/// <summary>
/// NexusEngine 单元测试
/// 
/// 测试范围：
/// - Provider 注册/注销
/// - JIT 配置加载
/// - ImplementationName 路由策略
/// - 异常处理与包装
/// </summary>
public class NexusEngineTests
{
    [Fact]
    public void Constructor_NullConfigResolver_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ContractIncompleteException>(() => new NexusEngine(null!));
    }

    [Fact]
    public void RegisterProvider_ValidProvider_ShouldSucceed()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);
        var mockProvider = new Mock<IProvider>();

        // Act
        engine.RegisterProvider("TestProvider", mockProvider.Object);

        // Assert
        Assert.Equal(1, engine.RegisteredProviderCount);
        Assert.True(engine.IsProviderRegistered("TestProvider"));
    }

    [Fact]
    public void RegisterProvider_NullProviderName_ShouldThrow()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);
        var mockProvider = new Mock<IProvider>();

        // Act & Assert
        Assert.Throws<ContractIncompleteException>(() => 
            engine.RegisterProvider(null!, mockProvider.Object));
    }

    [Fact]
    public void RegisterProvider_NullProvider_ShouldThrow()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);

        // Act & Assert
        Assert.Throws<ContractIncompleteException>(() => 
            engine.RegisterProvider("TestProvider", null!));
    }

    [Fact]
    public void UnregisterProvider_ExistingProvider_ShouldReturnTrue()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);
        var mockProvider = new Mock<IProvider>();
        engine.RegisterProvider("TestProvider", mockProvider.Object);

        // Act
        bool result = engine.UnregisterProvider("TestProvider");

        // Assert
        Assert.True(result);
        Assert.Equal(0, engine.RegisteredProviderCount);
        Assert.False(engine.IsProviderRegistered("TestProvider"));
    }

    [Fact]
    public void UnregisterProvider_NonExistentProvider_ShouldReturnFalse()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);

        // Act
        bool result = engine.UnregisterProvider("NonExistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsProviderRegistered_CaseInsensitive_ShouldReturnTrue()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);
        var mockProvider = new Mock<IProvider>();
        engine.RegisterProvider("Alipay", mockProvider.Object);

        // Act & Assert
        Assert.True(engine.IsProviderRegistered("alipay"));
        Assert.True(engine.IsProviderRegistered("ALIPAY"));
        Assert.True(engine.IsProviderRegistered("Alipay"));
    }

    [Fact]
    public async Task ExecuteAsync_NullRequest_ShouldThrow()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ContractIncompleteException>(() => 
            engine.ExecuteAsync<TestResponse>(null!, "Alipay", "app123"));
    }

    [Fact]
    public async Task ExecuteAsync_NullProviderName_ShouldThrow()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);
        var mockRequest = new Mock<IApiRequest<TestResponse>>();

        // Act & Assert
        await Assert.ThrowsAsync<ContractIncompleteException>(() => 
            engine.ExecuteAsync(mockRequest.Object, null!, "app123"));
    }

    [Fact]
    public async Task ExecuteAsync_NullProfileId_ShouldThrow()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var engine = new NexusEngine(mockResolver.Object);
        var mockRequest = new Mock<IApiRequest<TestResponse>>();

        // Act & Assert
        await Assert.ThrowsAsync<ContractIncompleteException>(() => 
            engine.ExecuteAsync(mockRequest.Object, "Alipay", null!));
    }

    [Fact]
    public async Task ExecuteAsync_ProviderNotRegistered_ShouldThrow()
    {
        // Arrange
        var mockResolver = new Mock<IConfigurationResolver>();
        var mockConfig = new Mock<IProviderConfiguration>();
        mockConfig.Setup(c => c.ProviderName).Returns("Alipay");
        
        mockResolver.Setup(r => r.ResolveAsync("Alipay", "app123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConfig.Object);

        var engine = new NexusEngine(mockResolver.Object);
        
        var mockRequest = new Mock<IApiRequest<TestResponse>>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ContractIncompleteException>(() => 
            engine.ExecuteAsync(mockRequest.Object, "Alipay", "app123"));
        Assert.Contains("not registered", ex.Message);
    }
}
