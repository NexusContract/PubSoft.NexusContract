# PubSoft.NexusContract (Elite Edition)

**Kernelized Contract Integration (KCI) Framework** - 基于 netstandard2.0 + .NET 10 的高性能、自描述、元数据驱动型支付集成引擎。

> **"显式边界优于隐式魔法。"** —— 这是本框架的最高宪法。

---

## 🏛️ 核心架构模式：REPR-P

我们遵循并扩展了 [**FastEndpoints**](https://fast-endpoints.com/) 倡导的 **REPR (Request-Endpoint-Response)** 模式，通过 **Proxying (代理化)** 机制实现了业务逻辑与物理协议的彻底解耦，形成了 **REPR-P** 模式。

**关键创新**：
- **零代码端点**：Endpoint 退化为透明代理，自动完成从 HTTP 输入到三方协议输出的全过程
- **契约即真理**：Contract 是唯一的语义内核，消灭 90% 的重复代码

### 1. 【模式 P-101】Contract Kernel (契约内核)

契约是不可变的语义内核。通过 `IApiRequest<TResponse>` 在编译期锁定强类型绑定，消灭运行时类型猜测。

### 2. 【规则 R-201】ApiField Fail-Fast (快速失败)

强制性映射约束。若 `IsEncrypted = true`，则必须显式锁定 `Name`。禁止任何形式的隐式加密名称推导，确保支付数据的物理安全性。

### 3. 【决策 A-301】Frozen Metadata (冷冻元数据)

启动期完成全量反射与约束审计，结果存入不可变的 `ReflectionCache`。运行期 **零反射** 开销，P99 曲线平滑如镜。

---

## 🛠️ 执行管道：四阶段精密手术

| 阶段 | 职责 | 组件 | 诊断域 |
| --- | --- | --- | --- |
| **1. 验证 (Validate)** | 静态契约合法性自检 | `ContractValidator` | **NXC1xx** |
| **2. 投影 (Project)** | 语义转义为协议字典 | `ProjectionEngine` | **NXC2xx** |
| **3. 执行 (Execute)** | 物理签名、编码与网络交互 | `IProvider` | **Transport** |
| **4. 回填 (Hydrate)** | 响应结果的对称还原 | `ResponseHydrationEngine` | **NXC3xx** |

---

## 📋 结构化诊断码 (NXC Codes)

我们不抛出含义模糊的异常，每一处失效都有其唯一索引。

* **NXC1xx (静态错误)**：架构红线违规（如深度 > 3，加密未锁定名）。
* **NXC2xx (出向错误)**：运行时请求报文不合规（如必填项缺失）。
* **NXC3xx (入向错误)**：三方协议对齐失败（如响应报文破坏、解密失效）。

---

## 📂 项目结构

```text
PubSoft.NexusContract/
├── src/
│   ├── NexusContract.Abstractions/  # 宪法层 (netstandard2.0)
│   │   └── Contracts, Attributes, Policies, Security, Exceptions
│   ├── NexusContract.Core/          # 引擎层 (net10.0)
│   │   └── Reflection, Projection, Hydration, Validation
│   ├── NexusContract.Client/        # 客户端 SDK (net10.0)
│   │   └── Elite Channel & MAF Integration
│   └── Providers/
│       └── NexusContract.Providers.Alipay/
├── contracts/                       # 业务契约 (纯净 POCO)
│   └── PubSoft.UnionPay.Contract/
├── examples/                        # 示例程序
└── docs/                            # 架构设计与实现章法
    └── IMPLEMENTATION.md（实现细节与组件）
```

---

## 🏗️ 架构优化策略

本框架采用以下优化手段确保高性能：

### 1. 元数据冷冻（Frozen Metadata）
- **启动期**：完成全量反射，提取并缓存所有 Contract 元数据
- **运行期**：O(1) 查询，零反射开销，确保 P50 = P99（无 GC 导致的波动）
- **内存优化**：线程池异步设计，避免线程栈堆积（800 并发同步 = 800 MB，vs 异步 8 个核心线程复用）

### 2. 预编译投影/回填（Expression Tree）
- **编译策略**：投影、回填逻辑预编译为 IL 代码，后续调用等同硬编码性能
- **相对提升**：相比运行时反射遍历，指令消耗减少数倍
- **GC 压力**：UTF-8 直通，避免 UTF-16 二倍内存分配

### 3. 确定性设计
- **无魔法**：显式边界与约束（不靠运行时推断）
- **启动红线**：违反 NXC1xx 约束的契约无法启动
- **性能可观测**：所有关键路径都有诊断码（NXC2xx/3xx）

---

## 💰 核心成本模型

| 成本来源 | 同步方案 | 异步方案（我们） | 优势 |
|---------|---------|-----------------|------|
| 并发连接数（400 TPS，2s 响应） | 800 个线程 | 8 个核心 | 线程复用 |
| 线程栈占用 | 800 × 1MB = 800 MB | < 50 MB | **节省内存** |
| GC 压力 | Gen1/Gen2 频繁 | Gen0 平稳 | **降低抖动** |
| 元数据查询 | 每次 O(n) 反射 | 启动 1 次 + 运行 O(1) | **消除热点** |

---

## 🎯 物理红线 (Boundaries)

1. **最大嵌套深度 = 3** 【决策 A-401】：强制业务扁平化，超过 3 层直接抛出 **NXC104**。
2. **显式路径锁定** 【规则 R-201】：嵌套对象必须显式标注 `[ApiField]`。
3. **路径固定与命名单调** 【决策 A-302】：禁止契约树内混用命名策略。
4. **禁止行为**：
   - ✗ Attribute 内包含行为逻辑（签名、加密实现）
   - ✗ 全局隐式序列化规则干预投影过程
   - ✗ 自动推断字段名映射

---

## 🏁 快速示例

```csharp
/// <summary>
/// 统一下单契约
/// </summary>
[ApiOperation("payment.order.create", HttpVerb.POST)]
public class OrderRequest : IApiRequest<OrderResponse>
{
    /// <summary> 商户单号 </summary>
    [ApiField("mer_order_no", IsRequired = true)]
    public string OrderNo { get; set; }

    /// <summary> 敏感卡号 (必须显式锁定名称) </summary>
    [ApiField("card_no", IsEncrypted = true)]  // 触发 【规则 R-201】 验证
    public string CardNo { get; set; }

    /// <summary> 交易金额 </summary>
    [ApiField("txn_amt")]
    public long Amount { get; set; }
}
```

---

## 💡 关键决策与背景

| 决策 | 值 | 理由 |
|------|----|----|
| 【A-401】MaxDepth | 3 | Alipay/WeChat/UnionPay 实证数据，深度均 ≤ 3 |
| 【A-301】元数据冷冻 | 启动期 | 消除运行期反射成本，P99 曲线平稳 |
| 【A-501】纯异步 | 强制 | 400 TPS × 2s 需 100 线程（同步）vs 8 线程（异步），节省 800 MB 内存 |
| 【A-201】语义投影分离 | Core→JsonNode, Provider→byte[] | Core 拒绝 ArrayPool（数据所有权清晰），编码由 Provider 自主选择（UTF-8/GBK） |

---

## 🎯 设计约束

### ✓ 必须遵守

1. **业务契约只依赖 Abstractions**
2. **加密字段必须显式命名**：`[ApiField("card_no", IsEncrypted = true)]` 【规则 R-201】
3. **契约必须标注 ApiOperation** 【模式 P-101】
4. **最大嵌套深度 ≤ 3 层** 【决策 A-401】

### ✗ 禁止行为

1. **禁止 Magic Mapping**：自动推断字段名
2. **禁止 Annotation as Behavior**：Attribute 内包含签名/加密实现
3. **禁止 Global Rule**：全局序列化规则干预投影逻辑

---

## 📚 更多信息

* **架构宪法**：查看 `src/NexusContract.Abstractions/CONSTITUTION.md`（设计禁忌与物理约束）
* **实现章法**：查看 `docs/IMPLEMENTATION.md`（四阶段管道、组件详解、场景案例）
* **示例程序**：运行 `dotnet run --project examples/NexusContract.Examples/NexusContract.Examples.csproj`
* **代码注释**：所有关键决策已标注 【决策】【规则】【模式】标签

---

## 🏛️ 结语

> **"在网络延迟的 3 秒黑暗中，我们要让内部处理的 50 微秒闪耀得像恒星。"**

本框架拒绝瑞士军刀式的全能，只做一把切开异构协议最锋利的手术刀。

## License

MIT License
