# NexusContract 实现路线图（除数据库接入外）

**分析时间：** 2026-01-10  
**当前版本：** 1.0.0-preview  
**目标版本：** 1.0.0-GA（内部落地稳定后）  
**分析范围：** IProvider 适配器、单元测试、Demo 项目完善

---

## 📊 架构现状评估

### ✅ 已完成核心组件

| 组件 | 状态 | 完成度 | 备注 |
|------|------|--------|------|
| **Abstractions Layer** | ✅ 完成 | 100% | 纯接口层，依赖倒置原则 |
| **Core Layer** | ✅ 完成 | 95% | NexusEngine, NexusGateway, 投影/回填引擎 |
| **Hosting Layer** | ✅ 完成 | 90% | TenantContextFactory, HybridConfigResolver, AesSecurityProvider |
| **Hosting.Yarp** | ✅ 完成 | 100% | INexusTransport, YarpTransport (HTTP/2 + Polly) |
| **Providers.Alipay** | ✅ 完成 | 85% | AlipayProvider, AlipayProxyProvider (已集成 INexusTransport) |

### ❌ 核心架构缺口

#### 1. **IProvider 适配器缺失** ⚠️ 高优先级

**问题描述：**

当前 `AlipayProvider` 不实现 `IProvider` 接口，导致：
- ❌ **无法与 NexusEngine 集成** - Engine 需要 `IProvider.ExecuteAsync(request, configuration, ct)` 签名
- ❌ **配置注入不一致** - AlipayProvider 使用 `AlipayProviderConfig`，Engine 需要 `IProviderConfiguration`
- ❌ **无法实现 ISV 多租户** - Engine 无法动态加载配置并调用 Provider

**当前签名对比：**

```csharp
// IProvider 接口要求（NexusEngine 调用）
Task<TResponse> ExecuteAsync<TResponse>(
    IApiRequest<TResponse> request,
    IProviderConfiguration configuration,  // ← 通用配置接口
    CancellationToken ct = default);

// AlipayProvider 当前签名（无法被 Engine 调用）
public async Task<TResponse> ExecuteAsync<TResponse>(
    IApiRequest<TResponse> request,
    CancellationToken cancellationToken = default);  // ← 缺少配置参数
```

**根本原因：**

1. `AlipayProvider` 在构造时接收 `AlipayProviderConfig`（静态配置）
2. `IProvider` 要求配置通过 `ExecuteAsync` 方法参数传入（动态配置）
3. 两种设计理念冲突：
   - **静态配置模式**：Provider 实例 = 单个租户（传统方式）
   - **动态配置模式**：Provider 实例 = 无状态单例，服务所有租户（ISV 模式）

---

#### 2. **单元测试缺失** ⚠️ 中优先级

**问题描述：**

当前项目 **完全没有测试项目**，导致：
- ❌ 无法验证核心组件（HybridConfigResolver, AesSecurityProvider, YarpTransport）
- ❌ 无法回归测试重构影响
- ❌ 无法 TDD 驱动开发
- ❌ CI/CD 缺少质量门控

**测试框架选择：**

推荐 **xUnit + Moq + FluentAssertions**，理由：
- xUnit：.NET 社区标准，支持并行测试
- Moq：轻量级 Mock 框架
- FluentAssertions：可读性强的断言语法

**需要测试的核心组件：**

| 组件 | 测试类型 | 优先级 | 复杂度 |
|------|----------|--------|--------|
| `HybridConfigResolver` | 集成测试 | ⭐⭐⭐ | 高（Redis + MemoryCache） |
| `AesSecurityProvider` | 单元测试 | ⭐⭐⭐ | 中（加密算法验证） |
| `YarpTransport` | 集成测试 | ⭐⭐ | 高（Polly 重试/熔断） |
| `TenantContextFactory` | 单元测试 | ⭐⭐ | 低（HTTP 上下文提取） |
| `NexusEngine` | 单元测试 | ⭐⭐⭐ | 中（路由逻辑） |
| `ProviderSettings` | 单元测试 | ⭐ | 低（配置验证） |

---

#### 3. **Demo 项目不完整** ⚠️ 中优先级

