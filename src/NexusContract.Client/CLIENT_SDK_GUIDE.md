# NexusContract.Client SDK - 精英通道指南

> **"契约是共享的，但工具是进化的。"** — 架构师宣言

## 🎯 使命

**NexusContract.Client** 是 .NET 10 级别的精英 SDK，为支付集成开发者提供**无与伦比的开发体验（DX）**和**性能**。

---

## 📦 技术堆栈

- **.NET 10**（目标框架）
- **C# 12/13**（Primary Constructors, 一级构造函数）
- **System.Net.Http.Json**（原生 JSON 序列化）
- **Microsoft.Extensions.Http**（IHttpClientFactory 连接池）
- **Microsoft.Extensions.DependencyInjection**（.NET 原生 DI）

---

## 🚀 核心特性

### 1. **零冗余代码** — Primary Constructor 魔法

```csharp
// Traditional（传统）:
public class NexusGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly INamingPolicy _namingPolicy;
    private readonly Uri _baseUri;

    public NexusGatewayClient(HttpClient httpClient, INamingPolicy namingPolicy, Uri baseUri)
    {
        _httpClient = httpClient;
        _namingPolicy = namingPolicy;
        _baseUri = baseUri;
    }
}

// .NET 10 Elite（精英版）:
public sealed class NexusGatewayClient(
    HttpClient httpClient,
    INamingPolicy namingPolicy,
    Uri? baseUri = null)
{
    // 直接使用 httpClient, namingPolicy, baseUri
    // 无需任何样板代码！
}
```

### 2. **自动类型推断** — 一行搞定

```csharp
// 调用时自动推断 TResponse，无需显式泛型参数
var response = await client.SendAsync(new PaymentRequest 
{ 
    Amount = 1000,
    MerchantOrderId = "ORDER-123"
});

// response 的类型自动推断为 PaymentResponse
// 编译器零猜测，开发者零烦恼
```

### 3. **结构化诊断** — NXC 错误码体系

```csharp
try
{
    var response = await client.SendAsync(paymentRequest);
}
catch (NexusCommunicationException ex)
{
    // ex.ErrorCode: "NXC101", "NXC201", etc.
    // ex.ErrorCategory: "ValidationError", "NetworkError"
    // ex.DiagnosticData: { "ClassName": "PaymentRequest", "Field": "Amount" }
    // ex.HttpStatusCode: 503
    
    Console.WriteLine($"[{ex.ErrorCode}] {ex.GetDiagnosticSummary()}");
}
```

### 4. **点分标识符路由** — 多网关支持

```csharp
// 支持 "allinpay.yunst", "unionpay.api" 等点分标识符
var factory = NexusGatewayClientFactory.CreateBuilder(namingPolicy)
    .RegisterGateway("allinpay", new Uri("https://alipay.yunst.api/"))
    .RegisterGateway("unionpay", new Uri("https://union.api.com/"))
    .Build();

// 自动按标识符的第一部分路由
var alipayClient = factory.CreateClient("allinpay.trade.pay", httpClient);
var unionPayClient = factory.CreateClient("unionpay.query", httpClient);
```

---

## 🔧 DI 集成（3 步搞定）

### 步骤 1：注册服务

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddNexusContractClient(namingPolicy: new SnakeCaseNamingPolicy())
    .AddGateway("allinpay", new Uri("https://alipay.yunst.api/"))
    .AddGateway("unionpay", new Uri("https://union.api.com/"))
    .RegisterFactory();

var app = builder.Build();
```

### 步骤 2：注入使用

```csharp
[ApiController]
[Route("api/[controller]")]
public class PaymentController(NexusGatewayClient client)
{
    [HttpPost("pay")]
    public async Task<IActionResult> Pay(PaymentRequest request)
    {
        try
        {
            var response = await client.SendAsync(request);
            return Ok(response);
        }
        catch (NexusCommunicationException ex)
        {
            return StatusCode((int?)ex.HttpStatusCode ?? 500, 
                new { error = ex.GetDiagnosticSummary() });
        }
    }
}
```

### 步骤 3：享受开发 + 配置生产参数

```csharp
// 🔧 针对三方支付的连接池配置示例
var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = 20,  // 重要：长连接（3s级别）需要更大的池
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    UseCookies = false,
    AllowAutoRedirect = false
};

