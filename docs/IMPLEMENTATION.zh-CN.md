# NexusContract.Core - 集成与优化手册

**定位**：本手册不讲"如何实现每个类"，而是讲"**为什么这样架构**"和"**怎么把这些组件组装成 Provider**"。

每个组件的内部实现细节请参考源码注释和【决策】标签；本手册的任务是让你理解"手感"——工程直觉。

---

## 📐 架构分层：四阶段管道回顾

```
           输入 (Contract)
                  │
        ┌─────────▼─────────┐
        │   阶段1: 验证      │  ← ContractValidator (NXC1xx)
        │  (Fail-Fast)      │     + ContractAuditor
        └─────────┬─────────┘
                  │
        ┌─────────▼─────────┐
        │   阶段2: 投影      │  ← ProjectionEngine
        │  (POCO→Dict)      │     + PropertyAuditResult (约束检查)
        └─────────┬─────────┘
                  │
        ┌─────────▼─────────┐
        │   阶段3: 执行      │  ← Provider.Execute() (HTTP + Signing)
        │ (HTTP + Signing)  │
        └─────────┬─────────┘
                  │
        ┌─────────▼─────────┐
        │   阶段4: 回填      │  ← ResponseHydrationEngine
        │  (Dict→POCO)      │     + 强制类型纠偏
        └─────────┬─────────┘
                  │
              输出 (Response)
```

---

## 🔍 核心概念：六大组件的工程逻辑

### 1. NexusContractMetadataRegistry（契约元数据注册表）

**工程逻辑**：
- **元数据冷冻**: 启动时，对每个 Contract 进行反射一次，提取 Attribute 元数据并缓存（`ConcurrentDictionary`）。首次访问 O(n)，后续 O(1)。
- **启动期体检 (`Preload`)**: 提供 `Preload` 方法，调用 `ContractValidator` 的诊断模式对所有契约进行全景扫描，并返回一份结构化的 `DiagnosticReport`。
- **懒加载 (`GetMetadata`)**: 在运行时首次访问某个契约时，调用 `ContractValidator` 的执法模式（Fail-Fast）进行验证，然后缓存元数据。

**手感**：为什么要这样做？
```
方案 A（Alipay 模式）：每个请求来时反射属性 → O(n) 反射成本 × 高并发 = GC 压力 + 性能衰减
方案 B（我们的做法）：启动一次反射 + 全量体检 → 后续 O(1) 缓存查询 → P50 = P99 无波动
```

参考：[src/NexusContract.Core/Reflection/NexusContractMetadataRegistry.cs](../../src/NexusContract.Core/Reflection/NexusContractMetadataRegistry.cs)

---

### 2. ContractValidator（双模宪法执法官）

**工程逻辑**：
- **双重模式**: `ContractValidator` 现在以两种模式运行，以平衡启动期全面性与运行期效率。
  - **诊断模式 (`Validate`)**: 在启动期 `Preload` 期间调用。它会**无损扫描**整个契约对象树，收集所有违规行为并记录到 `DiagnosticReport` 中，而**不抛出异常**。
  - **执法模式 (`ValidateFailFast`)**: 在运行时懒加载 (`GetMetadata`) 期间调用。它继承了原始的 **Fail-Fast** 行为，遇到第一个违规立即抛出 `ContractIncompleteException`，确保运行期安全。

**手感**：这不是单一的“代码检查”，而是“体检医生 + 现场执法官”的结合体。启动时给你一份完整的体检报告，运行时对任何意外的动态加载执行严格的现场执法。

参考：[ContractValidator.cs](../../src/NexusContract.Core/Reflection/ContractValidator.cs)

---

### 3. ProjectionEngine（投影引擎）

**工程逻辑**：
- **ProjectionEngine**：递归遍历 Contract 对象，应用命名策略和加密。支持嵌套对象和列表，深度限制 3 层。
- 将投影逻辑预编译为 Expression Tree，后续调用直接执行编译后的委托，性能等同硬编码。

