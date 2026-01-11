# 中型文件架构决策提取

> **从 80-200 行关键实现文件中提取的架构决策**  
> 版本：1.0.0-preview.10  
> 日期：2026-01-11

---

## 📌 第一部分：安全设计 - AES 加密策略

### 【安全决策 SEC-AES-001】AES256-CBC 硬件加速加密

**文件**：`AesSecurityProvider.cs` (108 行)  
**概念**：对称加密的高性能实现

**原则**：
- 算法：AES256-CBC（256 位密钥）
- IV 策略：随机生成，每次加密不同
- 性能：硬件加速（CPU AES-NI 指令集）
- 耗时：~5μs/2KB 密钥（< 网络 IO 延迟）

**关键设计**：

| 维度 | 策略 |
|------|------|
| **加密算法** | AES256-CBC，PKCS7 填充 |
| **密钥长度** | 256 位（32 字节），来自环境变量 |
| **初始化向量** | 随机生成，编码到密文头部 |
| **版本前缀** | v1:（便于未来算法升级） |
| **格式** | `v1:[IV(16字节)][密文(变长)]`（Base64 编码） |

**适用场景**：
- Redis L2 缓存中的 PrivateKey 加密
- 配置文件中的敏感字段加密
- ISV 多租户环境的密钥隔离

**安全约束**：
```csharp
// ✓ 正确：每次加密使用不同 IV（防止模式攻击）
aes.GenerateIV();

// ✗ 错误：重复使用 IV（极易破解）
aes.IV = staticIv;
```

**实现特点**：
- 版本前缀便于密钥轮换（未来升级为 v2: AES-GCM）
- 主密钥存储于环境变量（不在代码中）
- 加密/解密都支持（满足存储和传输需求）

---

### 【安全决策 SEC-AES-002】密钥存储隔离策略

**原则**：主密钥从环境变量读取，不在代码/配置文件中

**验证机制**：
```csharp
// 构造时校验密钥有效性
if (_masterKey.Length != 32)
    throw new ArgumentException("Master key must be 32 bytes");
```

**部署建议**：
1. 生成 256 位随机密钥（32 字节）
2. Base64 编码后存储在 Secret Manager（Azure Key Vault / 阿里云密钥管理）
3. 应用启动时从环境变量 `NEXUS_MASTER_KEY` 读取
4. 不在 appsettings.json 中出现

---

## 📌 第二部分：身份提取 - HTTP 协议适配

### 【适配决策 ADAPT-HTTP-001】多源租户身份提取（优先级链）

**文件**：`TenantContextFactory.cs` (185 行)  
**概念**：从 HTTP 请求中提取业务身份

**提取优先级**（从高到低）：

```
1️⃣ 请求头（X-Tenant-Realm / X-Tenant-Profile / X-Provider-Name）
   ↓
2️⃣ 查询参数（?realm_id=xxx&profile_id=xxx&provider=xxx）
   ↓
3️⃣ 请求体 JSON（{ "realm_id": "xxx", "profile_id": "xxx" }）
```

**设计原理**：
- **请求头优先**：性能最优（HTTP 解析第一步）
- **查询参数次之**：兼容 RESTful 风格
- **JSON 体最后**：性能成本最高（需 EnableBuffering）

**跨平台别名支持**：

| 标识符 | 支持别名 | 平台含义 |
|-------|--------|--------|
| **RealmId** | realm_id, sys_id, sp_mch_id | 支付宝服务商 ID |
|  | sysid, realmid, spmchid |  |
| **ProfileId** | profile_id, app_id, sub_mch_id | 支付宝应用 ID |
|  | profileid, appid, submchid |  |
| **ProviderName** | provider_name, provider, channel | 平台标识 |
|  | providername |  |

**关键实现特点**：
1. **大小写不敏感**：`realm_id` 和 `REALM_ID` 等同
2. **流式查找**：先查标准名称，再查别名
3. **缓冲支持**：请求体多次读取（EnableBuffering）
4. **异常容错**：JSON 解析失败时忽略（不中断请求）