**问题描述：**

当前 `Demo.Alipay.HttpApi` 缺少关键组件集成：
- ❌ **缺少 NexusEngine 集成** - 没有演示 ISV 多租户路由
- ❌ **缺少 HybridConfigResolver** - 没有演示 Redis 配置缓存
- ❌ **缺少 INexusTransport 集成** - 没有演示 YARP 传输层
- ❌ **缺少多租户示例** - 只演示单个商户，没有动态配置加载

**当前 Demo 架构：**

```
FastEndpoints → AlipayProvider → 支付宝 API
    ↑                ↑
    |                └─ 静态配置（构造时注入）
    └─ 直接调用 Provider
```

**期望的完整架构：**

```
FastEndpoints → TenantContextFactory → NexusEngine → IProvider → INexusTransport → 支付宝 API
                        ↓                    ↓             ↓              ↓
                  提取租户身份        JIT 配置加载   动态路由     HTTP/2 + Polly
                        ↓                    ↓
                 HybridConfigResolver   MemoryCache/Redis
```

---

## 🎯 解决方案详解

### 方案 1：IProvider 适配器实现

#### 架构选择：适配器模式 vs 重构 AlipayProvider

**选项 A：创建适配器（推荐）⭐**

```csharp
// Providers.Alipay/AlipayProviderAdapter.cs
public class AlipayProviderAdapter : IProvider
{
    private readonly INexusTransport _transport;
    private readonly NexusGateway _gateway;
    private readonly INamingPolicy _namingPolicy;

    public string ProviderName => "Alipay";

    public AlipayProviderAdapter(
        INexusTransport transport,
        NexusGateway gateway,
        INamingPolicy? namingPolicy = null)
    {
        _transport = transport;
        _gateway = gateway;
        _namingPolicy = namingPolicy ?? new SnakeCaseNamingPolicy();
    }

    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration configuration,  // ← 从 Engine 传入
        CancellationToken ct = default)
        where TResponse : class, new()
    {
        // 1. 转换配置：IProviderConfiguration → AlipayProviderConfig
        var alipayConfig = new AlipayProviderConfig
        {
            AppId = configuration.AppId,
            MerchantId = configuration.MerchantId,
            PrivateKey = configuration.PrivateKey,
            AlipayPublicKey = configuration.PublicKey,
            ApiGateway = new Uri(configuration.GatewayUrl),
            UseSandbox = configuration.GetExtendedSetting<bool>("UseSandbox"),
            RequestTimeout = TimeSpan.FromSeconds(30)
        };

        // 2. 创建临时 AlipayProvider 实例（或复用单例）
        var provider = new AlipayProvider(alipayConfig, _gateway, _transport, _namingPolicy);

        // 3. 委托执行
        return await provider.ExecuteAsync(request, ct);
    }
}
```

**优点：**
- ✅ 不破坏现有 `AlipayProvider` 实现
- ✅ 向后兼容（现有代码继续工作）
- ✅ 清晰的职责分离（适配器 = Engine 桥接层）
- ✅ 快速实现（~100 行代码）

**缺点：**
- ⚠️ 每次请求创建临时 `AlipayProvider` 实例（性能损耗）
- ⚠️ 配置转换开销（`IProviderConfiguration` → `AlipayProviderConfig`）

**优化方案（推荐）：**
```csharp
// ⚠️ 注意：缓存轻量级配置对象，而非 Provider 实例
// 因为 AlipayProvider 依赖 INexusTransport（单例），应该是无状态执行引擎
private readonly ConcurrentDictionary<string, AlipayProviderConfig> _configCache = new();

public async Task<TResponse> ExecuteAsync<TResponse>(...)
{
    // 配置哈希作为缓存键
    string cacheKey = $"{configuration.AppId}:{configuration.MerchantId}";
    
    var alipayConfig = _configCache.GetOrAdd(cacheKey, _ => ConvertConfig(configuration));
    
    // AlipayProvider 本身应该是单例，每次传入不同配置
    var provider = new AlipayProvider(alipayConfig, _gateway, _transport, _namingPolicy);
    
    return await provider.ExecuteAsync(request, ct);
}
```