**手感**：为什么要这样做？
```
方案 A（反射遍历）：每次投影都反射属性 → O(n) 反射成本
方案 B（Expression Tree）：首次编译，后续执行编译代码 → O(n) 执行成本，相比反射开销显著降低
```

关键特性：
- ✅ 支持深度限制（MaxDepth = 3，防止 AI 生成过深结构）
- ✅ 自动处理嵌套对象和列表（递归投影）
- ✅ 强制应用加密和命名策略（无"魔法"）
- ✅ 必填字段检查（违反者抛 NXC2xx 异常）

参考：[ProjectionEngine.cs](../../src/NexusContract.Core/Projection/ProjectionEngine.cs)

---

### 4️⃣ ResponseHydrationEngine（回填引擎）【NEW】

**工程逻辑**：
- **ResponseHydrationEngine**：执行投影的反向流程：Dictionary → POCO。强制类型纠偏（String "100" → Long 100）。
- 同样用 Expression Tree 预编译回填逻辑，避免运行时反射。

**手感**：这是"对称性"的体现。

投影的约束（必填检查、加密）在回填时也要执行（但方向相反）：
```
投影：Contract 有字段 → 检查必填 → 投影到 Dictionary
回填：Dictionary 有字段 → 检查完整性 → 回填到 Contract，同时做类型纠偏

如果三方返回 "status": "100" (String)，但你的 Contract.Status 是 Long，
回填引擎自动转换，无需业务代码处理。
```

参考：[ResponseHydrationEngine.cs](../../src/NexusContract.Core/Hydration/ResponseHydrationEngine.cs)

---

### 5️⃣ NexusGateway + Provider（指挥部）

**工程逻辑**：
- **NexusGateway**：协调上述所有组件，执行四阶段管道。
- **Provider**：封装平台特定逻辑（签名、HTTP、响应验证），调用 Gateway 执行。
- **Endpoint**：框架特定集成层（FastEndpoints、Minimal API、MVC），调用 Provider。

**架构层次**：
```
Endpoint (框架特定)
    ↓ 调用
Provider (平台特定，框架无关)
    ↓ 调用
NexusGateway (通用引擎)
    ↓ 操作
Contract (纯POCO)
```

**实例（FastEndpoints）**：
```csharp
// 1. Endpoint 层（FastEndpoints 特定）
public class TradePayEndpoint : AlipayEndpointBase<TradePayRequest>
{
    // 零代码！路由和响应类型从 Contract 自动推断
}

// 2. Provider 层（框架无关）
public class AlipayProvider
{
    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request, CancellationToken ct)
    {
        // 调用 Gateway，传入 HTTP 执行器（签名、网络调用）
        return await _gateway.ExecuteAsync(request, HttpExecutor, ct);
    }
}

// 3. Gateway 自动执行四阶段管道
```

参考：[NexusGateway.cs](../../src/NexusContract.Core/NexusGateway.cs) 和 [AlipayProvider.cs](../../src/Providers/NexusContract.Providers.Alipay/AlipayProvider.cs)

---

## 🔌 命名策略与加密器（可插拔）
Naming Policy 有三种内置实现：
- **SnakeCaseNamingPolicy**：MerchantId → merchant_id（Alipay 标准）
- **CamelCaseNamingPolicy**：MerchantId → merchantId（WeChat 标准）
- **PascalCaseNamingPolicy**：MerchantId → MerchantId（保持原样）

参考：[NamingPolicies.cs](../../src/NexusContract.Core/Policies/Impl/NamingPolicies.cs)

---

## 🔗 Provider 集成范式

这是最重要的部分：**怎么把这套机制组装到 Provider 里**。

### 全流程示例：支付宝当面付

我们将以 `Demo.Alipay.HttpApi` 为例，展示如何将 `AlipayProvider` 集成到 `FastEndpoints` 中，实现一个零代码的业务端点。

#### 步骤 1：定义契约 (Contract)

契约是所有逻辑的起点。它定义了请求、响应以及与三方 API 的映射关系。