var client = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(30)  // 对账/大批量查询等极端场景
};
```

- 自动连接池管理（IHttpClientFactory）
- ⚠️ **重要**：连接池大小 → 需根据并发量 × 单次请求耗时（3s）调整
- 自动超时配置（30秒，适用于极端场景）
- 自动 JSON 序列化/反序列化
- 自动契约验证和诊断

---

## 🎪 性能指标

### SDK 内部损耗（纯引擎开销）

> **重要前提**：下表为典型观测的相对成本，基于缓存已热身、契约规模为小到中等（字段数少）的场景。它用于说明各操作的相对权重，而不是向任何运行环境作出时间保证。

| 操作 | 相对成本 | 说明 |
|------|---------|------|
| 初始化客户端 | 最小 | 一次性启动开销 |
| 契约审计 (Validation) | 极小 | O(1) FrozenDictionary 查询 |
| 投影操作 (Projection) | 小 | O(n) 字段迭代，预编译 IL 执行 |
| POCO 序列化 | 小 | JsonSerializer 缓存与优化 |
| 回填操作 (Hydration) | 中等 | O(n) 类型转换，受响应体大小影响 |

### 全链路耗时（E2E，包含三方 API）

| 场景 | P95 耗时 | 说明 |
|------|---------|------|
| **SDK + 三方网关** | 0.5s - 3s | 常规支付、转账、对账 |
| **HSM 硬件加密签名** | 200ms - 1500ms | 支付宝、银联加密机处理 |
| **公网往返延迟** | 50ms - 500ms | 取决于地域和线路质量 |
| **三方业务逻辑处理** | 100ms - 1000ms | 风控、实名认证、账户查询 |
| **超时告警 (Threshold)** | > 10s | 异常情况，应主动告警 |
| **连接超时 (Timeout 配置)** | 30s | 针对对账、大批量查询等极端场景 |

---

## 📋 实际使用示例

### 支付宝支付集成

```csharp
// 1. 定义契约（纯业务层）
[ApiOperation("allinpay.trade.pay", HttpVerb.POST, Version = "5.1.0")]
public class AlipayPaymentRequest : IApiRequest<AlipayPaymentResponse>
{
    [ApiField("merchant_id", IsRequired = true)]
    public string MerchantId { get; set; }

    [ApiField("amount", IsRequired = true)]
    public long Amount { get; set; }

    [ApiField("order_id", IsRequired = true)]
    public string OrderId { get; set; }
}

public class AlipayPaymentResponse
{
    public string TradeNo { get; set; }
    public string Status { get; set; }
}

// 2. 使用客户端（纯路由层）
public class PaymentService(NexusGatewayClient client)
{
    public async Task<AlipayPaymentResponse> PayAsync(AlipayPaymentRequest request)
    {
        return await client.SendAsync(request);
    }
}

// 3. 完成！没有其他代码了
```

---

## 🛡️ 异常处理链

```
IApiRequest.SendAsync()
    ↓
[Contract Validation] → ContractIncompleteException
    ↓
[JSON Serialization] → JsonSerializationException
    ↓
[HTTP Communication] → HttpRequestException
    ↓
[Response Deserialization] → JsonSerializationException
    ↓
└─→ All wrapped as: NexusCommunicationException
```

每一层异常都被自动转换为 `NexusCommunicationException`，包含：
- 📍 ErrorCode (NXC1xx/NXC2xx/NXC3xx)
- 📊 ErrorCategory (ValidationError, NetworkError, etc.)
- 🔍 DiagnosticData (上下文信息)
- 📞 HttpStatusCode (网络层状态)

---

## 📌 性能特性（由 Core 引擎提供）

Client 层通过 `gateway.Project()` 和 `gateway.Hydrate()` 调用获得以下性能保证：

| 特性 | 实现层 | 收益 |
|------|--------|------|
| **FrozenDictionary 元数据缓存** | Core | O(1) 契约查询，无锁 |
| **Expression Tree 预编译** | Core | 投影/回填：预编译委托，显著优于反射（微观开销） |
| **UTF-8 直接流式处理** | Core | 避免 ArrayPool 的数据所有权风险 |

---

## ⚠️ 性能预期（务必了解）

### 三方支付网关的现实耗时分层

在局域网微服务中，响应时间可达 50-100ms。但涉及**三方支付网关**时：

| 环节 | 耗时 | 说明 |
|------|------|------|
| **Client 内部处理** | 小（远小于网络往返） | 契约审计 + 投影 + 回填 |
| **公网往返延迟** | 50-500ms | 地域和线路质量 |
| **三方硬件加密机 (HSM)** | 200-1500ms | 支付宝、银联加密处理 |
| **三方业务逻辑** | 100-1000ms | 风控、实名认证等 |
| **总耗时（常规）** | **0.5s - 3s** | 正常预期 |
| **告警阈值** | > 10s | 应该主动告警 |

**结论**：3 秒是常规预期，而非异常。99% 的性能瓶颈不在 Client 层，而在三方网关。

---

#### 🏛️ Core 引擎的"工业质感"执行流

```
NexusGateway.ExecuteAsync()
    ↓