---

**选项 B：重构 AlipayProvider（不推荐）❌**

```csharp
// 将 AlipayProvider 改为无状态单例
public class AlipayProvider : IProvider
{
    private readonly INexusTransport _transport;
    private readonly NexusGateway _gateway;

    // ❌ 移除静态配置字段
    // private readonly AlipayProviderConfig _config;

    public string ProviderName => "Alipay";

    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration configuration,  // ← 每次传入
        CancellationToken ct = default)
    {
        // 每次请求从 configuration 读取参数
        string appId = configuration.AppId;
        string privateKey = configuration.PrivateKey;
        // ...
    }
}
```

**缺点：**
- ❌ 破坏现有 API（不向后兼容）
- ❌ 强制所有调用者修改代码
- ❌ 配置验证分散到每次请求中

---

### 方案 2：单元测试项目结构

#### 项目结构设计

```
tests/
├── NexusContract.Core.Tests/
│   ├── Configuration/
│   │   ├── ProviderSettingsTests.cs
│   │   ├── ConfigurationContextTests.cs
│   │   └── InMemoryConfigResolverTests.cs
│   ├── Engine/
│   │   └── NexusEngineTests.cs
│   └── NexusContract.Core.Tests.csproj
│
├── NexusContract.Hosting.Tests/
│   ├── Configuration/
│   │   └── HybridConfigResolverTests.cs
│   ├── Security/
│   │   ├── AesSecurityProviderTests.cs
│   │   └── ProtectedPrivateKeyConverterTests.cs
│   ├── Factories/
│   │   └── TenantContextFactoryTests.cs
│   └── NexusContract.Hosting.Tests.csproj
│
└── NexusContract.Hosting.Yarp.Tests/
    ├── YarpTransportTests.cs
    ├── YarpTransportOptionsTests.cs
    └── NexusContract.Hosting.Yarp.Tests.csproj
```

#### 测试用例设计示例

**1. HybridConfigResolver 测试**

```csharp
public class HybridConfigResolverTests
{
    [Fact]
    public async Task ResolveAsync_L1Cache_Hit_Should_Return_From_MemoryCache()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var redis = new Mock<IConnectionMultiplexer>();
        var securityProvider = new Mock<ISecurityProvider>();
        var resolver = new HybridConfigResolver(memoryCache, redis.Object, securityProvider.Object);
        
        var identity = new ConfigurationContext("realm1", "profile1", "Alipay");
        var expectedConfig = new ProviderSettings { AppId = "2021..." };
        
        // 预填充 L1 缓存
        memoryCache.Set($"config:{identity.RealmId}:{identity.ProfileId}", expectedConfig);
        
        // Act
        var result = await resolver.ResolveAsync(identity);
        
        // Assert
        result.Should().BeEquivalentTo(expectedConfig);
        redis.Verify(r => r.GetDatabase(It.IsAny<int>()), Times.Never); // 未访问 Redis
    }
    
    [Fact]
    public async Task ResolveAsync_L1Miss_L2Hit_Should_Backfill_L1()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var redis = new Mock<IConnectionMultiplexer>();
        var db = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>())).Returns(db.Object);
        
        var configJson = JsonSerializer.Serialize(new ProviderSettings { AppId = "2021..." });
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(configJson);
        
        var resolver = new HybridConfigResolver(memoryCache, redis.Object, null);
        var identity = new ConfigurationContext("realm1", "profile1", "Alipay");
        
        // Act
        var result = await resolver.ResolveAsync(identity);
        
        // Assert
        result.AppId.Should().Be("2021...");
        memoryCache.TryGetValue($"config:{identity.RealmId}:{identity.ProfileId}", out _)
            .Should().BeTrue("L1 should be backfilled");
    }
}
```

**2. AesSecurityProvider 测试**

