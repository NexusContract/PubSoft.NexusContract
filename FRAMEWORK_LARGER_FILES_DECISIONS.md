# 中等文件架构决策提取

> **从 200-300 行业务模式和指南文件中提取的架构决策**  
> 版本：1.0.0-preview.10  
> 日期：2026-01-11

---

## 📌 第一部分：回填引擎 - 响应序列化

### 【执行决策 HYDRATE-001】预编译回填委托 vs 运行时反射

**文件**：`ResponseHydrationEngine.cs` (295 行)  
**概念**：Dictionary → 强类型 Response 的反向映射

**设计分层**：

```
L1 快速路径：预编译委托（Expression Tree）
   ↓ 极快（~20ns），仅支持简单 POCO
   
L2 通用路径：运行时反射（完整功能）
   ↓ 较慢（~1000ns），支持复杂对象、集合、解密
```

**性能提升**：从反射 SetValue (~1000ns) → 原生委托 (~20ns)，**约 50 倍提升**

**使用条件**：
- ✅ 简单 POCO（无嵌套、无集合）：使用 L1 委托
- ❌ 失败自动降级：Fallback 到 L2 反射

**实现例子**：
```csharp
// 优先使用预编译委托（性能最优）
if (metadata.Hydrator != null)
{
    return (T)metadata.Hydrator(source, _namingPolicy, _decryptor);
}

// 失败则 Fallback 到反射（功能完整）
return (T)HydrateInternal(typeof(T), source, 0);
```

---

### 【执行决策 HYDRATE-002】物理红线检查（MaxNestingDepth）

**原则**：嵌套深度限制（防 StackOverflow）

**约束**：
- 深度红线：MaxNestingDepth（通常 3 层）
- 超过时抛出 NXC203 异常
- 定义在 `ContractBoundaries` 中（单一来源）

**实现**：
```csharp
if (depth > ContractBoundaries.MaxNestingDepth)
{
    throw new ContractIncompleteException(
        typeName,
        ContractDiagnosticRegistry.NXC203,
        ContractBoundaries.MaxNestingDepth
    );
}
```

**设计理由**：
- 防止无限递归导致 StackOverflow
- 强制开发者重构深度嵌套（通常是设计问题）

---

### 【执行决策 HYDRATE-003】集合大小限制（MaxCollectionSize）

**原则**：集合元素数量限制

**约束**：
- 大小红线：MaxCollectionSize（通常 10000）
- 超过时抛出 NXC303 异常
- 防止恶意大响应导致内存爆炸

**实现**：
```csharp
int itemCount = 0;
foreach (object? item in rawList)
{
    if (++itemCount > ContractBoundaries.MaxCollectionSize)
    {
        throw new ContractIncompleteException(
            declaringTypeName,
            ContractDiagnosticRegistry.NXC303,
            ContractBoundaries.MaxCollectionSize
        );
    }
}
```

---

### 【执行决策 HYDRATE-004】对称解密处理

**原则**：IsEncrypted=true 的字段自动解密

**工作流**：
```
1. 检查 ApiField.IsEncrypted
2. 如果为 true，获取加密字符串
3. 调用 IDecryptor.Decrypt()
4. 替换为明文值后赋值
```

**异常处理**：
- 如果 IsEncrypted=true 但 IDecryptor==null：抛出 NXC202
- 解密失败：异常向上冒泡

---

### 【执行决策 HYDRATE-005】强力类型转换（核心容错）

**原则**：三方 API 返回的"脏数据"自动处理

**常见场景**：
```csharp
// 支付宝 API 可能返回：
// - String "123" 但应该是 Long
// - String "2024-01-10 10:30:00" 但应该是 DateTime
// - Decimal "0.01" 但应该是 Decimal
```

**转换优先级**：
1. 同类型直接返回
2. 核心容错（Long, Decimal, Int, Double, DateTime, Boolean）
3. 通用转换（Convert.ChangeType）

**异常处理**：
- 转换失败：抛出 NXC302，携带期望类型、实际值

**例子**：
```csharp
if (underlyingType == typeof(long))
    return Convert.ToInt64(value);  // String "123" → 123L
if (underlyingType == typeof(DateTime))
    return Convert.ToDateTime(value);  // String "2024-01-10" → DateTime
```

---

### 【执行决策 HYDRATE-006】递归回填复杂对象

**原则**：嵌套对象自动递归回填

**工作流**：
```
1. 检查类型是否为复杂类型（非基元，非字符串）
2. 检查源数据是否为 IDictionary<string, object>
3. 递归调用 HydrateInternal，depth + 1
4. 返回回填后的对象
```

**深度跟踪**：
- 每层递归传递 depth + 1
- 超过 MaxNestingDepth 时抛出异常

---

## 📌 第二部分：配置解析 - 内存实现