```csharp
// 文件: examples/Demo.Alipay.Contract/Transactions/TradePayRequest.cs

[ApiOperation("alipay.trade.pay", HttpVerb.POST)]
public class TradePayRequest : IApiRequest<TradePayResponse>
{
    [ApiField("out_trade_no", IsRequired = true)]
    public string MerchantOrderNo { get; set; }

    [ApiField("total_amount", IsRequired = true)]
    public decimal TotalAmount { get; set; }

    [ApiField("subject", IsRequired = true)]
    public string Subject { get; set; }

    [ApiField("scene", IsRequired = true)]
    public string Scene { get; set; }
}
```
- `[ApiOperation]` 定义了此契约对应的支付宝接口 (`alipay.trade.pay`) 和 HTTP 动词。
- `IApiRequest<TradePayResponse>` 在编译期锁定了响应类型。
- `[ApiField]` 将我们的业务属性 (`MerchantOrderNo`) 精确映射到支付宝的协议字段 (`out_trade_no`)。

#### 步骤 2：创建 Provider 并定义 HTTP 执行器

`AlipayProvider` 封装了与支付宝交互的所有细节，如签名、验签和网络通信。其核心是 `ExecuteAsync` 方法，该方法内部定义了一个 `HttpExecutor` 委托，并将其传递给 `NexusGateway`。

```csharp
// 文件: src/Providers/NexusContract.Providers.Alipay/AlipayProvider.cs

public class AlipayProvider : IAsyncDisposable, IDisposable
{
    // ... 构造函数和配置 ...

    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request, CancellationToken ct)
        where TResponse : class, new()
    {
        // 定义 HTTP 执行器：处理实际的网络通信、签名、验证
        async Task<IDictionary<string, object>> HttpExecutor(
            CoreExecutionContext context,
            IDictionary<string, object> projectedRequest)
        {
            // 1. 构建 OpenAPI v3 URL (e.g., /v3/alipay/trade/pay)
            Uri requestUri = BuildOpenApiV3Uri(context.OperationId);

            // 2. 准备认证参数并生成签名
            string signature = GenerateSignature(...);

            // 3. 构建并发送 HTTP 请求
            using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
            // ... 设置请求头和内容 ...
            HttpResponseMessage httpResponse = await _httpClient.SendAsync(httpRequest, ct);

            // 4. 解析并验证响应签名
            string responseContent = await httpResponse.Content.ReadAsStringAsync(ct);
            IDictionary<string, object> responseDict = ParseAlipayResponse(responseContent);
            if (!VerifyResponseSignature(responseDict))
                throw new InvalidOperationException("验签失败");

            return responseDict;
        }

        // 委托给 Gateway 执行四阶段管道
        return await _gateway.ExecuteAsync(request, HttpExecutor, ct);
    }
}
```
- `HttpExecutor` 是**唯一**需要关心平台协议细节（签名、URL 格式等）的地方。
- `NexusGateway` 负责调用 `HttpExecutor`，并在此之前和之后执行“验证”、“投影”和“回填”阶段。

#### 步骤 3：FastEndpoints 集成与零代码端点

在 `Demo.Alipay.HttpApi` 中，我们通过一个基类 `AlipayEndpointBase` 来自动化所有端点的通用逻辑。

```csharp
// 文件: examples/Demo.Alipay.HttpApi/Endpoints/AlipayEndpointBase.cs

public abstract class AlipayEndpointBase<TRequest>(AlipayProvider alipayProvider) 
    : Endpoint<TRequest> where TRequest : class, IApiRequest
{
    public override void Configure()
    {
        // 从契约的 [ApiOperation] 自动提取并配置路由
        var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(typeof(TRequest));
        string route = metadata.Operation.Operation.Replace("alipay.", "").Replace('.', '/');
        Post(route); // e.g., "alipay.trade.pay" -> "trade/pay"
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        // 直接调用 Provider 执行请求
        var response = await alipayProvider.ExecuteAsync(req, ct);
        await SendAsync(response, cancellation: ct);
    }
}
```
- `Configure()` 方法利用 `NexusContractMetadataRegistry` 读取契约元数据，自动将 `alipay.trade.pay` 转换为 RESTful 路由 `trade/pay`。
- `HandleAsync()` 简单地将请求转发给 `AlipayProvider`。