```csharp
public class AesSecurityProviderTests
{
    [Fact]
    public void Encrypt_Decrypt_Should_Return_Original_PlainText()
    {
        // Arrange
        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new AesSecurityProvider(masterKey);
        var plainText = "MIIEvQIBA..."; // 私钥
        
        // Act
        var encrypted = provider.Encrypt(plainText);
        var decrypted = provider.Decrypt(encrypted);
        
        // Assert
        decrypted.Should().Be(plainText);
        encrypted.Should().StartWith("v1:"); // 版本前缀
        encrypted.Should().NotBe(plainText); // 加密后不同
    }
    
    [Fact]
    public void Encrypt_Same_PlainText_Should_Generate_Different_Ciphertext()
    {
        // Arrange
        var provider = new AesSecurityProvider(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var plainText = "test-key";
        
        // Act
        var encrypted1 = provider.Encrypt(plainText);
        var encrypted2 = provider.Encrypt(plainText);
        
        // Assert
        encrypted1.Should().NotBe(encrypted2, "IV should be random");
        provider.Decrypt(encrypted1).Should().Be(plainText);
        provider.Decrypt(encrypted2).Should().Be(plainText);
    }
}
```

**3. YarpTransport 集成测试**

```csharp
public class YarpTransportTests
{
    [Fact]
    public async Task SendAsync_Should_Retry_On_Timeout()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        int callCount = 0;
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 3)
                    throw new TaskCanceledException("Timeout");
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        
        var httpClient = new HttpClient(mockHandler.Object);
        var options = Options.Create(new YarpTransportOptions { RetryCount = 3 });
        var transport = new YarpTransport(httpClient, options, Mock.Of<ILogger<YarpTransport>>());
        
        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com");
        var response = await transport.SendAsync(request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(3, "should retry 2 times + 1 final success");
    }
    
    [Fact]
    public async Task SendAsync_Should_Open_CircuitBreaker_After_Threshold()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Service unavailable"));
        
        var httpClient = new HttpClient(mockHandler.Object);
        var options = Options.Create(new YarpTransportOptions 
        { 
            CircuitBreakerFailureThreshold = 3,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(10)
        });
        var transport = new YarpTransport(httpClient, options, Mock.Of<ILogger<YarpTransport>>());
        
        // Act & Assert
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com");
        
        // 前 3 次失败
        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync(request));
        }
        
        // 第 4 次应该触发熔断器
        await Assert.ThrowsAsync<BrokenCircuitException>(() => transport.SendAsync(request));
    }
}
```

---

### 方案 3：Demo 项目完善

#### 需要添加的组件集成

**1. 注册 NexusEngine 和 IProvider**

```csharp
// Program.cs
// ==================== 步骤 1：注册核心组件 ====================
builder.Services.AddSingleton<IConfigurationResolver>(sp => 
{
    var memoryCache = sp.GetRequiredService<IMemoryCache>();
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    var securityProvider = sp.GetRequiredService<ISecurityProvider>();
    
    return new HybridConfigResolver(memoryCache, redis, securityProvider);
});

builder.Services.AddSingleton<INexusEngine>(sp =>
{
    var configResolver = sp.GetRequiredService<IConfigurationResolver>();
    var engine = new NexusEngine(configResolver);
    
    // 注册 Provider
    var transport = sp.GetRequiredService<INexusTransport>();
    var gateway = sp.GetRequiredService<NexusGateway>();
    var alipayProvider = new AlipayProviderAdapter(transport, gateway);
    
    engine.RegisterProvider("Alipay", alipayProvider);
    
    return engine;
});

// ==================== 步骤 2：注册传输层和安全组件 ====================
builder.Services.AddNexusYarpTransport(options =>
{
    options.RetryCount = 3;
    options.CircuitBreakerFailureThreshold = 5;
});

builder.Services.AddSingleton<ISecurityProvider>(sp =>
{
    var masterKey = builder.Configuration["Security:MasterKey"] 
        ?? throw new InvalidOperationException("Security:MasterKey is required");
    return new AesSecurityProvider(masterKey);
});

// ==================== 步骤 3：注册 Redis ====================
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration["Redis:ConnectionString"] 
        ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});
```

**2. 更新 Endpoint 使用 NexusEngine**

