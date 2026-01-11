# NexusContract 框架接口契约清单

> 核心接口及其约束条件

**编制日期**：2026-01-11  
**版本**：1.0.0-preview.10

---

## 📐 分层接口总览

```
┌──────────────────────────────────────────────┐
│ Abstractions Layer (netstandard2.0)          │
│ ├─ IApiRequest<TResponse>                   │
│ ├─ IApiOperation                             │
│ ├─ ITenantIdentity                           │
│ ├─ IProvider                                 │
│ ├─ INexusEngine                              │
│ ├─ INexusTransport                           │
│ ├─ IConfigurationResolver                    │
│ └─ IProviderConfiguration                    │
└──────────────────────────────────────────────┘
         ↓
┌──────────────────────────────────────────────┐
│ Core Layer (.NET 10)                         │
│ ├─ NexusGateway                              │
│ ├─ NexusEngine                               │
│ ├─ ProjectionEngine                          │
│ ├─ ResponseHydrationEngine                   │
│ ├─ NexusContractMetadataRegistry             │
│ └─ ContractValidator / ContractAuditor       │
└──────────────────────────────────────────────┘
         ↓
┌──────────────────────────────────────────────┐
│ Hosting Layer (.NET 10)                      │
│ ├─ HybridConfigResolver                      │
│ ├─ TenantConfigurationManager                │
│ ├─ TenantContextFactory                      │
│ ├─ AesSecurityProvider                       │
│ └─ NexusEndpoint (FastEndpoints)             │
└──────────────────────────────────────────────┘
```

---

## 🔧 核心接口详解

### 1. IApiRequest<TResponse>

**职责**：业务意图的强类型表达

**约束**：
- 必须标注 `[ApiOperation(operationId)]`
- 实现泛型接口 `IApiRequest<TResponse>`
- TResponse 必须是引用类型且可 new

**代码示例**：
```csharp
[ApiOperation("alipay.trade.pay", HttpVerb.POST)]
public sealed class TradePayRequest : IApiRequest<TradePayResponse>
{
    [ApiField("out_trade_no")]
    [Required]
    public string OutTradeNo { get; set; }
    
    [ApiField("total_amount")]
    [Range(0.01, 999999.99)]
    public decimal TotalAmount { get; set; }
    
    [ApiField("subject")]
    public string Subject { get; set; }
}
```

**使用流程**：
```csharp
// 由 Endpoint 创建
var request = new TradePayRequest { OutTradeNo = "xxx", ... };

// 由 Engine 执行
var response = await engine.ExecuteAsync(request, tenantCtx, ct);

// 由 Endpoint 返回
SendOk(response);
```

---

### 2. IProvider

**职责**：无状态单例，代表一个支付平台的协议实现

**约束**：
- 必须实现 `ProviderName` 属性
- 必须实现 `ExecuteAsync<TResponse>` 方法
- **配置必须通过参数传入**，不能持有字段
- 必须支持并发调用（线程安全）

**接口定义**：
```csharp
public interface IProvider
{
    string ProviderName { get; }
    
    Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration configuration,
        CancellationToken ct = default)
        where TResponse : class, new();
}
```

**实现要求**：
1. 构造投影引擎（ProjectionEngine）
   - 指定 NamingPolicy（SnakeCaseNamingPolicy）
   - 指定加密器（AlipayAes256Encryptor）

2. 实现 ExecuteAsync 工作流
   ```
   Validate → Project → Sign → HTTP → Verify → Hydrate
   ```

3. 处理异常映射
   - 网络异常 → 透传
   - 签名异常 → 包装为 ContractIncompleteException
   - 响应异常 → 映射三方错误码

**使用示例**（AlipayProvider）：
```csharp
public class AlipayProvider : IProvider
{
    private readonly NexusGateway _gateway;
    private readonly INexusTransport _transport;
    private readonly ProjectionEngine _projector;
    
    public string ProviderName => "Alipay";
    
    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration config,
        CancellationToken ct = default)
        where TResponse : class, new()
    {
        // 1. 验证配置
        if (string.IsNullOrEmpty(config.PrivateKey))
            throw new ArgumentException("PrivateKey required");
        
        // 2. 投影请求
        var dict = _gateway.Project(request);
        
        // 3. 签名
        var signed = SignRequest(dict, config.PrivateKey);
        
        // 4. 发送 HTTP
        var response = await _transport.PostAsync(
            new Uri(config.GatewayUrl + "/v3/alipay/trade/query"),
            signed,
            ct);
        
        // 5. 回填响应
        return _gateway.Hydrate<TResponse>(response);
    }
}
```