**使用场景**：
```csharp
// 在 FastEndpoints Endpoint 中自动调用
public override async Task HandleAsync(TradeQueryRequest req, CancellationToken ct)
{
    var tenantCtx = await TenantContextFactory.CreateAsync(HttpContext);
    // → 自动从请求头/查询参数/JSON 体中提取标识
}
```

---

### 【适配决策 ADAPT-HTTP-002】JSON 体缓冲与流重置

**原则**：在 ASP.NET Core 中多次读取请求体

**实现**：
```csharp
// 启用缓冲（允许多次读取）
context.Request.EnableBuffering();

// 读取 JSON
using var jsonDoc = await JsonDocument.ParseAsync(context.Request.Body);

// 重置流位置供后续 Endpoint 读取
context.Request.Body.Position = 0;
```

**设计收益**：
- 工厂层提取身份信息
- Endpoint 层仍可正常反序列化完整请求
- 无数据丢失，避免二次序列化

---

## 📌 第三部分：配置上下文 - 身份抽象

### 【多租户决策 MT-CONTEXT-001】ConfigurationContext 的三元组设计

**文件**：`ConfigurationContext.cs` (177 行)  
**概念**：配置查询的业务身份映射

**三元组定义**：

```csharp
public sealed class ConfigurationContext : ITenantIdentity
{
    public string ProviderName { get; }    // 渠道：Alipay, WeChat, UnionPay
    public string RealmId { get; }         // 域：ISV 服务商 ID
    public string ProfileId { get; set; }  // 档案：子商户/应用 ID
}
```

**术语映射**：

| 概念 | 字段 | 支付宝含义 | 微信含义 |
|------|------|----------|--------|
| **Provider** | ProviderName | "Alipay" | "WeChat" |
| **Realm** | RealmId | sys_id（系统商） | sp_mchid（服务商） |
| **Profile** | ProfileId | app_id（应用） | sub_mchid（特约商户） |

**关键设计**：

#### 1️⃣ Realm 概念（逻辑隔离的业务空间）
- **定义**：ISV 服务商或代理商的标识
- **隔离性**：不同 Realm 的配置完全隔离（防越权）
- **Redis 键**：`nxc:map:{realm}:{provider}` 或 `nxc:inst:{realm}:{provider}:{profile}`

#### 2️⃣ Profile 概念（具体业务实例）
- **定义**：Realm 下的具体执行单元
- **可选性**：可为空（某些场景由 RealmId 推导默认 Profile）
- **自动补全**：HybridConfigResolver 支持 null ProfileId 自动解析

#### 3️⃣ 扩展元数据
```csharp
// 支持配置分片和多环境
public IDictionary<string, object> Metadata { get; set; }

// 使用示例
ctx.WithMetadata("Environment", "sandbox")
   .WithMetadata("Region", "cn");
```

---

### 【多租户决策 MT-CONTEXT-002】大小写不敏感的 ProviderName 比较

**原则**：ProviderName 使用大小写不敏感比较

**实现**：
```csharp
public override bool Equals(object obj)
{
    // ProviderName: "Alipay" == "alipay" ✓
    return string.Equals(ProviderName, other.ProviderName, 
        StringComparison.OrdinalIgnoreCase);
}

public override int GetHashCode()
{
    // 哈希码也需大小写不敏感
    int hash = 17;
    hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(ProviderName);
    // ...
}
```

**设计收益**：
- 缓存命中率提高（无论大小写都能命中）
- 防止开发者因大小写导致的 Bug
- 符合 URL 规范（域名大小写不敏感）

---

### 【多租户决策 MT-CONTEXT-003】流式 API 支持链式调用

**原则**：ConfigurationContext 支持链式配置

```csharp
var ctx = new ConfigurationContext("Alipay", realmId)
    .WithProfileId(profileId)
    .WithMetadata("Environment", "prod")
    .WithMetadata("Region", "cn");
```

**设计收益**：
- 代码简洁易读
- 避免中间变量
- 语义清晰（与建造者模式无关，仅返回 this）

---

## 📌 第四部分：执行网关 - 管道编排

### 【执行决策 GATEWAY-001】NexusGateway 纯异步设计