```csharp
// Endpoints/TradePayEndpoint.cs
public class TradePayEndpoint : NexusEndpoint<TradePayRequest, TradePayResponse>
{
    private readonly INexusEngine _engine;

    public TradePayEndpoint(INexusEngine engine)
    {
        _engine = engine;
    }

    public override void Configure()
    {
        Post("/trade/pay");
        AllowAnonymous();
    }

    public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
    {
        // 1. 从 HTTP 上下文提取租户身份
        var tenantContext = TenantContextFactory.FromHttpContext(HttpContext);
        
        // 2. 通过 Engine 执行（JIT 配置加载 + 动态路由）
        var response = await _engine.ExecuteAsync(req, tenantContext, ct);
        
        // 3. 返回响应
        await SendAsync(response, cancellation: ct);
    }
}
```

**3. 添加多租户配置示例**

```json
// appsettings.json
{
  "Redis": {
    "ConnectionString": "localhost:6379,ssl=false"
  },
  "Security": {
    "MasterKey": "your-base64-encoded-32-byte-key"
  },
  "TenantConfigs": [
    {
      "RealmId": "merchant-001",
      "ProfileId": "prod",
      "ProviderName": "Alipay",
      "AppId": "2021001234567890",
      "MerchantId": "2088123456789012",
      "PrivateKey": "MIIEvQIBA...",
      "PublicKey": "MIIBIjANBgkqh...",
      "GatewayUrl": "https://openapi.alipay.com/",
      "ExtendedSettings": {
        "ImplementationName": "Alipay.RSA",
        "UseSandbox": false
      }
    },
    {
      "RealmId": "merchant-002",
      "ProfileId": "prod",
      "ProviderName": "Alipay",
      "AppId": "2021009876543210",
      "MerchantId": "2088210987654321",
      "PrivateKey": "MIIEvQIBA...",
      "PublicKey": "MIIBIjANBgkqh...",
      "GatewayUrl": "https://openapi.alipay.com/",
      "ExtendedSettings": {
        "ImplementationName": "Alipay.Cert",  // ← 使用证书版本
        "UseSandbox": false
      }
    }
  ]
}
```

---

## 📝 实施优先级和时间估算

### Phase 1: IProvider 适配器（1-2 天）⭐⭐⭐

**任务清单：**
- [ ] 创建 `AlipayProviderAdapter` 类
- [ ] 实现配置转换逻辑（`IProviderConfiguration` → `AlipayProviderConfig`）
- [ ] 添加 Provider 缓存机制（性能优化）
- [ ] 更新 `NexusEngine` 注册示例
- [ ] 编写集成测试

**预期产出：**
- ✅ AlipayProvider 可被 NexusEngine 调用
- ✅ 支持 ISV 多租户动态配置
- ✅ 向后兼容现有代码

---

### Phase 2: 单元测试项目（3-5 天）⭐⭐

**任务清单：**
- [ ] 创建测试项目结构（3 个测试项目）
- [ ] 安装测试依赖（xUnit, Moq, FluentAssertions）
- [ ] 编写 `HybridConfigResolver` 测试（10+ 用例）
- [ ] 编写 `AesSecurityProvider` 测试（8+ 用例）
- [ ] 编写 `YarpTransport` 测试（12+ 用例）
- [ ] 编写 `TenantContextFactory` 测试（6+ 用例）
- [ ] 编写 `NexusEngine` 测试（10+ 用例）
- [ ] 配置 CI/CD 测试流水线

**预期产出：**
- ✅ 测试覆盖率 > 80%
- ✅ CI/CD 自动化测试
- ✅ 回归测试保障

---

### Phase 3: Demo 项目完善（2-3 天）⭐⭐

**任务清单：**
- [ ] 集成 `NexusEngine` + `HybridConfigResolver`
- [ ] 集成 `INexusTransport` + `YarpTransport`
- [ ] 添加 Redis 配置和连接
- [ ] 添加多租户配置示例
- [ ] 更新 Endpoint 使用 Engine
- [ ] 添加预热机制（`WarmupAsync`）
- [ ] 编写 README 文档
- [ ] 添加 Docker Compose（Redis + API）

**预期产出：**
- ✅ 完整的 ISV 多租户 Demo
- ✅ 演示 JIT 配置加载
- ✅ 演示 YARP 传输层
- ✅ 可一键启动的 Docker 环境