---

### 3. INexusEngine

**职责**：多租户请求调度和协调

**约束**：
- 无状态单例
- 必须支持并发调用
- 必须实现 Provider 注册和路由机制
- 必须集成 IConfigurationResolver 进行 JIT 配置加载

**接口定义**：
```csharp
public interface INexusEngine
{
    Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        ITenantIdentity identity,
        CancellationToken ct = default)
        where TResponse : class, new();
}
```

**实现步骤**：
1. 路由：根据 ProviderName 或 OperationId 前缀找到 Provider
2. 构造：创建 ConfigurationContext
3. 加载：调用 IConfigurationResolver 获取配置
4. 执行：调用 Provider.ExecuteAsync
5. 返回：返回强类型响应

**Provider 路由策略**：
1. **显式路由**：TenantIdentity.ProviderName 指定
2. **前缀路由**：OperationId = "alipay.trade.pay" → AlipayProvider
3. **默认路由**：从配置文件读取 DefaultProvider
4. **元数据路由**：Contract 上的 [Provider("Alipay")] 标注

---

### 4. ITenantIdentity

**职责**：多租户身份标识

**约束**：
- 必须包含 RealmId（域）、ProfileId（档案）、ProviderName（平台）
- 必须支持序列化（日志、追踪）
- 应该支持扩展元数据（Metadata 字典）

**接口定义**：
```csharp
public interface ITenantIdentity
{
    string RealmId { get; }        // 业务单位（SysId / SPMchId）
    string ProfileId { get; }      // 应用标识（AppId / SubMchId）
    string ProviderName { get; }   // 支付平台（Alipay / WeChat）
}
```

**标准实现**（TenantContext）：
```csharp
public class TenantContext : ITenantIdentity
{
    public string RealmId { get; set; }
    public string ProfileId { get; set; }
    public string ProviderName { get; set; }
    
    // 扩展元数据（非接口部分）
    public Dictionary<string, object> Metadata { get; set; }
}
```

**创建方式**：
```csharp
// 方式 1：工厂方法（推荐）
var ctx = TenantContextFactory.FromHttpContext(httpContext);

// 方式 2：手动创建
var ctx = new TenantContext("merchant-001", "app-001", "Alipay");

// 方式 3：从 HTTP 请求体
var ctx = new TenantContext
{
    RealmId = request.Headers["X-Tenant-Realm"],
    ProfileId = request.RouteValues["profileId"],
    ProviderName = request.RouteValues["provider"]
};
```

---

### 5. IConfigurationResolver

**职责**：JIT 配置解析，支持多层缓存

**约束**：
- 必须实现 L1/L2 缓存策略
- 必须支持配置热更新（Refresh）
- 必须支持批量预热（Warmup）
- 必须提供线程安全的并发访问

**接口定义**：
```csharp
public interface IConfigurationResolver
{
    Task<IProviderConfiguration> ResolveAsync(
        ITenantIdentity identity,
        CancellationToken ct = default);
    
    Task RefreshAsync(
        ITenantIdentity identity,
        CancellationToken ct = default);
    
    Task WarmupAsync(CancellationToken ct = default);
}
```

**实现候选**：
1. **InMemoryConfigResolver**：纯内存（测试用）
2. **HybridConfigResolver**：L1（内存）+ L2（Redis）+ 数据库
3. **RedisConfigResolver**：单层 Redis
4. **DatabaseConfigResolver**：直接查询数据库

**HybridConfigResolver 的缓存策略**：
```
L1 缓存（MemoryCache）：
├─ TTL：24h 滑动过期 + 30d 绝对过期
├─ 优先级：NeverRemove
├─ 命中率：99.99%+

L2 缓存（Redis）：
├─ TTL：30min
├─ 用途：多实例共享
└─ 持久化：RDB + AOF

负缓存：
├─ 配置不存在时缓存 5min
└─ 防止恶意穿透攻击
```