### 【配置决策 CONFIG-MEMORY-001】纯内存配置存储（开发/测试）

**文件**：`InMemoryConfigResolver.cs` (287 行)  
**概念**：无外部依赖的配置解析器

**存储**：ConcurrentDictionary<string, ProviderSettings>（进程内）

**适用场景**：
- ✅ 单元测试（Mock 配置）
- ✅ 集成测试（预设测试数据）
- ✅ 开发环境（快速启动）
- ✅ Demo 演示（简化部署）
- ❌ 生产环境（无持久化，重启丢失）

**性能特征**：
- 查询延迟：< 1μs（纯内存）
- 内存占用：~1KB/配置
- 并发能力：ConcurrentDictionary 支持高并发读写

---

### 【配置决策 CONFIG-MEMORY-002】缓存键设计（Provider:Realm:Profile）

**原则**：三元组唯一标识配置

**格式**：
```
"{ProviderName}:{RealmId}:{ProfileId}"

例子：
"Alipay:2088123456789012:2021001234567890"
"WeChat:1234567890:100000001"
```

**大小写处理**：
- 键比较：`StringComparer.OrdinalIgnoreCase`
- ProviderName 大小写不敏感
- RealmId/ProfileId 大小写敏感

---

### 【配置决策 CONFIG-MEMORY-003】文件热更新支持

**原则**：监控配置文件变化，自动重新加载

**实现**：
```csharp
// 启用文件监控
_fileWatcher = new FileSystemWatcher(directory, fileName)
{
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
    EnableRaisingEvents = true
};

_fileWatcher.Changed += OnConfigFileChanged;
```

**处理策略**：
- 延迟 100ms 再加载（避免文件锁定）
- 异常静默处理（不中断服务）

---

### 【配置决策 CONFIG-MEMORY-004】敏感数据脱敏

**原则**：DEBUG 和 RELEASE 模式有不同的输出

**实现**：
```csharp
#if DEBUG
// DEBUG 模式：返回完整配置（包括私钥）
return _cache.Values.ToList();
#else
// 生产模式：脱敏私钥（前4位+***+后4位）
return _cache.Values.Select(MaskSensitiveData).ToList();
#endif
```

**脱敏格式**：
```
原始：MIIEvQIBADANBgkqhkiG...
脱敏：MIIE***kG9w
```

---

## 📌 第三部分：契约验证 - 全量诊断

### 【验证决策 VALIDATE-001】双重验证模式（诊断 vs 快速失败）

**文件**：`ContractValidator.cs` (265 行)  
**概念**：Contract 的完整性和安全性检查

**模式选择**：

| 模式 | 方法 | 使用场景 | 特点 |
|------|------|--------|------|
| **诊断模式** | `Validate(Type, DiagnosticReport)` | 启动期 Preload | 一次运行扫描所有错误 |
| **快速失败** | `ValidateFailFast(Type)` | 运行时动态加载 | 遇到第一个错误立即抛异常 |

**设计理由**：
- 启动期：收集所有错误，一次修复（用户体验好）
- 运行期：快速反馈，及时中断（性能优先）

---

### 【验证决策 VALIDATE-002】NXC1xx 静态结构错误集

**检查列表**（NXC101-NXC107）：

| 错误码 | 检查项 | 触发条件 |
|-------|-------|--------|
| **NXC101** | 缺少 [ApiOperation] | 类未标记 ApiOperationAttribute |
| **NXC102** | OperationId 为空 | [ApiOperation] 标注但 OperationId 未指定 |
| **NXC103** | OneWay 语义错误 | Interaction=OneWay 但 Response≠EmptyResponse |
| **NXC104** | 嵌套深度超限 | 嵌套深度 > MaxNestingDepth（通常 3） |
| **NXC105** | 循环引用检测 | 类型自身或间接引用自身 |
| **NXC106** | 加密字段未锁定 | IsEncrypted=true 但 Name 为空 |
| **NXC107** | 嵌套对象未命名 | 第 2 层及以上对象缺少 [ApiField] 的 Name |

**例子**：
```csharp
// ✗ NXC101: 缺少 [ApiOperation]
public class TradeQueryRequest { }

// ✓ NXC101 修复
[ApiOperation("alipay.trade.query")]
public class TradeQueryRequest { }

// ✗ NXC106: 加密字段未锁定
[ApiField(IsEncrypted = true)]  // 字段名是啥？
public string CardNo { get; set; }

// ✓ NXC106 修复
[ApiField("card_no", IsEncrypted = true)]  // 明确指定
public string CardNo { get; set; }
```

---

### 【验证决策 VALIDATE-003】循环引用检测

**原则**：使用 HashSet 跟踪已访问类型

**实现**：
```csharp
HashSet<Type> visited = new HashSet<Type>();

// 递归检查
if (visited.Contains(type))
{
    throw new ContractIncompleteException(...NXC105...);
}

visited.Add(type);
```