---

## 🎯 关键决策点

### 决策 1：IProvider 适配器 vs 重构 AlipayProvider

**推荐：** 创建适配器  
**理由：**
1. 不破坏现有 API（向后兼容）
2. 快速实现（~100 行代码）
3. 清晰的职责分离
4. 可复用模式（未来其他 Provider 也可复用）

**风险：**
- 每次请求有配置转换开销（可通过缓存缓解）

---

### 决策 2：测试框架选择

**推荐：** xUnit + Moq + FluentAssertions  
**理由：**
1. xUnit：.NET 社区标准，微软官方推荐
2. Moq：轻量级，易学习
3. FluentAssertions：可读性强，链式 API

**替代方案：**
- NUnit + NSubstitute（更传统，但生态较老）
- MSTest（VS 内置，但功能较弱）

---

### 决策 3：Demo 项目复杂度

**推荐：** 完整集成（Engine + HybridConfigResolver + YARP）  
**理由：**
1. 演示完整架构价值
2. 验证组件集成可行性
3. 提供最佳实践参考

**风险：**
- 依赖 Redis（需 Docker 环境）
- 配置复杂度高（需详细文档）

---

## � 渐进式版本演进策略

### 当前阶段：1.0.0-preview（功能完善期）

**核心原则：** 在至少 **1 个完整的内部落地项目稳定运行** 后才移除 `preview` 标签。

#### 版本号语义

```
1.0.0-preview.N
│ │ │    │     └─ 预览版本递增（每次重要功能提交）
│ │ │    └─────── 预览标识（GA 前保持）
│ │ └──────────── Patch（Bug 修复）
│ └────────────── Minor（功能增强，向后兼容）
└──────────────── Major（破坏性变更）
```

#### 版本演进路线图

**Phase 1：功能补全（当前阶段）**
```
1.0.0-preview          (基础架构)
  ↓
1.0.0-preview.1        (IProvider 适配器)
  ↓
1.0.0-preview.2        (单元测试 + Demo 完善)
  ↓
1.0.0-preview.3        (内部落地集成验证)
```

**Phase 2：内部验证（GA 前门槛）**
- ✅ 至少 1 个生产级项目接入
- ✅ 稳定运行 >= 1 个月
- ✅ 核心组件测试覆盖率 >= 80%
- ✅ 性能基准测试通过
- ✅ 安全审计通过（加密/签名/证书）

**Phase 3：正式发布**
```
1.0.0-rc.1             (Release Candidate，冻结功能)
  ↓
1.0.0-rc.2             (仅 Bug 修复)
  ↓
1.0.0                  (GA，生产就绪)
```

#### 发布策略

| 版本类型 | 发布时机 | 发布渠道 | 稳定性保证 |
|---------|---------|---------|----------|
| `preview.N` | 每完成一个 Phase | BaGet (私有) | 功能验证，API 可能变动 |
| `rc.N` | 内部验证通过 | BaGet (私有) | API 冻结，仅修 Bug |
| `GA` | 稳定运行 1 个月+ | NuGet.org (公开) | 生产就绪，长期支持 |

#### 内部落地验证清单

**必须验证的场景：**
- [ ] ISV 多租户动态配置加载（>= 10 个租户）
- [ ] Redis 缓存穿透/雪崩/击穿防护
- [ ] YARP 传输层重试/熔断在真实网络故障下的表现
- [ ] AES-GCM 加密密钥轮换（密钥版本升级）
- [ ] 高并发场景下的内存/CPU 开销（>= 1000 QPS）
- [ ] OpenTelemetry 链路追踪完整性
- [ ] FastEndpoints 集成的性能对比（vs 原生 HttpClient）

**风险降级策略：**
- 如发现严重问题，回退到 `1.0.0-preview.N-hotfix` 分支
- GA 后前 3 个月内保持每周一次 Patch 版本（`1.0.1`, `1.0.2`...）

---

## �🚀 快速启动建议

### 本周目标（Week 1）

**优先实现 IProvider 适配器**，原因：
1. 解除架构阻塞（Engine 无法使用）
2. 快速验证设计可行性
3. 为后续测试提供基础