---

### 6. INexusTransport

**职责**：HTTP/2 传输层，支持重试和熔断

**约束**：
- 必须使用 HTTP/2 多路复用（不能回退到 HTTP/1.1）
- 必须集成 Polly 重试/熔断策略
- 必须支持连接预热（WarmupAsync）
- 必须提供性能指标（GetHostMetrics）

**接口定义**：
```csharp
public interface INexusTransport
{
    Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct = default);
    
    Task<HttpResponseMessage> PostAsync(
        Uri requestUri,
        HttpContent content,
        CancellationToken ct = default);
    
    Task<HttpResponseMessage> GetAsync(
        Uri requestUri,
        CancellationToken ct = default);
    
    Task WarmupAsync(IEnumerable<string> hosts, CancellationToken ct = default);
    
    IReadOnlyDictionary<string, long> GetHostMetrics();
}
```

**标准实现**（YarpTransport）：
```
客户端 → YARP 反向代理 → HTTP/2 连接池 → 上游服务
                           ↓
                    Polly 重试策略
                    熔断器 (Circuit Breaker)
                    超时 (Timeout)
```

**Polly 策略配置**：
```csharp
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .Or<TimeoutException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt =>
            TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100)  // 指数退避
    );

var circuitBreakerPolicy = Policy
    .Handle<HttpRequestException>()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30)
    );
```

---

### 7. IProviderConfiguration

**职责**：Provider 物理配置

**约束**：
- 必须包含 AppId、PrivateKey、PublicKey、GatewayUrl
- 必须支持扩展设置（ExtendedSettings 字典）
- PrivateKey 必须加密存储（在 Redis 中）
- 不能在日志中输出敏感信息

**接口定义**：
```csharp
public interface IProviderConfiguration
{
    string AppId { get; }
    string MerchantId { get; }
    string PrivateKey { get; }
    string PublicKey { get; }
    Uri GatewayUrl { get; }
    string ProviderName { get; }
    
    T GetExtendedSetting<T>(string key);
    void SetExtendedSetting<T>(string key, T value);
}
```

**标准实现**（ProviderSettings）：
```csharp
public sealed class ProviderSettings : IProviderConfiguration
{
    public string AppId { get; set; }
    public string MerchantId { get; set; }
    [JsonIgnore]  // 敏感信息不序列化
    public string PrivateKey { get; set; }
    public string PublicKey { get; set; }
    public Uri GatewayUrl { get; set; }
    public string ProviderName { get; set; }
    
    public Dictionary<string, object> ExtendedSettings { get; set; }
}
```

**扩展设置用途**：
```csharp
// 沙箱模式
config.SetExtendedSetting("UseSandbox", true);

// 实现名称（RSA vs CERT）
config.SetExtendedSetting("ImplementationName", "Alipay.RSA");

// 额外参数
config.SetExtendedSetting("SubMerchantId", "2088123456789012");
```

---

## 🔐 关键约束条件

### 约束 C1：线程安全

所有接口实现必须支持并发调用：
- IProvider：无状态单例
- INexusEngine：可共享单个实例
- IConfigurationResolver：支持并发查询（SemaphoreSlim 防击穿）
- INexusTransport：支持并发请求（HTTP/2 多路复用）

### 约束 C2：配置不可变性

配置从 IConfigurationResolver 获取后，不应修改：
```csharp
// ✗ 错误
var config = await resolver.ResolveAsync(identity);
config.AppId = "new-id";  // 修改后影响其他线程

// ✓ 正确
var config = await resolver.ResolveAsync(identity);
// 只读使用，不修改
var signed = SignRequest(request, config.PrivateKey);
```

### 约束 C3：异常映射

Provider 必须将平台异常映射为 NexusContract 异常：
```csharp
try
{
    var response = await _transport.SendAsync(request, ct);
    return _gateway.Hydrate<TResponse>(response);
}
catch (HttpRequestException ex)
{
    // ✓ 正确：映射为通用异常
    throw new ContractIncompleteException(
        "HTTP request failed",
        errorCode: null,
        innerException: ex);
}
catch (JsonException ex)
{
    // ✓ 正确：映射为响应异常
    throw new ContractIncompleteException(
        "Response deserialization failed",
        errorCode: "INVALID_RESPONSE",
        innerException: ex);
}
```