**场景**：
```
A → B → C → A  ← 检测到循环，抛 NXC105
```

---

### 【验证决策 VALIDATE-004】递归验证与路径追踪

**原则**：记录嵌套路径便于定位问题

**路径格式**：
```
TradeQueryRequest
  → BuyerInfo（Depth 1）
    → Address（Depth 2）
      → Region（Depth 3）
        → Code（Depth 4 超限）→ NXC104
```

**诊断报告输出**：
```
propertyPath: "TradeQueryRequest.BuyerInfo.Address.Region"
contextArgs: [3, "TradeQueryRequest.BuyerInfo.Address.Region", "Region"]
```

---

## 📌 第四部分：适配器模式 - Alipay 实现

### 【适配决策 ADAPTER-ALIPAY-001】无状态单例适配器

**文件**：`AlipayProviderAdapter.cs` (245 行)  
**概念**：IProvider 接口的支付宝实现

**设计原则**：
- 单例服务所有租户
- 配置通过方法参数传入（无状态）
- 每次调用创建 AlipayProvider 实例（轻量级）
- 缓存 AlipayProviderConfig（~1KB）

**架构**：
```
IProvider (NexusEngine 调用)
    ↓
AlipayProviderAdapter（配置转换 + 路由）
    ↓
AlipayProvider（实际业务逻辑）
    ↓
INexusTransport（HTTP 通信）
```

---

### 【适配决策 ADAPTER-ALIPAY-002】配置转换与缓存

**原则**：IProviderConfiguration → AlipayProviderConfig

**映射**：
```
IProviderConfiguration          AlipayProviderConfig
├─ AppId                    ├─ AppId
├─ MerchantId               ├─ MerchantId
├─ PrivateKey               ├─ PrivateKey
├─ PublicKey (RSA)          ├─ AlipayPublicKey
├─ GatewayUrl               ├─ ApiGateway
└─ ExtendedSettings          └─ UseSandbox, RequestTimeout
```

**缓存策略**：
- 缓存键：`{AppId}:{MerchantId}`
- 缓存对象：AlipayProviderConfig（~1KB）
- 线程安全：ConcurrentDictionary.GetOrAdd
- 无过期：配置通常不变

**性能**：
- 首次：~10μs（配置转换 + 字典插入）
- 后续：极快（字典查找）

---

### 【适配决策 ADAPTER-ALIPAY-003】异常转换链

**原则**：统一异常转换为 NexusTenantException

**链路**：
```
1. YarpTransport
   ├─ HttpRequestException
   └─ TaskCanceledException
        ↓
2. AlipayProvider
   （原始异常）
        ↓
3. AlipayProviderAdapter
   （捕获并转换）
        ↓
4. NexusEngine
   NexusTenantException
        ↓
5. FastEndpoints
   HTTP 错误码
```

**实现**：
```csharp
try { /* ... */ }
catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
{
    throw new NexusTenantException("Request timeout...", ex);
}
catch (NexusTenantException)
{
    throw;  // 直接抛出，不重复包装
}
catch (Exception ex)
{
    throw new NexusTenantException("Failed to execute Alipay...", ex);
}
```

---

### 【适配决策 ADAPTER-ALIPAY-004】配置验证与扩展设置

**原则**：解析 ExtendedSettings 中的支付宝特定参数

**扩展设置**：
```json
{
  "ExtendedSettings": {
    "UseSandbox": false,
    "RequestTimeout": 30
  }
}
```

**解析**：
```csharp
bool useSandbox = configuration.GetExtendedSetting<bool>("UseSandbox");
int timeoutSeconds = configuration.GetExtendedSetting<int>("RequestTimeout");

if (timeoutSeconds <= 0) timeoutSeconds = 30;  // 默认 30s
```

---

## 📌 第五部分：多 AppId 管理 - 配置指南

### 【多应用决策 MULTIAPP-001】精确匹配 vs 默认回退

**文件**：`MULTI_APPID_GUIDE.md` (245 行)  
**概念**：一个服务商下管理多个应用（AppId）

**场景**：
```
SysId: 2088123456789012（服务商）
  ├─ AppId: 2021001234567890（Web 应用）← 默认
  ├─ AppId: 2021009876543210（小程序）
  └─ AppId: 2021005555555555（H5）
```

**解析策略**：

#### 精确匹配（传入 AppId）
```csharp
var context = new ConfigurationContext("Alipay", "2088123456789012")
{
    ProfileId = "2021001234567890"  // 精确指定
};

// → 返回该 AppId 的配置
```

#### 默认匹配（不传 AppId）
```csharp
var context = new ConfigurationContext("Alipay", "2088123456789012")
{
    ProfileId = null  // 不指定
};

// 解析策略：
// 1. 查找标记为 default 的 AppId → 返回
// 2. 如果无 default，返回 first AppId
// 3. 如果无任何 AppId，抛 NotFound
```