### 下周目标（Week 2）

**补充核心组件测试**，重点：
1. `HybridConfigResolver`（最复杂）
2. `YarpTransport`（集成测试）
3. `AesSecurityProvider`（安全关键）

### 第三周目标（Week 3）

**完善 Demo 项目**，目标：
1. 完整演示 ISV 多租户
2. 提供 Docker 一键启动
3. 编写详细使用文档

---

## 🎓 架构优化要点（进阶设计决策）

### 1. 缓存策略修正：配置对象 vs Provider 实例

**❌ 原方案（缓存 Provider 实例）：**
```csharp
private readonly ConcurrentDictionary<string, AlipayProvider> _providerCache = new();

// 问题：AlipayProvider 依赖 INexusTransport（单例），缓存整个实例违反无状态设计
```

**✅ 优化方案（缓存配置对象）：**
```csharp
private readonly ConcurrentDictionary<string, AlipayProviderConfig> _configCache = new();

public async Task<TResponse> ExecuteAsync<TResponse>(
    IApiRequest<TResponse> request,
    IProviderConfiguration configuration,
    CancellationToken ct = default)
{
    string cacheKey = $"{configuration.AppId}:{configuration.MerchantId}";
    
    // 缓存轻量级配置对象（~1KB），而非整个 Provider 实例
    var alipayConfig = _configCache.GetOrAdd(cacheKey, _ => new AlipayProviderConfig
    {
        AppId = configuration.AppId,
        MerchantId = configuration.MerchantId,
        PrivateKey = configuration.PrivateKey,
        AlipayPublicKey = configuration.PublicKey,
        ApiGateway = new Uri(configuration.GatewayUrl)
    });
    
    // AlipayProvider 本身应是无状态执行引擎
    var provider = new AlipayProvider(alipayConfig, _gateway, _transport, _namingPolicy);
    return await provider.ExecuteAsync(request, ct);
}
```

**设计原则：**
- **冷热隔离**：`AlipayProviderAdapter`（冷配置转换） + `AlipayProvider`（热签名计算）
- **轻量缓存**：配置对象 ~1KB，Provider 实例可能包含 HttpClient 等重量级资源
- **无状态执行**：Provider 依赖 INexusTransport（单例），不应持有租户状态

---

### 2. TTL 失效回源测试（防止僵尸配置）

**测试场景：**
L1 缓存过期后，验证能否正确从 Redis 回源加载最新配置。

```csharp
[Fact]
public async Task ResolveAsync_L1TTL_Expired_Should_Reload_From_Redis()
{
    var memoryCache = new MemoryCache(new MemoryCacheOptions());
    var identity = new ConfigurationContext("realm1", "profile1", "Alipay");
    
    // 设置极短 TTL (1 秒)
    memoryCache.Set($"config:{identity.RealmId}:{identity.ProfileId}", 
        new ProviderSettings { AppId = "old-value" },
        TimeSpan.FromSeconds(1));
    
    await Task.Delay(1500); // 等待 TTL 过期
    
    // 模拟 Redis 返回新配置
    var redis = new Mock<IConnectionMultiplexer>();
    var db = new Mock<IDatabase>();
    db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
      .ReturnsAsync(JsonSerializer.Serialize(new ProviderSettings { AppId = "new-value" }));
    redis.Setup(r => r.GetDatabase(It.IsAny<int>())).Returns(db.Object);
    
    var resolver = new HybridConfigResolver(memoryCache, redis.Object, null);
    var result = await resolver.ResolveAsync(identity);
    
    result.AppId.Should().Be("new-value", "should reload from Redis after TTL expiry");
}
```

---

### 3. 异常穿透链测试（熔断器 → 业务异常）

**异常转换链：**
```
YarpTransport (抛出 BrokenCircuitException)
    ↓
AlipayProvider (捕获并转换)
    ↓
NexusEngine (统一异常处理)
    ↓
FastEndpoints (返回 503 Service Unavailable)
```