### 约束 C4：取消令牌支持

所有异步接口必须支持 CancellationToken：
```csharp
public async Task<TResponse> ExecuteAsync<TResponse>(
    IApiRequest<TResponse> request,
    ITenantIdentity identity,
    CancellationToken ct = default)  // ← 必须支持
```

---

## 📋 接口实现检查清单

### 实现 IProvider

- [ ] 类标记为 sealed
- [ ] 实现 ProviderName 属性（只读）
- [ ] 实现 ExecuteAsync 方法（public async Task<TResponse>）
- [ ] 配置通过参数传入，不持有字段
- [ ] 支持泛型 TResponse（where class, new()）
- [ ] 处理所有可能的异常
- [ ] 支持 CancellationToken
- [ ] 线程安全（无静态字段）

### 实现 INexusEngine

- [ ] 注册所有 Provider（使用 FrozenDictionary）
- [ ] 实现 ExecuteAsync 方法
- [ ] 集成 IConfigurationResolver
- [ ] 支持 OperationId 路由
- [ ] 支持 ProviderName 路由
- [ ] 异常透传或映射
- [ ] 支持 CancellationToken

### 实现 IConfigurationResolver

- [ ] 实现 ResolveAsync 方法
- [ ] 实现 L1 缓存（MemoryCache）
- [ ] 实现 L2 缓存（Redis）
- [ ] 支持负缓存（配置不存在）
- [ ] 支持缓存防击穿（SemaphoreSlim）
- [ ] 实现 RefreshAsync 方法
- [ ] 实现 WarmupAsync 方法
- [ ] 支持 CancellationToken

### 实现 IProviderConfiguration

- [ ] 实现所有必要属性（AppId、PrivateKey 等）
- [ ] 支持扩展设置字典
- [ ] PrivateKey 加密存储
- [ ] 支持序列化/反序列化
- [ ] ToString() 不输出敏感信息

---

## 🔗 接口协作示例

### 完整的请求执行流程

```csharp
// 1. Endpoint 接收 HTTP 请求
public class TradePayEndpoint(INexusEngine engine, IConfigurationResolver resolver)
    : NexusEndpoint<TradePayRequest>(engine)
{
    public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
    {
        // 2. 从 HTTP 上下文构造 TenantIdentity
        var identity = TenantContextFactory.FromHttpContext(HttpContext);
        
        // 3. 调用 Engine 执行
        var response = await engine.ExecuteAsync(req, identity, ct);
        
        // 4. 返回 HTTP 响应
        await SendOkAsync(response, cancellation: ct);
    }
}

// Engine 的内部流程：
// 1. 路由：operationId = "alipay.trade.pay" → AlipayProvider
// 2. 加载：resolver.ResolveAsync(identity) → ProviderSettings
// 3. 执行：provider.ExecuteAsync(req, settings, ct)
//    ├─ 投影：req → Dictionary
//    ├─ 签名：使用 settings.PrivateKey
//    ├─ HTTP：transport.PostAsync(...)
//    └─ 回填：Dictionary → response
// 4. 返回：TradePayResponse
```

### 多实例部署场景

```
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│ Gateway 1    │  │ Gateway 2    │  │ Gateway 3    │
├──────────────┤  ├──────────────┤  ├──────────────┤
│ L1 MemCache  │  │ L1 MemCache  │  │ L1 MemCache  │
└──────┬───────┘  └──────┬───────┘  └──────┬───────┘
       └──────────────┬───────────────────┬─┘
                      │
              ┌───────▼────────┐
              │ L2 Redis Cache │
              │ (shared)       │
              └────────────────┘
                      │
              ┌───────▼────────────┐
              │ Configuration DB   │
              │ (MySQL/PostgreSQL) │
              └────────────────────┘

Pub/Sub 消息流：
┌──────────────┐
│ Config Mgmt  │ 发送 ConfigChange / MappingChange
└──────┬───────┘
       │
    Redis Pub/Sub Channel
       │
    ┌──┴──┬───────┬──────┐
    │     │       │      │
Gateway 1 2  3 监听消息
    │     │       │      │
  清除L1缓存，下次请求重新加载
```

---

**文档生成日期**：2026-01-11
