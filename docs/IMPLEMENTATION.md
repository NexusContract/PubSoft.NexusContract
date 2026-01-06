# PubSoft.NexusContract.Core - 集成与优化手册

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

### 1. ReflectionCache（元数据冻结）

**工程逻辑**：
- **ReflectionCache**：启动时，对每个 Contract 进行反射一次，提取 Attribute 元数据并缓存（ConcurrentDictionary）。首次 O(n)，后续 O(1)。
- 将全量元数据冻结为高效缓存，让 400 TPS 的高频查询完全零损耗。

**手感**：为什么要这样做？
```
方案 A（Alipay 模式）：每个请求来时反射属性 → O(n) 反射成本 × 高并发 = GC 压力 + 性能衰减
方案 B（我们的做法）：启动一次反射 → 后续 O(1) 缓存查询 → P50 = P99 无波动
```

参考：[src/NexusContract.Core/Reflection/ReflectionCache.cs](../../src/NexusContract.Core/Reflection/ReflectionCache.cs)

---

### 2. ContractValidator + ContractAuditor + PropertyAuditResult（三重审计）

**工程逻辑**：
- **ContractValidator**：Fail-Fast 执法，检查 NXC1xx（静态结构）和 NXC104-105（递归深度/循环）。
- **ContractAuditor**：逐字段审计，检查加密字段的命名约束（NXC106）、嵌套对象的显式路径锁定（NXC107）。
- **PropertyAuditResult**：缓存审计结果，避免运行时重复检查。

**手感**：这不是"代码检查"，而是"宪法执法"。任何违反 NXC1xx-3xx 的契约都在启动时被卡死。

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

### 5️⃣ NexusGateway + NexusProxyEndpoint（指挥部）【NEW】

**工程逻辑**：
- **NexusGateway**：协调上述所有组件，执行四阶段管道。
- **NexusProxyEndpoint**：零代码端点，仅负责路由声明，所有业务逻辑都交给 NexusGateway。

**手感**：这就是 REPR-P 模式的灵魂。
```
传统方式：Endpoint 中写业务逻辑（投影、加密、签名、HTTP、回填）
我们的做法：Endpoint 继承 NexusProxyEndpoint，一行代码搞定

public class PaymentEndpoint : NexusProxyEndpoint<PaymentRequest, PaymentResponse>
{
    // 就这样！Gateway 会自动执行四阶段管道
}
```

参考：[NexusGateway.cs](../../src/NexusContract.Core/NexusGateway.cs) 和 [NexusProxyEndpoint.cs](../../src/NexusContract.Core/Endpoints/NexusProxyEndpoint.cs)

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

### 全流程示例：银联支付请求

```csharp
// 1. 定义契约类（基于实际 contracts/PubSoft.UnionPay.Contract）
[ApiOperation("unionpay.trade.pay", HttpVerb.POST, Version = "5.1.0")]
public class PaymentRequest : IApiRequest<PaymentResponse>
{
    [ApiField(IsRequired = true, Description = "商户系统订单号，必须唯一")]
    public string MerchantOrderId { get; set; }
    
    [ApiField("txn_amt", IsRequired = true, Description = "交易金额，单位：分")]
    public long Amount { get; set; }
    
    [ApiField("card_no", IsEncrypted = true, IsRequired = true, Description = "支付银行卡号")]
    public string CardNumber { get; set; }
    
    [ApiField("goods_desc", Description = "商品或订单描述")]
    public string GoodsDescription { get; set; }
}

// 2. 创建 Provider，配置 Gateway
public class UnionPayProvider : AlipayProvider  // 继承基础Provider
{
    public UnionPayProvider(AlipayProviderConfig config, NexusGateway gateway) 
        : base(config, gateway) { }
    
    // 3. 执行请求（实际实现HTTP调用）
    public async Task<PaymentResponse> PayAsync(PaymentRequest request)
    {
        // 定义HTTP执行器（实际网络调用）
        async Task<IDictionary<string, object>> HttpExecutor(
            CoreExecutionContext context, 
            IDictionary<string, object> projectedRequest)
        {
            // 这里实现实际的HTTP调用、签名、加密等
            // 使用 projectedRequest 中的字段发送到银联API
            // 返回解析后的响应字典
            
            // 示例（伪代码）：
            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsJsonAsync("https://api.unionpay.com/pay", projectedRequest);
            return await response.Content.ReadFromJsonAsync<IDictionary<string, object>>();
        }
        
        // Gateway 会自动执行四阶段管道：
        // 1️⃣ 验证：ContractValidator 检查 NXC1xx-3xx
        // 2️⃣ 投影：ProjectionEngine 将 request 转为 Dictionary
        // 3️⃣ 执行：调用 HttpExecutor，发送签名后的请求
        // 4️⃣ 回填：ResponseHydrationEngine 将 response 转为强类型
        return await ExecuteAsync(request, HttpExecutor);
    }
}

// 4. FastEndpoints 集成（路由由Provider管理）
public class PaymentEndpoint : NexusProxyEndpoint<PaymentRequest>
{
    // 路由配置在Provider层面实现，不需要每个Endpoint重复定义
    // Provider会根据[ApiOperation]自动映射路由
}
```

**集成关键点**：
- ✅ 契约类定义一次，后续所有逻辑都通过 Gateway 自动化
- ✅ Provider 只需要提供"HTTP 执行器"（签名、加密、网络调用）
- ✅ Endpoint 零代码，完全是代理模式

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
- ✅ **ReflectionCache**：启动冻结 → 运行时零反射
- ✅ **ContractValidator/Auditor**：Fail-Fast 宪法执法 → 坏契约无法启动
- ✅ **ProjectionEngine/ExpressionTree**：递归投影 + 预编译 → 微观开销（显著优于直接反射，远小于网络 I/O）
- ✅ **ResponseHydrationEngine**：对称回填 + 强制类型纠偏 → 多态安全
- ✅ **NexusGateway/ProxyEndpoint**：四阶段自动化 → 零代码端点

**最后的话**：这套机制的目标不是为了"代码简洁"，而是为了"支付系统的可靠性"。在目标并发与延迟约束下（例如 400 TPS 与可接受的 P99 延迟场景），确定性胜过任何技巧。明确前提能避免误读。

继承 `ContractValidator` 并覆盖方法：

```csharp
public class StrictContractValidator : ContractValidator