**测试代码：**
```csharp
[Fact]
public async Task ExecuteAsync_CircuitBreaker_Should_Convert_To_NexusTenantException()
{
    var mockTransport = new Mock<INexusTransport>();
    mockTransport.Setup(t => t.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new BrokenCircuitException("Circuit is open"));
    
    var adapter = new AlipayProviderAdapter(mockTransport.Object, gateway, namingPolicy);
    var config = new Mock<IProviderConfiguration>();
    
    // Act & Assert
    var ex = await Assert.ThrowsAsync<NexusTenantException>(
        () => adapter.ExecuteAsync(request, config.Object));
    
    ex.InnerException.Should().BeOfType<BrokenCircuitException>();
    ex.Message.Should().Contain("Transport layer unavailable");
}
```

---

### 4. 跨平台加密兼容性（Windows ↔ Linux）

**问题：**
Windows AES-GCM 加密的数据在 Linux 环境下解密失败（Base64 字符集）。

**解决方案（URL-safe Base64）：**
```csharp
public class AesSecurityProvider : ISecurityProvider
{
    public string Encrypt(string plainText)
    {
        // ...
        
        // ✅ 使用 URL-safe Base64（避免 +/= 导致的传输问题）
        var base64 = Convert.ToBase64String(combined)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        
        return $"v1:{base64}";
    }
    
    public string Decrypt(string cipherText)
    {
        var parts = cipherText.Split(':');
        var base64 = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');
        
        // Padding 补全
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        
        var combined = Convert.FromBase64String(base64);
        // ...
    }
}
```

**测试验证：**
```csharp
[Fact]
public void Encrypt_On_Windows_Should_Decrypt_On_Linux()
{
    var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var provider = new AesSecurityProvider(masterKey);
    
    var plainText = "MIIEvQIBA..."; // RSA 私钥
    var encrypted = provider.Encrypt(plainText);
    
    // 模拟跨平台解密（重新实例化 Provider）
    var providerOnLinux = new AesSecurityProvider(masterKey);
    var decrypted = providerOnLinux.Decrypt(encrypted);
    
    decrypted.Should().Be(plainText);
    encrypted.Should().MatchRegex(@"^v1:[A-Za-z0-9\-_]+$", "should use URL-safe Base64");
}
```

---

### 5. Demo 配置来源多样化（静态 + 动态）

**混合配置源演示：**
```csharp
public class DemoConfigurationResolver : IConfigurationResolver
{
    public async Task<ProviderSettings> ResolveAsync(ConfigurationContext context)
    {
        // 1. 静态配置（appsettings.json）
        if (context.RealmId == "demo-static")
            return LoadFromAppSettings(context);
        
        // 2. Redis 缓存（L2）
        var cached = await _redis.StringGetAsync($"config:{context.RealmId}");
        if (!cached.IsNullOrEmpty)
            return JsonSerializer.Deserialize<ProviderSettings>(cached!)!;
        
        // 3. 数据库动态加载（L3 - Mock）
        var config = await _mockRepository.GetTenantConfigAsync(context.RealmId);
        
        // 回填 Redis（TTL 30 分钟）
        await _redis.StringSetAsync(
            $"config:{context.RealmId}",
            JsonSerializer.Serialize(config),
            TimeSpan.FromMinutes(30));
        
        return config;
    }
}
```

**配置层次结构：**
```
appsettings.json (静态基础配置)
    ↓
Redis (L2 分布式缓存)
    ↓
Mock ITenantRepository (模拟数据库动态加载)
    ↓
SQL Server / PostgreSQL (生产环境数据库)
```

---

## 📚 参考资源

### 设计模式
- **适配器模式**：https://refactoring.guru/design-patterns/adapter
- **工厂模式**：https://refactoring.guru/design-patterns/factory-method
- **策略模式**：https://refactoring.guru/design-patterns/strategy

### 测试最佳实践
- **xUnit 官方文档**：https://xunit.net/
- **Moq Quickstart**：https://github.com/moq/moq4/wiki/Quickstart
- **FluentAssertions**：https://fluentassertions.com/

### .NET 性能优化
- **高性能异步编程**：https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/
- **ConcurrentDictionary 最佳实践**：https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2

---

## 🔒 License

MIT License. See [LICENSE](../LICENSE) for details.