1️⃣ [契约审计] — FrozenDictionary 元数据快速查询（O(1)）
    ↓
2️⃣ [投影 Projection] — Core 引擎预编译 Expression Tree，POCO → Dict（O(n)，受字段数影响）
    ↓
3️⃣ [网络 I/O] — ValueTask 异步，无阻塞等待（典型 0.5s-3s，为全链路主要耗时）
    ↓
4️⃣ [回填 Hydration] — Core 引擎 Expression Tree 委托，Dict → POCO（O(n)，受响应体大小影响）
    ↓
5️⃣ [响应] — 返回强类型对象，开发者得到类型安全
```

**特点**：
- 每一层都有可观察的性能指标
- 没有隐藏的黑盒优化
- 支持运行时热更新（反射 + 缓存的完美结合）
- 调试时可以逐行追踪

---

#### 🏁 "克制"背后的智慧---

## 🛡️ 实用建议（处理 3 秒级别的响应）

### 1️⃣ 异步优先设计

```csharp
// ❌ 同步等待（阻塞 UI）
var response = await client.SendAsync(request);  // 可能等 3s
return Ok(response);

// ✅ 异步模式（立即返回）
await client.SendAsync(request);  // fire-and-forget，后续轮询
return Accepted();  // 202 告知客户端请求已接收
```

### 2️⃣ 连接池配置（关键）

对于 3 秒级别的请求，单个连接会被长时间占用。需要配置足够的池：

```csharp
// 公式：MaxConnectionsPerServer = (期望并发数 × 单次耗时秒数) + buffer
// 例：100 并发 × 3s → 需要 300+ 个连接

var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = 300,
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
};

var httpClient = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(30)  // 对账、大批量查询等极端场景
};
```

### 3️⃣ 监控和告警

在支付链路埋点，及时发现瓶颈：

```csharp
var sw = Stopwatch.StartNew();
try
{
    var response = await client.SendAsync(request);
    sw.Stop();
    
    _logger.LogInformation("Payment completed in {DurationMs}ms", sw.ElapsedMilliseconds);
    
    // 告警阈值
    if (sw.ElapsedMilliseconds > 10000)
        _alerts.SendSlowPaymentAlert(request.OrderId, sw.ElapsedMilliseconds);
}
catch (NexusCommunicationException ex)
{
    _metrics.RecordFailure(ex.ErrorCode, sw.ElapsedMilliseconds);
}
```

---

## 📚 相关文档

- [README.md](../../README.md) — 项目概览
- [CONSTITUTION.md](../NexusContract.Abstractions/CONSTITUTION.md) — 架构约束和规则（包含【R-201】ArrayPool 决策）
- [IMPLEMENTATION.md](../../docs/IMPLEMENTATION.md) — Core 引擎的投影/回填设计

---

## 🎯 总结

**NexusContract.Client** 是为支付集成而生的精英 SDK：

✅ **零冗余代码**：Primary Constructor 消除样板  
✅ **强大诊断**：NXC 错误码体系清晰定位问题  
✅ **高效路由**：点分标识符 + FrozenDictionary = O(1) 网关查询  
✅ **安全第一**：拒绝 ArrayPool，确保数据所有权明确  
✅ **开发友好**：DI 集成，一行代码开启支付功能
✅ 开发高效（自动类型推断）
✅ 运维友好（结构化异常）

**这是为渴望优雅和高效的团队准备的利器。**
