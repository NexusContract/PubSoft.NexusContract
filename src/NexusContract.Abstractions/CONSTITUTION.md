# NexusContract 宪法

> **"显式边界优于隐式魔法"** —— 这是本框架的最高法律。

本文档定义了整个架构系统的**"物理红线"**、**"设计禁忌"**和**"诊断立法"**。

---

## 第一章：物理红线（不可逾越的约束）

### 【红线 1】最大嵌套深度 = 3

```
Contract 对象树的最深允许层级为 3。
```

**为什么？**
- Alipay/WeChat/UnionPay 的实际协议深度均 ≤ 3
- 深层嵌套增加 StackOverflow 风险
- 强制业务重构为多个 Operation，提高可维护性

**触发诊断码**：**NXC104**（深度溢出）

**修正方式**：将深层结构拆分为多个独立的 Contract 类

---

### 【红线 2】显式路径锁定（禁止隐式猜测）

```
所有嵌套对象和加密字段必须显式标注 [ApiField]。
```

**禁止行为**：
```csharp
// ✗ 错误：依赖命名策略推导
public class Payment
{
    public Address ShippingAddress { get; set; }  // Name 被自动推导？
}

// ✓ 正确：显式锁定
public class Payment
{
    [ApiField("shipping_addr")]
    public Address ShippingAddress { get; set; }
}
```

**为什么？**
- 支付系统的每一个字段对应真实的金钱流转
- 字段名必须精确，不容许"聪明的猜测"
- 隐式猜测导致：某开发写 `ShippingAddr`，某开发读 `shipping_address`，钱跑错账户

**触发诊断码**：**NXC107**（嵌套对象未命名）、**NXC106**（加密字段未锁定）

---

### 【红线 3】加密字段必须显式命名

```
IsEncrypted = true 时，必须同时指定 Name。
```

**禁止行为**：
```csharp
// ✗ 致命错误
[ApiField(IsEncrypted = true)]  // 没有 Name？加密后的字段名是啥？
public string CardNo { get; set; }

// ✓ 正确
[ApiField("card_no", IsEncrypted = true)]
public string CardNo { get; set; }
```

**为什么？**
- 加密后的字段在协议层是 Blob，不可被自动转换
- 必须有一个"明确的身份"才能在解密时找到它

**触发诊断码**：**NXC106**（加密字段未锁定名称）

---

### 【红线 4】隐式字段的命名一致性（最佳实践）

```
推荐：未显式标注 [ApiField] 的属性应遵循统一的命名风格。
绝对：显式标注 [ApiField("xxx")] 的字段可以使用任意命名（精确映射三方API）。
```

**最佳实践**：
```csharp
// ✓ 推荐：隐式字段统一风格（依赖 NamingPolicy 推导）
public class Order
{
    [ApiField]  // 推导为 order_id（统一使用 SnakeCase）
    public string OrderId { get; set; }
    
    [ApiField]  // 推导为 customer_name（统一使用 SnakeCase）
    public string CustomerName { get; set; }
}

// ✓ 允许：显式字段精确映射三方API（不受 NamingPolicy 约束）
public class AlipayLegacyRequest
{
    [ApiField("buyer_id")]       // 三方字段：SnakeCase
    public string BuyerId { get; set; }
    
    [ApiField("BuyerLogonId")]   // 三方字段：PascalCase（老接口遗留）
    public string BuyerLogonId { get; set; }
}
```

**为什么？**
- `[ApiField("xxx")]` 显式命名的**本质目的**就是精确锁定字段名，不管三方API用什么命名风格
- 三方API（如 Alipay 老接口）可能混用命名策略，我们必须精确映射
- NamingPolicy 只影响**隐式字段**的推导，显式字段直接使用 `Name` 属性

**架构保证**：
- ProjectionEngine 优先使用 `ApiField.Name`（精确映射）
- 仅在 `Name` 为空时才调用 `NamingPolicy.ConvertName`（自动推导）

**注意**：当前版本**未实现 NXC108 检查**，命名策略混用不会触发编译错误，但建议遵循上述最佳实践以提高代码可读性。

---

## 第二章：设计禁忌（代价极高的错误）

### 【禁忌 1】魔法映射（Magic Mapping）

**定义**：自动推断字段名或类型转换，无显式声明。