有了这个基类，我们的业务端点就实现了**真正的零代码**：

```csharp
// 文件: examples/Demo.Alipay.HttpApi/Endpoints/TradePayEndpoint.cs

public class TradePayEndpoint(AlipayProvider alipayProvider) 
    : AlipayEndpointBase<TradePayRequest>(alipayProvider)
{
    // 无需任何代码！
    // 路由、请求处理、响应全部由基类和 NexusGateway 自动完成。
}
```

#### 步骤 4：在 `Program.cs` 中注册服务

最后，在应用程序的入口点注册所有服务。

```csharp
// 文件: examples/Demo.Alipay.HttpApi/Program.cs

var builder = WebApplication.CreateBuilder(args);

// 注册支付宝 Provider 和相关服务
builder.Services.AddAlipayProvider(new AlipayProviderConfig { ... });

// 注册 FastEndpoints
builder.Services.AddFastEndpoints();

var app = builder.Build();

// 配置 FastEndpoints 中间件和路由前缀
app.UseFastEndpoints(c => c.Endpoints.RoutePrefix = "v3/alipay");

app.Run();
```

**集成关键点**：
- ✅ **单一事实来源**: `TradePayRequest` 契约是唯一需要定义业务逻辑和协议映射的地方。
- ✅ **职责分离**: `AlipayProvider` 关心支付宝，`AlipayEndpointBase` 关心 FastEndpoints，`NexusGateway` 关心执行流程。它们各司其职。
- ✅ **零代码端点**: 业务开发人员只需定义契约，无需编写任何端点代码，极大地提高了开发效率和一致性。

---

## ⚡ 性能策略与成本模型

### 四阶段管道的复杂度分析

| 阶段 | 组件 | 复杂度 | 技术手段 | 诊断码 |
|------|------|--------|---------|--------|
| 1. 验证 | ContractValidator | **O(1)** | 启动期冻结，运行期秒查询 | NXC1xx |
| 2. 投影 | ProjectionEngine | **O(n)** | n=字段数，预编译执行 | NXC2xx |
| 3. 执行 | Provider + JsonHandler | **O(n)** | n=字段数，UTF-8 直通 | Transport |
| 4. 回填 | ResponseHydrationEngine | **O(n)** | n=响应字段数，强制类型纠偏 | NXC3xx |

**关键特性**：
- ✅ **运行期零反射**：元数据在启动期一次性冷冻到 FrozenDictionary
- ✅ **预编译执行**：投影/回填逻辑编译为 IL 代码，执行速度等同硬编码
- ✅ **内存确定性**：UTF-8 直通（非 UTF-16），GC 压力由 .NET 运行时统一管理
- ✅ **确定的可观测性**：每处失效都有诊断码（NXC），便于追踪与调试

### 性能成本与收益对标

**投影策略对标**（POCO → Dictionary）：
```
方案 A（反射遍历）：每次请求反射属性 → O(n) 反射 + GC 压力
方案 B（我们）：启动期冻结元数据 + 预编译 Expression Tree → O(n) 编译执行

成果：消除运行期反射热点，P50 = P99（无 GC 导致的波动）
```

**回填策略对标**（Dictionary → POCO）：
```
方案 A（反射设值）：每次响应反射 SetValue → O(n) 反射 + 类型转换成本高
方案 B（我们）：预编译 Expression Tree + 强制类型纠偏 → O(n) 编译执行 + 自动类型转换

成果：对称性设计，减少三方 API 类型混乱导致的异常
```

**内存模型对标**（线程管理）：
```
同步方案：400 TPS × 2s 响应 = 800 并发 = 800 个线程 = 800MB 栈占用
异步方案：8 个核心 = 线程池复用 = < 50MB 栈占用

成果：避免线程池耗尽，确定性支持高并发场景
```

