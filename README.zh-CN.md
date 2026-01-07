# NexusContract (Elite Edition)

> **中文文档 (当前)** | **[English Documentation](./README.md)**

**内核化契约集成 (Kernelized Contract Integration, KCI) 框架** - 基于 .NET Standard 2.0 + .NET 10 的高性能、自描述、元数据驱动型支付集成引擎。

> **"显式边界优于隐式魔法。"** —— 这是本框架的最高宪法。所有设计都围绕**确定性**、**可观测性**和**架构约束**展开。

---

## 🏛️ 核心架构：从 REPR 到 REPR-P

本框架扩展了 [**FastEndpoints**](https://fast-endpoints.com/) 的 **REPR (Request-Endpoint-Response)** 模式，通过 **代理化 (Proxying)** 机制实现了业务逻辑与物理协议的彻底解耦，形成 **REPR-P** 模式。

- **R**equest: 强类型业务契约 (`IApiRequest<TResponse>`)。
- **E**ndpoint: **零代码代理**，仅负责协议转换，不包含业务逻辑。
- **R**esponse: 强类型业务响应。
- **P**roxy: `NexusGateway` 作为指挥中心，协调四阶段管道，将 Endpoint 的调用代理到具体的三方 `Provider`。

---

## 🚀 核心特性

- **元数据驱动**: 启动期一次性扫描并“冷冻”所有契约元数据，运行期零反射，实现极致性能与平滑的 P99 延迟。
- **启动期体检**: 在应用启动时对所有契约进行“全景无损扫描”，生成结构化诊断报告，提前发现架构违规。
- **四阶段管道**: 所有请求都经过“验证 → 投影 → 执行 → 回填”的标准化流程，确保行为一致性。
- **结构化诊断**: 任何错误（静态、出向、入向）都有唯一的 `NXC` 诊断码，实现快速定位与修复。
- **显式边界**: 框架通过严格的“宪法”约束（如最大嵌套深度、加密字段锁定），杜绝“魔法”和不确定性。

---

## 🏁 快速上手：启动期体检

在 `Demo.Alipay.HttpApi` 中，我们展示了如何在应用启动时进行契约体检：

```csharp
// examples/Demo.Alipay.HttpApi/Program.cs

// 1. 扫描所有带 [ApiOperation] 的类型
var types = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => a.GetTypes())
    .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<ApiOperationAttribute>() != null)
    .ToArray();

// 2. 执行预加载和全景扫描
var report = NexusContractMetadataRegistry.Instance.Preload(types, warmup: true);

// 3. 打印精美的 ASCII 体检报告
report.PrintToConsole(includeDetails: true);

// 4. 如果存在致命错误，则中止启动
if (report.HasCriticalErrors)
{
    Console.Error.WriteLine("\n❌ 检测到致命的契约错误，系统即将中止启动。");
    Environment.Exit(1);
}
```

**控制台输出**:
```
========================================
NexusContract 启动期契约体检
========================================
扫描到 5 个契约类型，开始体检...

✅ 完美！(Perfect!):
   所有契约均符合 NexusContract 规范，零违宪。

✅ 所有契约均已通过体检，系统启动成功。
========================================
```

---

## 🛠️ 执行管道：四阶段精密手术

| 阶段 | 职责 | 核心组件 | 诊断域 |
| :--- | :--- | :--- | :--- |
| **1. 验证 (Validate)** | 静态契约“合宪性”自检 | `ContractValidator` | **NXC1xx** |
| **2. 投影 (Project)** | 将 POCO 契约转为协议字典 | `ProjectionEngine` | **NXC2xx** |
| **3. 执行 (Execute)** | 物理签名、编码与网络交互 | `IProvider` 实现 | **Transport** |
| **4. 回填 (Hydrate)** | 将响应字典对称还原为 POCO | `ResponseHydrationEngine` | **NXC3xx** |

---

## ⚖️ 架构宪法：三大设计禁忌

为保证系统的长期可维护性和确定性，框架严禁以下行为：

1.  **【禁忌 1】魔法映射 (Magic Mapping)**
    - **禁止**: 依赖框架自动推断字段名 (`MerchantId` → `merchant_id`)。
    - **必须**: 所有字段必须使用 `[ApiField("merchant_id")]` 显式标注。

2.  **【禁忌 2】属性即行为 (Annotation as Behavior)**
    - **禁止**: 在 `[ApiOperation]` 中定义签名算法、加密器或超时逻辑。
    - **必须**: `Attribute` 仅声明**意图**，具体**行为**由 `Provider` 独立实现。

3.  **【禁忌 3】全局序列化规则 (Global Serializer Rule)**
    - **禁止**: 使用单个全局 `JsonSerializerOptions` 或 `NamingPolicy` 控制所有 `Provider`。
    - **必须**: 每个 `Provider` 独立配置自己的命名策略（如 `SnakeCaseNamingPolicy` vs `CamelCaseNamingPolicy`），实现多协议共存。

---

## ⚡ 性能与成本模型

| 成本来源 | 传统同步方案 | **NexusContract (异步)** | 优势 |
| :--- | :--- | :--- | :--- |
| **元数据查询** | 每次 O(n) 反射 | 启动 1 次 + 运行 O(1) | **消除热点** |
| **并发模型** | 800 个线程 | 8 个核心线程复用 | **线程复用** |
| **线程栈占用** | ~800 MB | < 50 MB | **节省内存** |
| **GC 压力** | Gen1/Gen2 频繁 | Gen0 平稳 | **降低抖动** |

> *注：上述成本模型基于 400 TPS、2s 响应、8 核 CPU 的假设，用于说明架构优势。*

---

## 📂 项目结构

```text
NexusContract/
├── src/
│   ├── NexusContract.Abstractions/  # 宪法层 (netstandard2.0)
│   │   └── Contracts, Attributes, Policies, Security, Exceptions
│   ├── NexusContract.Core/          # 引擎层 (.NET 10)
│   │   └── Reflection, Projection, Hydration, Validation
│   ├── NexusContract.Client/        # 客户端 SDK
│   └── Providers/
│       └── NexusContract.Providers.Alipay/ # 支付宝提供商实现
├── examples/
│   └── Demo.Alipay.HttpApi/         # FastEndpoints 示例
└── docs/
    └── IMPLEMENTATION.md            # 实现细节与组件详解
```

---

## 📚 深度阅读

- **架构宪法**: 查看 [src/NexusContract.Abstractions/CONSTITUTION.md](src/NexusContract.Abstractions/CONSTITUTION.md)，了解框架的“物理红线”与“设计禁忌”。
- **实现章法**: 查看 [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md)，深入理解四阶段管道、组件设计与 Provider 集成范式。
- **诊断法典**: `CONSTITUTION.md` 中详细列出了所有 `NXC` 诊断码的触发条件与修正方式。

---

## 🏛️ 结语

> **"在网络延迟的 3 秒黑暗中，我们要让内部处理的 50 微秒闪耀得像恒星。"**

本框架拒绝瑞士军刀式的全能，只做一把切开异构协议最锋利、最可靠的手术刀。

## License

MIT License