**禁止代码**：
```csharp
// 错误：期望框架"聪明地"把 MerchantId → merchant_id
public class Order
{
    public string MerchantId { get; set; }
}

// 框架无法确定：到底是 merchant_id、merchantId、merchantID、merchant_ID？
```

**代价**：
- 字段映射错误，导致"鬼钱"问题（交易数据丢失）
- 难以追踪（魔法越智能，Bug 越隐蔽）

**正确做法**：所有字段都显式标注
```csharp
[ApiField("merchant_id")]
public string MerchantId { get; set; }
```

---

### 【禁忌 2】属性即行为（Annotation as Behavior）

**定义**：在 Attribute 中包含执行逻辑（签名、加密、超时重试等）。

**禁止代码**：
```csharp
// ✗ 错误：Attribute 中混入业务逻辑
[ApiOperation(
    Operation = "alipay.trade.pay",
    SigningAlgorithm = "RSA2",      // ← 不！这是 Provider 的职责
    Encryptor = "AES256",            // ← 不！这也是 Provider 的职责
    Timeout = 30000
)]
public class PaymentRequest : IApiRequest<PaymentResponse> { }
```

**代价**：
- Attribute 变成"配置的垃圾场"
- 不同 Provider（Alipay vs UnionPay）无法独立定制策略
- 新增 Provider 时，必须修改所有 Contract

**正确做法**：Attribute 仅声明意图，Provider 独立实现
```csharp
// Contract：仅声明意图
[ApiOperation("alipay.trade.pay", HttpVerb.POST)]
public class PaymentRequest : IApiRequest<PaymentResponse> { }

// Provider：自主决定实现
public class AlipayProvider
{
    private readonly ProjectionEngine _engine = new(
        new SnakeCaseNamingPolicy(),
        new AlipayRsa2Encryptor()    // ← Provider 决定
    );
}
```

---

### 【禁忌 3】全局序列化规则（Global Serializer Rule）

**定义**：单个全局 JsonSerializerOptions 或命名策略控制所有投影。

**禁止代码**：
```csharp
// ✗ 错误：全局污染
public static class GlobalConfig
{
    public static JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    
    public static INamingPolicy NamingPolicy = new SnakeCaseNamingPolicy();
}

// 所有 Provider 都被这个全局策略束缚
```

**代价**：
- Alipay 需要 SnakeCase，UnionPay 需要 CamelCase，无法共存
- 修改全局策略会影响所有 Provider（连锁爆炸）

**正确做法**：Per-Provider 独立策略
```csharp
public class AlipayProvider
{
    private readonly ProjectionEngine _engine = new(
        new SnakeCaseNamingPolicy()  // ← 仅此 Provider 有效
    );
}

public class UnionPayProvider
{
    private readonly ProjectionEngine _engine = new(
        new CamelCaseNamingPolicy()  // ← 仅此 Provider 有效
    );
}
```

---

## 第三章：诊断立法（NXC 法典）

### 静态错误区间：NXC1xx （启动期立即感知）

| 代码 | 违规类型 | 触发条件 | 修正方式 |
|------|--------|--------|--------|
| **NXC101** | 缺少 ApiOperation | 未标注 `[ApiOperation]` | 添加操作标注 |
| **NXC102** | Operation 为空 | `Operation` 属性值为 null/empty | 指定有效的操作ID |
| **NXC103** | 交互模式约束 | OneWay 却指定非 EmptyResponse | OneWay → TResponse 必须是 EmptyResponse |
| **NXC104** | 深度溢出 | 嵌套 > 3 层 | 拆分为多个 Contract |
| **NXC105** | 循环引用 | A 包含 B，B 包含 A | 重设计对象图 |
| **NXC106** | 加密字段未锁定 | `IsEncrypted=true` 无 `Name` | 添加 `[ApiField("name", IsEncrypted=true)]` |
| **NXC107** | 嵌套对象未命名 | 复杂属性无 `[ApiField]` | 标注 `[ApiField("name")]` |

### 出向错误区间：NXC2xx （投影时感知）

| 代码 | 违规类型 | 触发条件 | 修正方式 |
|------|--------|--------|--------|
| **NXC201** | 必填项缺失 | `IsRequired=true` 的字段为 null | 初始化对象时赋值 |
| **NXC202** | 字段类型不匹配 | 投影时发现类型转换失败 | 检查业务逻辑或 Contract 定义 |
| **NXC203** | 集合大小溢出 | 列表元素 > MaxCollectionSize | 分页处理或减少数据量 |