---

## 🛠️ 高级集成场景

### 场景 1：多态支付方式（通联支付）

```
问题：同一个 Operation，支持多种支付方式（余额支付、银行卡支付、微信支付）
      这些方式的字段结构完全不同，怎么在一个 Contract 里表达？

答案：使用多态 POCO + 显式字段名
```

定义多态层次：

```csharp
public abstract class PayMethodBase { }

public class BalancePayMethod : PayMethodBase
{
    [ApiField("BALANCE")]  // 显式路径锁定
    public List<BalanceItem> Items { get; set; }
}

public class BankCardPayMethod : PayMethodBase
{
    [ApiField("CARD")]
    public string CardNo { get; set; }
    
    [ApiField("amount")]
    public decimal Amount { get; set; }
}

public class ConsumeApplyRequest : IApiRequest<ConsumeApplyResponse>
{
    [ApiField("payMethod")]
    public PayMethodBase PayMethod { get; set; }  // 多态！
}
```

Gateway 会自动递归投影和回填，无需 if/switch 判断！

---

### 场景 2：复杂嵌套与深度限制

```
问题：AI 生成的接口可能会生成超过 3 层的嵌套结构，这违反了 NXC104

解决：必须在 Contract 设计阶段就拆分
```

错误示例（会被拒绝）：

```csharp
public class BadRequest : IApiRequest<BadResponse>
{
    [ApiField("level1")]
    public Level1 L1 { get; set; }
}

public class Level1
{
    [ApiField("level2")]
    public Level2 L2 { get; set; }
}

public class Level2
{
    [ApiField("level3")]
    public Level3 L3 { get; set; }
}

public class Level3
{
    [ApiField("level4")]  // ← NXC104！深度限制！
    public Level4 L4 { get; set; }
}
```

正确做法：拆分为多个 Request

```csharp
// Request 1: 获取顶层数据
[ApiOperation("query.top", HttpVerb.POST)]
public class TopLevelRequest : IApiRequest<TopLevelResponse> { }

// Request 2: 获取细节数据（新的独立请求）
[ApiOperation("query.detail", HttpVerb.POST)]
public class DetailRequest : IApiRequest<DetailResponse> { }
```

---

## 🐛 常见问题与调试

### Q1：为什么我的契约验证失败（NXC106）？
**A**：加密字段必须显式指定 Name。
```csharp
// ❌ 错误
[ApiField(IsEncrypted = true)]
public string CardNo { get; set; }

// ✅ 正确
[ApiField("card_no", IsEncrypted = true)]
public string CardNo { get; set; }
```

### Q2：投影性能怎么检查？
**A**：看 ProjectionEngine 的日志，确保是"预编译执行"而不是"首次编译"。

### Q3：回填时类型转换失败怎么办？
**A**：ResponseHydrationEngine 会自动转换简单类型（String ↔ Int/Long/Decimal）。如果无法转换，检查三方报文格式是否与 Contract 对齐。

---

## 📝 总结

本手册核心：**不讲实现细节，讲工程手感**。

关键理解：
- ✅ **NexusContractMetadataRegistry**：启动冻结 → 运行时零反射
- ✅ **ContractValidator/Auditor**：Fail-Fast 宪法执法 → 坏契约无法启动
- ✅ **ProjectionEngine/ExpressionTree**：递归投影 + 预编译 → 微观开销（显著优于直接反射，远小于网络 I/O）
- ✅ **ResponseHydrationEngine**：对称回填 + 强制类型纠偏 → 多态安全
- ✅ **NexusGateway/ProxyEndpoint**：四阶段自动化 → 零代码端点

**最后的话**：这套机制的目标不是为了"代码简洁"，而是为了"支付系统的可靠性"。在目标并发与延迟约束下（例如 400 TPS 与可接受的 P99 延迟场景），确定性胜过任何技巧。明确前提能避免误读。

继承 `ContractValidator` 并覆盖方法：

```csharp
public class StrictContractValidator : ContractValidator