**文件**：`NexusGateway.cs` (174 行)  
**概念**：唯一的支付网关门面

**核心约束**：
- ✅ 仅有异步方法（ExecuteAsync）
- ❌ 无 ExecuteSync（强制异步）
- ❌ 禁止使用 .Wait() 或 .Result

**为什么纯异步**？

#### 1️⃣ 线程池效率
```
场景：2s 平均响应 × 400 TPS = 800 个并发请求
同步模式：需要 800 个线程（占用 16MB * 800 = 12.8GB 栈内存）
异步模式：400 个线程足以（I/O 释放线程，无需新增）
```

#### 2️⃣ GC 压力降低
- 同步：大量线程栈帧留存 → Full GC 频繁
- 异步：栈帧快速释放 → GC 暂停时间短

#### 3️⃣ 代码流可控
- 避免 AI 生成代码时意外产生死锁（同步等待 + ConfigureAwait 混用）
- 强制使用 await（不能用 .Result）

**实现示例**：
```csharp
// ✓ 正确
public async Task<TResponse> ExecuteAsync<TResponse>(
    IApiRequest<TResponse> request,
    Func<ExecutionContext, IDictionary<string, object>, 
        Task<IDictionary<string, object>>> executorAsync,
    CancellationToken ct = default)
{
    // 投影 → 执行 → 回填
    var projected = _projectionEngine.Project<object>(request);
    var responseDict = await executorAsync(executionContext, projected)
        .ConfigureAwait(false);  // ← 不切换回 UI 线程
    return _hydrationEngine.Hydrate<TResponse>(responseDict);
}

// ✗ 错误（不存在）
// public TResponse ExecuteSync<TResponse>() { }
```

---

### 【执行决策 GATEWAY-002】ConfigureAwait(false) 的必要性

**原则**：所有 async/await 都使用 ConfigureAwait(false)

**理由**：
1. **无 UI 线程**：支付系统总是后端，无需切换回 UI 上下文
2. **线程池复用**：继续使用现有线程，无上下文切换开销
3. **避免死锁**：防止 .Result 等待 + ConfigureAwait(true) 导致的死锁

**性能对比**：
```
ConfigureAwait(true)  → 上下文切换（~1-2μs 开销）
ConfigureAwait(false) → 直接继续（无开销）

× 1000 请求/秒 = 1-2ms 的累积开销
```

---

### 【执行决策 GATEWAY-003】四阶段管道在网关中的映射

**原则**：ExecuteAsync 自动编排四个阶段

```csharp
// 1️⃣ 验证（启动期已完成，运行期跳过）
ContractMetadata metadata = 
    NexusContractMetadataRegistry.Instance.GetMetadata(requestType);

// 2️⃣ 投影（C# → Dictionary）
IDictionary<string, object> projectedRequest = 
    _projectionEngine.Project<object>(request);

// 3️⃣ 执行（HTTP 调用）
IDictionary<string, object> responseDict = 
    await executorAsync(executionContext, projectedRequest)
        .ConfigureAwait(false);

// 4️⃣ 回填（Dictionary → C#）
TResponse response = 
    _hydrationEngine.Hydrate<TResponse>(responseDict);
```

**设计收益**：
- 所有调用走同一路径（无快捷方式）
- 便于监控和诊断（4 个阶段对应 4 个性能指标）
- 异常定位精确（哪个阶段失败一目了然）

---

### 【执行决策 GATEWAY-004】OperationId 提取与传递

**原则**：OperationId 从 [ApiOperation] 提取，传递给 Provider

**工作流**：
```csharp
// 1. 从元数据提取
string? operationId = metadata.Operation?.OperationId;

// 2. 传递给执行器
ExecutionContext executionContext = new ExecutionContext(operationId);

// 3. Provider 可根据 OperationId 选择证书、签名算法
await executorAsync(executionContext, projectedRequest);
```

**使用场景**：
- 支付宝：不同 OperationId 可能使用不同证书
- 微信：某些操作需要特定的 API 版本
- 通用：Provider 可根据操作类型动态选择配置

---

### 【执行决策 GATEWAY-005】异常转译与诊断