---

### 【多应用决策 MULTIAPP-002】默认 AppId 管理 API

**原则**：支持设置、查询、修改默认 AppId

**API**：
```csharp
// 1. 添加 AppId（标记为默认）
await manager.SetConfigurationAsync(
    "Alipay", "2088123456", "2021001234567890",
    settings, isDefault: true);

// 2. 查询 AppId 列表
var appIds = await manager.GetProfileIdsAsync(
    "Alipay", "2088123456");

// 3. 查询默认 AppId
var defaultAppId = await manager.GetDefaultProfileIdAsync(
    "Alipay", "2088123456");

// 4. 修改默认 AppId
await manager.SetDefaultProfileIdAsync(
    "Alipay", "2088123456", "2021009876543210");

// 5. 删除 AppId
await manager.DeleteConfigurationAsync(
    "Alipay", "2088123456", "2021001234567890");
```

---

### 【多应用决策 MULTIAPP-003】Redis 数据结构设计

**原则**：使用 Hash + String 实现高效查询

**结构**：

#### 单个 AppId 配置
```
Key: nexus:config:Alipay:2088123456789012:2021001234567890
Type: String
Value: { "AppId": "...", "PrivateKey": "...", ... }
```

#### AppId 组索引（用于默认 AppId 查询）
```
Key: nexus:config:group:Alipay:2088123456789012
Type: Hash
Fields:
  "2021001234567890" → "2026-01-10T10:30:00Z"  (创建时间)
  "2021009876543210" → "2026-01-10T11:00:00Z"
  "default" → "2021001234567890"  (默认 AppId)
```

**查询路径**：
```
1. 精确匹配：直接读 nexus:config:Alipay:SysId:AppId
2. 默认查询：
   a) 读 nexus:config:group:Alipay:SysId 的 default 字段
   b) 如果无 default，读 Hash 的第一个字段
   c) 用获得的 AppId 读配置
```

---

### 【多应用决策 MULTIAPP-004】删除默认 AppId 的自愈

**原则**：删除 default AppId 时自动清除标记，回退到 first

**工作流**：
```
删除 AppId: 2021001234567890（当前默认）

↓

1. 删除配置：nexus:config:Alipay:2088123456:2021001234567890
2. 清除 Hash 中的该字段
3. 删除 default 标记（保留其他字段）

↓

下次查询（ProfileId=null）：
1. 读 default → null（不存在）
2. 回退：读 Hash 的第一个字段 → 2021009876543210
3. 返回该 AppId 的配置
```

---

## 📋 决策汇总

### 按决策类型分类

| 类型 | 决策 | 文件 | 优先级 |
|------|------|------|--------|
| **回填** | HYDRATE-001 | ResponseHydrationEngine | L1 |
| **回填** | HYDRATE-002 | ResponseHydrationEngine | L1 |
| **回填** | HYDRATE-003 | ResponseHydrationEngine | L1 |
| **回填** | HYDRATE-004 | ResponseHydrationEngine | L1 |
| **回填** | HYDRATE-005 | ResponseHydrationEngine | L1 |
| **回填** | HYDRATE-006 | ResponseHydrationEngine | L1 |
| **配置** | CONFIG-MEMORY-001 | InMemoryConfigResolver | L2 |
| **配置** | CONFIG-MEMORY-002 | InMemoryConfigResolver | L2 |
| **配置** | CONFIG-MEMORY-003 | InMemoryConfigResolver | L3 |
| **配置** | CONFIG-MEMORY-004 | InMemoryConfigResolver | L3 |
| **验证** | VALIDATE-001 | ContractValidator | L1 |
| **验证** | VALIDATE-002 | ContractValidator | L1 |
| **验证** | VALIDATE-003 | ContractValidator | L1 |
| **验证** | VALIDATE-004 | ContractValidator | L1 |
| **适配** | ADAPTER-ALIPAY-001 | AlipayProviderAdapter | L1 |
| **适配** | ADAPTER-ALIPAY-002 | AlipayProviderAdapter | L1 |
| **适配** | ADAPTER-ALIPAY-003 | AlipayProviderAdapter | L2 |
| **适配** | ADAPTER-ALIPAY-004 | AlipayProviderAdapter | L2 |
| **多应用** | MULTIAPP-001 | MULTI_APPID_GUIDE | L2 |
| **多应用** | MULTIAPP-002 | MULTI_APPID_GUIDE | L2 |
| **多应用** | MULTIAPP-003 | MULTI_APPID_GUIDE | L2 |
| **多应用** | MULTIAPP-004 | MULTI_APPID_GUIDE | L2 |

---

**文档生成日期**：2026-01-11  
**覆盖范围**：中等文件组（200-300 行）  
**总决策数**：22 项新增决策