### 入向错误区间：NXC3xx （回填时感知）

| 代码 | 违规类型 | 触发条件 | 修正方式 |
|------|--------|--------|--------|
| **NXC301** | 响应字段缺失 | 回填时字段不存在 | 更新 Contract 或调查三方 API |
| **NXC302** | 响应类型不匹配 | 反序列化失败 | 检查三方返回格式或 Contract |
| **NXC303** | 解密失败 | 密文无法被 IEncryptor 解密 | 确认加密密钥和算法正确 |

---

## 第四章：核心决策（为什么这样做）

### 【决策 A-301】元数据冷冻（Frozen Metadata）

```
启动期：执行全量反射，构建 NexusContractMetadataRegistry
运行期：零反射查询，所有操作 O(1)
```

**背景**：
- 反射成本：启动期一次性反射（随契约规模与字段数而变） vs 运行期 O(1) 缓存查询（微观开销）。具体时间取决于契约规模与硬件配置，不应被视为通用的时间保证。
- 支付系统需要 400 TPS，反射会在高并发下累积成问题（需通过架构手段避免）

**实现**：
- ContractValidator 在应用启动时执行一遍
- 验证失败立即抛异常，应用无法启动
- 验证成功后，元数据被冷冻（Flyweight 模式）

**保证**：应用运行期内，元数据永不变化

---

### 【决策 A-401】最大嵌套深度 = 3

```
强制业务扁平化。任何超过 3 层的结构必须重新设计。
```

**背景**：
- 实证数据：Alipay/WeChat/UnionPay 最深均为 3 层
- 超过 3 层时，AI 生成代码时容易产生不可预期的行为

**权衡**：
- 失去：任意深度嵌套的灵活性
- 得到：明确的边界，启动时的静态检查

---

### 【决策 A-501】纯异步强制

```
系统中禁止任何同步 API。所有 I/O 都必须异步。
```

**背景**：
- 支付系统目标 400 TPS
- 单次外部调用 2s（HTTP → Alipay 网络）
- 同步方案：400 TPS × 2s ÷ 8 核 = 100 个线程（800 MB 内存消耗）
- 异步方案：8 个线程 × 50:1 复用比 = 400 TPS（8 MB 内存消耗）

**内存节省**：792 MB

> **前提说明**：上面计算基于平均外部调用延迟为 2s、机器为 8 核、线程栈约 1MB 的假设。该计算用于说明同步 vs 异步在内存占用上的差异性（数学推导），不是对任何部署环境的性能或响应时间保证。

---

### 【决策 A-201】语义投影分离

```
Core 层：POCO → JsonNode（语义转换，无编码关切）
Provider 层：JsonNode → byte[]（编码由 Provider 自主选择）
系统拒绝 ArrayPool：数据所有权必须清晰
```

**背景**：
- ArrayPool 在某些负载下可以带来性能提升，但会带来所有权模糊与池化膨胀风险，需谨慎使用
- 每个 Contract 请求一份独立 byte[]，由 GC 托管，所有权清晰
- 延迟示例：在实验或特定部署中可能观察到 ArrayPool 导致的波动；UTF-8 编码方案通常在实际部署中更平滑。具体数值取决于负载与环境，不能作为普适承诺。

---

## 第五章：架构师的克制

本宪法的核心：**显式 > 隐式，清晰 > 聪明，确定性 > 极致性能**

当面临选择时，问自己：
1. **这是显式的吗？** 或者我期待框架"猜"？
2. **如果这个字段错了，我能在 5 分钟内找到 Bug 吗？**
3. **一个新加入的工程师能在不问老同志的情况下理解这个设计吗？**
4. **AI 生成 700 个接口时，会不会产生逻辑污染？**

如果答案是"否"，那你违宪了。

---

## 结语

> "没有魔法，只有边界。这些红线不是束缚，而是自由。"

本宪法对应的代码约束已嵌入到：
- `ContractValidator`（NXC 检查）
- `ProjectionEngine`（投影规则）
- `NexusContractMetadataRegistry`（元数据冷冻）
- 各 Provider 的独立配置

开发者首次看到 NXC1xx 错误时，直接查阅本文第三章，就能秒懂问题所在。

AI 智能体在分析 NexusContract 时，应首先阅读本宪法，然后再去看代码。本宪法就是 AI 的"宪法约束"。