**原则**：ContractIncompleteException 转译为诊断信息

```csharp
catch (ContractIncompleteException ex)
{
    IDictionary<string, object> diagnosticData = 
        ex.GetDiagnosticData();
    string category = 
        ContractDiagnosticRegistry.GetCategory(ex.ErrorCode);
    
    // 接入日志系统（可选）
    // logger.LogError(new { category, diagnostic_data });
}
```

**诊断数据包含**：
- ErrorCode：NXC1xx/2xx/3xx
- Category：静态错误 / 出向错误 / 入向错误
- Details：具体字段名、类型、缺失项等

---

### 【执行决策 GATEWAY-006】投影和回填的独立使用

**原则**：支持仅投影或仅回填（不经过完整流程）

```csharp
// 仅投影（序列化请求）
public IDictionary<string, object> Project<TContract>(TContract contract)

// 仅回填（反序列化响应）
public TResponse Hydrate<TResponse>(IDictionary<string, object> source)
```

**使用场景**：
- 构建 GraphQL 解析器（需要手动投影）
- 调试工具（需要可视化投影/回填过程）
- 第三方适配器（仅需投影或仅需回填）

---

## 📋 决策汇总

### 按决策类型分类

| 类型 | 决策 | 文件 | 优先级 |
|------|------|------|--------|
| **安全** | SEC-AES-001 | AesSecurityProvider | L1 |
| **安全** | SEC-AES-002 | AesSecurityProvider | L1 |
| **适配** | ADAPT-HTTP-001 | TenantContextFactory | L2 |
| **适配** | ADAPT-HTTP-002 | TenantContextFactory | L2 |
| **多租户** | MT-CONTEXT-001 | ConfigurationContext | L1 |
| **多租户** | MT-CONTEXT-002 | ConfigurationContext | L2 |
| **多租户** | MT-CONTEXT-003 | ConfigurationContext | L3 |
| **执行** | GATEWAY-001 | NexusGateway | L1 |
| **执行** | GATEWAY-002 | NexusGateway | L1 |
| **执行** | GATEWAY-003 | NexusGateway | L1 |
| **执行** | GATEWAY-004 | NexusGateway | L2 |
| **执行** | GATEWAY-005 | NexusGateway | L2 |
| **执行** | GATEWAY-006 | NexusGateway | L3 |

### 按优先级分类

**L1 绝对必须**：
- SEC-AES-001：AES256-CBC 加密
- SEC-AES-002：密钥存储隔离
- MT-CONTEXT-001：三元组设计
- GATEWAY-001：纯异步执行
- GATEWAY-002：ConfigureAwait(false)
- GATEWAY-003：四阶段管道编排

**L2 强烈推荐**：
- ADAPT-HTTP-001：多源身份提取
- ADAPT-HTTP-002：JSON 缓冲重置
- MT-CONTEXT-002：大小写不敏感比较
- GATEWAY-004：OperationId 提取
- GATEWAY-005：异常转译诊断

**L3 可选优化**：
- MT-CONTEXT-003：流式 API
- GATEWAY-006：独立投影/回填

---

## 🔗 与已有决策的关联

### 与 CF 系列（宪法基础）的关联
- CF-001（显式）→ ADAPT-HTTP-001（显式提取多个别名）
- CF-002（确定性）→ MT-CONTEXT-002（大小写不敏感的确定性比较）
- CF-003（启动期失败）→ GATEWAY-003（启动期元数据冻结）

### 与 SD 系列（安全设计）的关联
- SD-001（加密字段）→ SEC-AES-001（加密实现）
- SD-003（私钥保护）→ SEC-AES-002（密钥存储隔离）

### 与 ED 系列（执行管道）的关联
- ED-002（四阶段）→ GATEWAY-003（投影 → 执行 → 回填）

### 与 MT 系列（多租户）的关联
- MT-001（Realm/Profile）→ MT-CONTEXT-001（三元组映射）
- MT-003（JIT 解析）→ MT-CONTEXT-001（ConfigurationContext）

---

**文档生成日期**：2026-01-11  
**覆盖范围**：中小文件组（80-200 行）  
**总决策数**：12 项新增决策
