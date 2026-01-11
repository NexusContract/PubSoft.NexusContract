# NexusContract 架构大清洗——137→12 的物理脱水重构
## "月月红"宪法锚点确立方案

> **执行时间：** 2026-01-11 ~ 2026-02-28（8周工程）  
> **目标：** 从散乱的 137 条决策 → 12 条物理主权锚点  
> **核心理念：** 物理寻址优先，删除所有"猜测"与"兼容"逻辑

---

## 🗺️ 第一部分：12 条核心宪法映射

| 宪法序号 | 核心主权原则 | 物理约束 | 涵盖原始决策池 |
|---------|-----------|--------|---------------|
| **001** | **显式契约锁定** | Contract 启动即冻结，运行时零反射验证 | CF-001, SD-001, HYDRATE-004, VALIDATE-002, NXC106, ARCH-ISV-001 |
| **002** | **URL 资源寻址** | ProfileId 从 HTTP 路径/Body 显式给定，禁止猜测 | DECOUPLE-002, ARCH-ISV-002, ADAPT-HTTP-001, PIPELINE-003 |
| **003** | **物理槽位隔离** | Provider:ProfileId 唯一寻址，Realm 仅审计 | ISO-003, MT-002, ARCH-ISV-003, CONFIG-MEMORY-002, RESOLVER-005 |
| **004** | **BFF/Gate 职责拆分** | BFF 负责身份->ProfileId 转换，Gate 仅负责执行 | DECOUPLE-001, ADR-010, MT-001, MT-CONTEXT-001, ROADMAP-001 |
| **005** | **热路径脱网自治** | L1 缓存+30天绝对过期，支撑 Redis 完全离线 | CS-002, CS-003, RESOLVER-001, ADR-015, REDIS-001, RESOLVER-004 |
| **006** | **启动期全量体检** | 启动成功 ⟺ 所有 Contract 元数据 FrozenDictionary 可靠 | CF-003, DIAG-002, VALIDATE-001, PIPELINE-002, ROADMAP-002 |
| **007** | **零反射 IL 引擎** | Projection/Hydration 全走编译期 IL，无运行时反射 | ED-003, ED-005, HYDRATE-001, ARCH-ISV-001, HYDRATE-001 |
| **008** | **四阶段原子管道** | Validate → Project → Execute → Hydrate，各阶段独立崩溃 | ED-002, GATEWAY-003, PIPELINE-001, HYDRATE-002, VALIDATE-003 |
| **009** | **Provider 协议主权** | 各 Provider 独立签名算法，框架无权干涉 | SD-002, ADAPTER-001, ADAPTER-ALIPAY-001, ROADMAP-001, ADAPTER-ALIPAY-002 |
| **010** | **Provider 无状态单例** | 同一 Provider 服务所有 ProfileId，配置从管道参数传入 | ADAPTER-001, ARCH-ISV-004, ROADMAP-001, ARCH-ISV-001 |
| **011** | **版本化加密存储** | 私钥 Redis 中 `v1:` 前缀 AES 加密，内存中明文驻留 | SD-003, SEC-AES-001, REDIS-003, RESOLVER-001 |
| **012** | **NXC 结构化诊断** | 每个错误必须在发生阶段立即生成 NXC 码，不允许汇总转译 | DIAG-001, GATEWAY-005, VALIDATE-001, PIPELINE-001, HYDRATE-006 |

---

## 🔥 第二部分：执行"三层分类"——137 项决策的命运

### I. 彻底删除（Prohibited/Deleted）—— 6 项"原罪"逻辑

这些决策代表**架构污染**，必须从代码库和训练文档中物理抹除。

#### ISO-001：500ms 冷启动超时保护
**删除理由：**
- 物理隔离（宪法003）已解决竞争问题
- 此逻辑是**战术妥协**而非架构主权
- `HybridConfigResolver.ColdStartSyncAsync()` 中的超时保护代码应全部删除
- **New Contract：** 新商家必须通过 BFF 预注册，Gate 不允许冷启动失败

**执行步骤：**
```csharp
// ❌ DELETED
private async Task<HashSet<string>> ColdStartSyncAsync(...)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromMilliseconds(500));  // ← DELETE THIS
    
    try
    {
        await mapLock.WaitAsync(cts.Token);
    }
    catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
    {
        // ← DELETE THIS ENTIRE CATCH BLOCK
        throw new TimeoutException(...);
    }
}

// ✅ NEW
private async Task<HashSet<string>> LoadFromRedisAsync(string realmId, string providerName)
{
    // 简单的 Redis 查询，不支持冷启动
    var result = await _redis.SetMembersAsync(BuildMapKey(realmId, providerName));
    if (result.Length == 0)
        throw new NexusTenantException($"Realm {realmId} not found in Redis");
    return new HashSet<string>(result.Select(r => r.ToString()));
}
```

---

#### ADAPT-HTTP-001：多源身份猜测（Header/Body/Query 模糊提取）
**删除理由：**
- **显式约束**优于**隐式猜测**（宪法002）
- TenantContextFactory 中的"魔法"身份提取逻辑必须删除
- 身份只能从 **URL 路径显式给定**

**执行步骤：**
```csharp
// ❌ DELETED
public class TenantContextFactory
{
    public static TenantContext FromHttpContext(HttpContext ctx)
    {
        // 删除这些猜测逻辑：
        // 1. 从 Header["X-SysId"] 读取
        var sysId = ctx.Request.Headers["X-SysId"].ToString();  // ← DELETE
        
        // 2. 从 Body 参数猜测 AppId
        var appIdFromBody = req.AppId ?? req.SubMchId ?? ...;  // ← DELETE
        
        // 3. 从 Query 回退到 Header
        var appId = ctx.Request.Query["app_id"] ?? ctx.Request.Headers["X-AppId"];  // ← DELETE
    }
}

// ✅ NEW
public class TenantContextFactory
{
    /// <summary>
    /// 从 URL 路径显式提取 ProfileId
    /// 例如：POST /merchants/{merchantId}/trade/pay
    /// </summary>
    public static TenantContext FromUrlPath(HttpContext ctx, string profileId, string providerName)
    {
        return new TenantContext
        {
            ProviderName = providerName,  // 从路由元数据读取
            ProfileId = profileId,         // 从路径参数读取
            Metadata = new Dictionary<string, object>()
        };
    }
}
```

**改造 Endpoint：**
```csharp
// OLD: 模糊身份提取
public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
{
    var tenantCtx = TenantContextFactory.FromHttpContext(HttpContext);  // ← 魔法
    var response = await _engine.ExecuteAsync(req, tenantCtx, ct);
}

// NEW: 显式路径参数
public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
{
    var profileId = HttpContext.GetRouteValue("merchantId")?.ToString()
        ?? throw new BadHttpRequestException("Missing merchantId in URL");
    
    var tenantCtx = TenantContextFactory.FromUrlPath(HttpContext, profileId, "Alipay");
    var response = await _engine.ExecuteAsync(req, tenantCtx, ct);
}
```

---

#### MULTIAPP-001：默认 AppId 回退策略
**删除理由：**
- **显式优于隐式**（宪法002）
- 网关不应猜测"哪个 AppId 是默认的"
- 每个请求必须明确指定 ProfileId

**执行步骤：**
```csharp
// ❌ DELETED
public async Task<ITenantIdentity> ResolveDefaultProfileAsync(ITenantIdentity identity)
{
    // 尝试获取默认 ProfileId 标记
    string defaultMarker = $"{mapKey}:default";
    RedisValue defaultProfileId = await _redisDb.StringGetAsync(defaultMarker);
    
    if (defaultProfileId.HasValue)
        return new ConfigurationContext(...) { ProfileId = defaultProfileId.ToString() };
    
    // 如果未设置默认，从 map 中获取第一个 ProfileId
    var allProfileIds = await _redisDb.SetMembersAsync(mapKey);
    var firstProfileId = allProfileIds[0];
    
    return new ConfigurationContext(...) { ProfileId = firstProfileId.ToString() };
}

// ✅ NEW: 删除整个方法，ProfileId 必须显式给定
// ResolveAsync(ITenantIdentity identity) 中，如果 identity.ProfileId 为空，直接抛异常
public async Task<IProviderConfiguration> ResolveAsync(ITenantIdentity identity, ...)
{
    if (string.IsNullOrEmpty(identity.ProfileId))
        throw new NexusTenantException(
            $"ProfileId is required. Realm '{identity.RealmId}' has no default AppId.");
    
    // 直接加载配置
    ...
}
```

---

#### CONFIG-MEMORY-003：文件热更新监控（FileSystemWatcher）
**删除理由：**
- Redis Pub/Sub 已提供**分布式一致性**（宪法005）
- 本地文件监控**引入不确定性**
- 仅在 CI/CD 环节提供配置文件，不在运行时热修改

**执行步骤：**
```csharp
// ❌ DELETED
public class HybridConfigResolver
{
    private readonly FileSystemWatcher _fileWatcher;  // ← DELETE THIS
    
    public HybridConfigResolver(...)
    {
        // 监听配置文件变化
        _fileWatcher = new FileSystemWatcher("/config")  // ← DELETE
        {
            Filter = "*.json",
            NotifyFilter = NotifyFilters.LastWrite
        };
        _fileWatcher.Changed += OnConfigFileChanged;
    }
    
    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // 重新加载配置
        var config = LoadFromFile(e.FullPath);  // ← DELETE
        _redisDb.StringSetAsync(BuildCacheKey(...), JsonSerializer.Serialize(config));
    }
}

// ✅ NEW: 仅支持 Redis Pub/Sub 和 Manual Refresh
public class HybridConfigResolver
{
    // FileSystemWatcher 完全删除
    // 配置变更仅通过：
    // 1. Redis Pub/Sub (自动)
    // 2. TenantConfigurationManager.UpdateAsync() (手动)
    
    public async Task RefreshAsync(ITenantIdentity identity, CancellationToken ct)
    {
        // 清除 L1，等待下次请求从 Redis 回源
        _memoryCache.Remove(BuildCacheKey(identity));
        await PublishRefreshNotificationAsync(identity);
    }
}
```

---

#### HYDRATE-001 L2：反射回填路径（Fallback to Reflection）
**删除理由：**
- **启动期体检**（宪法006）确保所有 Contract 编译期 IL 完全可靠
- 运行时反射回填是**逃生舱口**，必须删除
- 如果 IL 编译失败，启动就应该失败，不允许降级到反射

**执行步骤：**
```csharp
// ❌ DELETED
public class ResponseHydrationEngine
{
    public async Task<TResponse> HydrateAsync<TResponse>(
        Dictionary<string, object> responseDict)
        where TResponse : class, new()
    {
        try
        {
            // L1: 尝试编译期 IL（FastInvoker）
            var hydrator = _il GeneratedHydrators.GetOrAdd(typeof(TResponse), ...);
            return hydrator(responseDict) as TResponse;
        }
        catch
        {
            // ❌ DELETE THIS FALLBACK BLOCK
            // L2: 降级到反射（禁止）
            var instance = new TResponse();
            foreach (var prop in typeof(TResponse).GetProperties())
            {
                if (responseDict.TryGetValue(GetApiFieldName(prop), out var value))
                    prop.SetValue(instance, value);
            }
            return instance;
        }
    }
}

// ✅ NEW: 彻底删除 L2，启动失败即失败
public class ResponseHydrationEngine
{
    public async Task<TResponse> HydrateAsync<TResponse>(
        Dictionary<string, object> responseDict)
        where TResponse : class, new()
    {
        // 启动期已验证所有 Contract 可编译
        // 运行时无需 fallback
        var hydrator = _ilGeneratedHydrators[typeof(TResponse)]
            ?? throw new InvalidOperationException(
                $"Type {typeof(TResponse).Name} not precompiled during startup. " +
                "Run startup health check.");
        
        return hydrator(responseDict) as TResponse 
            ?? throw new InvalidOperationException("Hydration returned null");
    }
}
```

---

#### VER/CV 系列：管理八股（版本路线、项目价值观虚词）
**删除理由：**
- 这些都是**元决策文档**而非**架构约束**
- 仅为了满足"决策记录"的虚荣心
- 删除：VER-001~004, CV-001~003

**具体删除清单：**
- ❌ VER-001 (版本演进策略)
- ❌ VER-002 (Preview → RC → GA 阶段)
- ❌ VER-003 (发布渠道与稳定性保证)
- ❌ VER-004 (长期支持承诺)
- ❌ CV-001 (项目核心价值观)
- ❌ CV-002 (设计哲学)
- ❌ CV-003 (开发者体验愿景)

**保留：** 仅保留 ROADMAP-004（版本进化事实描述）

---

### II. 强力合并（Merged & Hardened）—— 3 项战术合并入宪法

#### 合并 1：配置寻址三元组 → ProfileId 单元寻址
**合并原始决策：**
- CONFIG-MEMORY-001 (L1/L2 结构)
- CONFIG-MEMORY-002 (缓存键设计)
- MULTIAPP-003 (Redis 数据结构)
- RESOLVER-005 (权限索引)

**新主权约束：**
```csharp
// ❌ OLD: 三元组 (Provider, Realm, Profile)
string cacheKey = $"{ctx.ProviderName}:{ctx.RealmId}:{ctx.ProfileId}";
var config = await resolver.ResolveAsync(new ConfigurationContext(
    providerName: "Alipay",
    realmId: "2088...",
    profileId: "2021..."
));

// ✅ NEW: ProfileId 单元寻址（Realm 仅审计）
string cacheKey = $"config:{profileId}";  // O(1) 物理索引
var config = await resolver.ResolveAsync(
    providerName: "Alipay",
    profileId: profileId  // ← 唯一寻址键
);

// Redis 数据结构简化：
// BEFORE: Hash (nxc:map:realm:provider) 存储权限索引
// AFTER: 直接使用 profileId 作为 Key，无需权限索引层
//        权限管理由 BFF 负责（身份 → ProfileId 转换）
```

**物理约束：**
- Realm 仅保留在**审计日志**中（"该请求来自 Realm X"）
- 寻址逻辑不依赖 Realm（即使丢失也能继续运行）
- Redis Key 空间：`config:{provider}:{profileId}`

---

#### 合并 2：安全主权统一化（加密算法 + 存储策略）
**合并原始决策：**
- SEC-AES-001 (AES256 加密)
- REDIS-003 (加密存储)
- RESOLVER-001 (L1/L2 缓存)

**新主权约束：**
```csharp
// 宪法 011：版本化加密存储

public interface ISecurityProvider
{
    /// <summary>
    /// 私钥加密（写入 Redis）
    /// - 算法：AES256-CBC
    /// - IV：每次随机生成
    /// - 版本前缀：v1: 支持未来升级
    /// 返回：v1:base64_encrypted
    /// </summary>
    string EncryptPrivateKey(string plaintext);
    
    /// <summary>
    /// 私钥解密（从 Redis 读取）
    /// - 自动识别版本前缀（v1:, v2: ...)
    /// - 返回：纯文本 PEM
    /// </summary>
    string DecryptPrivateKey(string encrypted);
}

// 执行策略：
// 1. Redis 中：PrivateKey = "v1:aGVs..." (AES加密)
// 2. 内存中：ProviderSettings.PrivateKey = "MIIEvQ..." (明文)
// 3. 传输中：[无] (不跨网络)

public class ProviderSettings
{
    public string ProviderName { get; set; }
    public string AppId { get; set; }
    public string PrivateKey { get; set; }  // 内存中始终明文
    public string PublicKey { get; set; }
}
```

**物理约束：**
- 私钥**绝不允许**写入日志/缓存键/诊断消息
- 加密密钥由环境变量 `SECURITY_MASTER_KEY` 提供
- 密钥轮换通过版本前缀（v1: → v2:）实现，无需应用重启

---

#### 合并 3：启动体检 + NXC 诊断码统一化
**合并原始决策：**
- VALIDATE-001 (Contract 验证)
- VALIDATE-002 (NXC 错误码设计)
- PIPELINE-002 (启动健康检查)
- DIAG-001/DIAG-002 (诊断系统)

**新主权约束：**
```csharp
// 宪法 006 + 012 的统一执行

public interface INexusContractMetadataRegistry
{
    /// <summary>
    /// 启动期预热：扫描所有 Contract，编译 IL，生成诊断报告
    /// 启动成功 ⟺ 所有 Contract 元数据 FrozenDictionary 可靠
    /// </summary>
    DiagnosticReport Preload(Type[] contractTypes, bool warmup = true);
}

public class DiagnosticReport
{
    public bool HasCriticalErrors { get; set; }
    
    // NXC 错误码清单
    public List<DiagnosticEntry> Entries { get; set; }
}

public class DiagnosticEntry
{
    public string NxcCode { get; set; }        // NXC101, NXC102 ...
    public string Message { get; set; }
    public int LineNumber { get; set; }
    public Type ContractType { get; set; }
}

// NXC 码范围分配：
// NXC1xx: Contract 验证错误（启动期）
// NXC2xx: 配置错误（运行时）
// NXC3xx: 传输错误（Execute 阶段）
// NXC4xx: 反序列化错误（Hydrate 阶段）
// NXC5xx: 签名错误（Provider 层）
// NXC99x: 框架内部错误

// 执行逻辑：每个阶段的错误必须在发生时立即生成 NXC 码
public async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
    TRequest request, TenantContext context, CancellationToken ct)
    where TRequest : IApiRequest<TResponse>
{
    try
    {
        // Phase 1: Validate
        var metadata = _registry.GetMetadata(typeof(TRequest));
        var validationResult = _validator.Validate(request, metadata);
        if (!validationResult.IsValid)
        {
            // 立即生成 NXC101（Contract 验证失败）
            throw new ContractValidationException("NXC101", validationResult.Errors);
        }
        
        // Phase 2: Project
        var dictionary = _projector.Project(request);
        // 如果 Project 失败，立即生成 NXC102
        
        // Phase 3: Execute
        var httpRequest = _signer.SignRequest(dictionary);
        var responseDict = await _transport.SendAsync(httpRequest, ct);
        // 如果 Execute 失败，立即生成 NXC3xx
        
        // Phase 4: Hydrate
        var response = _hydrator.Hydrate<TResponse>(responseDict);
        // 如果 Hydrate 失败，立即生成 NXC4xx
        
        return response;
    }
    catch (NexusException ex)
    {
        // NXC 码异常直接抛出，不允许重新包装或汇总
        throw;
    }
}
```

---

### III. 降权为细节（Downgraded to Implementation）—— 4 项

这些不再作为架构决策，而是编码指南或实现细节：

| 原始决策 | 新身份 | 理由 |
|---------|--------|------|
| GATEWAY-002 (ConfigureAwait) | .NET 开发规范 | 属于异步编程常识，无需 ADR |
| RESOLVER-002 (SemaphoreSlim) | 并发编程细节 | 属于 HybridConfigResolver 的内部实现，不影响主权 |
| CS-001 (L1/L2 结构) | 存储方案必然 | 是 Redis-First 的自然结果，不应单独决策 |
| MT-CONTEXT-003 (流式 API) | 语法糖 | `ITenantIdentity` 的构建方式，不影响系统走向 |

---

## 📐 第三部分：物理重构蓝图——12 条宪法的执行细则

### 宪法 001：显式契约锁定

**物理约束：**
```csharp
// Contract 定义必须包含完整的元数据
[NexusContract(Method = "alipay.trade.create")]
[ApiOperation("trade/create", HttpVerb.POST)]
public class TradeCreateRequest : IApiRequest<TradeCreateResponse>
{
    [ApiField("out_trade_no", IsRequired = true)]
    [Encrypt]  // 标记需要加密的字段
    public string OutTradeNo { get; set; }
}

// 启动期检查：所有 Contract 都能编译成 FrozenDictionary
// 运行时：无反射，所有访问都走编译期 IL
```

---

### 宪法 002：URL 资源寻址

**物理约束：**
```
POST /merchants/{merchantId}/trade/pay

路由参数严格映射：
- {merchantId} → profileId（唯一标识）
- API 操作 → 从 Contract 元数据读取

禁止：
- ❌ 从 Body 猜测身份
- ❌ 从 Header 默认补全
- ❌ Query 参数回退
```

**Endpoint 标准模板：**
```csharp
public sealed class TradePayEndpoint : NexusEndpoint<TradePayRequest>
{
    public override void Configure()
    {
        // 路由显式定义 ProfileId 位置
        Post("/merchants/{merchantId:guid}/trade/pay");
    }
    
    public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
    {
        // 从路由参数显式提取
        var merchantId = Route<Guid>("merchantId");
        
        // 转化为 ProfileId（可以是 GUID 的 Base36 编码）
        var profileId = merchantId.ToString("N");
        
        var response = await _engine.ExecuteAsync(req, "Alipay", profileId, ct);
        await SendAsync(response);
    }
}
```

---

### 宪法 003：物理槽位隔离

**物理约束：**
```csharp
// Redis Key 设计：
// Provider:ProfileId → 唯一物理槽位

string cacheKey = $"config:{providerName}:{profileId}";

// Realm 降级为纯审计信息（可选）
// 不参与寻址逻辑

public interface IConfigurationResolver
{
    Task<IProviderConfiguration> ResolveAsync(
        string providerName,
        string profileId,
        CancellationToken ct);
    
    // Realm 仅在审计接口中使用
    Task AuditAccessAsync(string realm, string profileId, string action);
}
```

---

### 宪法 004：BFF/Gate 职责拆分

**物理约束：**
```
BFF 责任（身份转换）：
  用户信息 + 业务参数 → ProfileId（确定性转换）
  
Gate 责任（执行）：
  ProfileId + Contract → 签名 + HTTP 调用 + 响应回填
  
分界线：
  BFF → HTTP.Body:profileId + Contract → Gate
  Gate → Contract.Execute() → HTTP.Response
```

**实现示例：**
```csharp
// BFF 层
public class MerchantBizService
{
    public async Task<TradePayResponse> PayAsync(Guid merchantId, PaymentDto dto)
    {
        // 1. 身份转换：merchantId → profileId
        var profileId = merchantId.ToString("N");
        
        // 2. 业务转换：PaymentDto → TradePayRequest
        var request = new TradePayRequest
        {
            OutTradeNo = dto.OrderId,
            TotalAmount = dto.Amount,
            ProfileId = profileId  // ← 显式传递
        };
        
        // 3. 调用 Gate API
        var gateClient = new NexusGatewayClient(...);
        return await gateClient.SendAsync(request);
    }
}

// Gate 层
public sealed class TradePayEndpoint : NexusEndpoint<TradePayRequest>
{
    public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
    {
        // Gate 只关心：ProfileId + Contract 的执行
        var response = await _engine.ExecuteAsync(req, "Alipay", req.ProfileId, ct);
        await SendAsync(response);
    }
}
```

---

### 宪法 005：热路径脱网自治

**物理约束：**
```csharp
// L1 缓存策略（极端情况：Redis 离线 30 天）
_memoryCache.Set(cacheKey, config, new MemoryCacheEntryOptions
{
    SlidingExpiration = TimeSpan.FromHours(24),           // 热数据
    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),  // 兜底
    Priority = CacheItemPriority.NeverRemove             // 禁止驱逐
});

// 业务含义：
// - 只要流量不停，缓存永远有效
// - 没有流量超过 24 小时，缓存自动过期
// - Redis 故障时，系统可继续运行 30 天（不是"降级"，是标准行为）
```

---

### 宪法 006：启动期全量体检

**物理约束：**
```csharp
// Program.cs：启动即刻检查
var contractTypes = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => a.GetTypes())
    .Where(t => t.GetCustomAttribute<ApiOperationAttribute>() != null)
    .ToArray();

var report = NexusContractMetadataRegistry.Instance.Preload(contractTypes, warmup: true);

if (report.HasCriticalErrors)
{
    Console.WriteLine("❌ Startup failed - Contract errors detected");
    report.PrintDiagnostics();
    Environment.Exit(1);
}

// 启动成功 ⟺ 宪法 007 的所有 IL 编译完成且可靠
```

---

### 宪法 007：零反射 IL 引擎

**物理约束：**
```csharp
// Projection 和 Hydration 都是编译期 IL
// 运行时只有方法调用，零反射

public class ILCompiledProjector
{
    // 每个 Contract 编译一次
    private readonly Func<TRequest, Dictionary<string, object>> _compiledProjector;
    
    public Dictionary<string, object> Project(TRequest request)
    {
        // 纯方法调用，无反射
        return _compiledProjector(request);
    }
}

public class ILCompiledHydrator
{
    private readonly Func<Dictionary<string, object>, TResponse> _compiledHydrator;
    
    public TResponse Hydrate(Dictionary<string, object> dict)
    {
        // 纯方法调用，无反射
        return _compiledHydrator(dict);
    }
}
```

---

### 宪法 008：四阶段原子管道

**物理约束：**
```csharp
public async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
    TRequest request,
    string providerName,
    string profileId,
    CancellationToken ct)
    where TRequest : IApiRequest<TResponse>
{
    // Phase 1: Validate（启动期已完成，运行时验证业务逻辑）
    if (string.IsNullOrEmpty(request.OutTradeNo))
        throw new ContractValidationException("NXC101", "OutTradeNo required");
    
    // Phase 2: Project
    var dictionary = _projector.Project(request);
    
    // Phase 3: Execute
    var config = await _resolver.ResolveAsync(providerName, profileId, ct);
    var httpRequest = _signer.SignRequest(dictionary, config);
    var responseDict = await _transport.SendAsync(httpRequest, ct);
    
    // Phase 4: Hydrate
    var response = _hydrator.Hydrate<TResponse>(responseDict);
    
    return response;
}

// 关键约束：
// - 各阶段独立崩溃（不级联）
// - 错误立即生成 NXC 码
// - 不允许阶段间状态共享
```

---

### 宪法 009 & 010：Provider 协议主权与无状态单例

**物理约束：**
```csharp
public interface IProvider
{
    string ProviderName { get; }
    
    /// <summary>
    /// 统一执行接口：配置从参数传入（无状态）
    /// </summary>
    Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration config,
        CancellationToken ct)
        where TResponse : class, new();
}

// AlipayProvider 是单例，服务所有 ProfileId
public class AlipayProvider : IProvider
{
    private readonly INexusTransport _transport;
    private readonly ISigningService _signer;
    
    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration config,
        CancellationToken ct)
    {
        // 配置从参数读取，不从实例字段读取
        var signature = _signer.Sign(request, config.PrivateKey);
        var response = await _transport.SendAsync(...);
        return response;
    }
}

// 注册为单例（而非 Transient）
builder.Services.AddSingleton<IProvider>(new AlipayProvider(...));

// 多 Provider 注册
engine.RegisterProvider("Alipay", alipayProvider);
engine.RegisterProvider("WeChat", wechatProvider);
engine.RegisterProvider("UnionPay", unionPayProvider);
```

---

### 宪法 011：版本化加密存储

**物理约束：**
```csharp
// 私钥加密格式：v1:base64_encrypted
// 版本前缀支持未来升级（v2:, v3: ...）

public class AesSecurityProvider : ISecurityProvider
{
    private readonly byte[] _masterKey;  // 环境变量 SECURITY_MASTER_KEY
    
    public string EncryptPrivateKey(string plaintext)
    {
        using (var aes = Aes.Create())
        {
            aes.Key = _masterKey;
            aes.GenerateIV();
            
            var encrypted = Encrypt(plaintext, aes);
            var combined = aes.IV.Concat(encrypted).ToArray();
            var base64 = Convert.ToBase64String(combined);
            
            return $"v1:{base64}";  // ← 版本前缀
        }
    }
    
    public string DecryptPrivateKey(string encrypted)
    {
        var parts = encrypted.Split(':');
        var version = parts[0];  // v1, v2, ...
        var base64 = parts[1];
        
        // 根据版本选择算法
        if (version == "v1")
            return DecryptV1(base64);
        else if (version == "v2")
            return DecryptV2(base64);
        else
            throw new InvalidOperationException($"Unsupported version: {version}");
    }
}

// 存储在 Redis：
// config:Alipay:merchant-001 = {
//   "ProviderName": "Alipay",
//   "AppId": "2021...",
//   "PrivateKey": "v1:aGVsbG8gd29ybGQ=...",  // ← 加密存储
//   "PublicKey": "MIIBIj...",
//   "GatewayUrl": "https://openapi.alipay.com/"
// }
```

---

### 宪法 012：NXC 结构化诊断

**物理约束：**
```csharp
// NXC 码的完整生命周期

public class ContractValidationException : NexusException
{
    public string NxcCode { get; }
    public DiagnosticData DiagnosticData { get; }
    
    public ContractValidationException(string nxcCode, object diagnosticData)
    {
        NxcCode = nxcCode;  // NXC101, NXC102, ...
        DiagnosticData = new DiagnosticData
        {
            Code = nxcCode,
            Timestamp = DateTime.UtcNow,
            Phase = "Validate",
            Details = diagnosticData
        };
    }
}

// 异常在发生阶段立即生成 NXC 码
try
{
    // Phase 1: Validate
    validator.Validate(request);
}
catch (ArgumentException ex)
{
    throw new ContractValidationException("NXC101", new { Field = "OutTradeNo", Error = ex.Message });
}

try
{
    // Phase 2: Project
    dictionary = projector.Project(request);
}
catch (InvalidCastException ex)
{
    throw new ProjectionException("NXC102", new { Type = typeof(TradePayRequest).Name, Error = ex.Message });
}

// HTTP 响应不处理异常，由 FastEndpoints 全局异常处理器负责
// 全局处理器读取 NxcCode 并生成标准的 NxcErrorEnvelope

public class NexusErrorHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        var nxcException = exception as NexusException 
            ?? throw new InvalidOperationException("Unknown exception type");
        
        context.Response.StatusCode = nxcException.HttpStatusCode;
        
        var envelope = new NxcErrorEnvelope
        {
            Code = nxcException.NxcCode,
            Message = nxcException.Message,
            DiagnosticData = nxcException.DiagnosticData,
            Timestamp = DateTime.UtcNow
        };
        
        await context.Response.WriteAsJsonAsync(envelope, cancellationToken: ct);
        return true;
    }
}
```

---

## 🎯 第四部分：137 → 12 的决策归属表

### 保留的 12 条宪法决策
✅ ARCH-ISV-001, ARCH-ISV-002, ARCH-ISV-003, ARCH-ISV-004  
✅ RESOLVER-001, RESOLVER-003  
✅ PIPELINE-001, PIPELINE-002  
✅ VALIDATE-001  
✅ DIAG-001  
✅ ROADMAP-001

### 删除的 6 项（物理抹除）
❌ ISO-001 (500ms 超时)  
❌ ADAPT-HTTP-001 (多源身份猜测)  
❌ MULTIAPP-001 (默认 AppId 回退)  
❌ CONFIG-MEMORY-003 (文件监控)  
❌ HYDRATE-001 L2 (反射降级)  
❌ VER/CV 系列 (管理八股)

### 合并入宪法的 25 项
**宪法 001（显式契约锁定）：** CF-001, SD-001, HYDRATE-004, VALIDATE-002, NXC106  
**宪法 002（URL 寻址）：** DECOUPLE-002, ADAPT-HTTP-001, PIPELINE-003  
**宪法 003（物理隔离）：** ISO-003, MT-002, CONFIG-MEMORY-002, RESOLVER-005  
**宪法 004（职责拆分）：** DECOUPLE-001, ADR-010, MT-001, MT-CONTEXT-001, ROADMAP-001  
**宪法 005（脱网自治）：** CS-002, CS-003, ADR-015, REDIS-001, RESOLVER-004  
**宪法 006（启动体检）：** CF-003, DIAG-002, ROADMAP-002  
**宪法 011（加密存储）：** SD-003, SEC-AES-001, REDIS-003  
**宪法 012（诊断码）：** GATEWAY-005

### 降权为细节的 4 项
🔷 GATEWAY-002 (ConfigureAwait) → .NET 开发规范  
🔷 RESOLVER-002 (SemaphoreSlim) → 并发编程细节  
🔷 CS-001 (L1/L2) → 存储方案必然  
🔷 MT-CONTEXT-003 (流式 API) → 语法糖

### 未分类的 82 项（待清理）
这些决策要么是**重复决策**，要么是**某条宪法的具体实例**，需要逐一归并。

---

## 📋 第五部分：8 周工程执行计划

### Week 1-2：基础设施清理
- [ ] 从代码库删除 ISO-001, ADAPT-HTTP-001, MULTIAPP-001 相关代码
- [ ] 从代码库删除 CONFIG-MEMORY-003 (FileSystemWatcher)
- [ ] 从代码库删除 HYDRATE-001 L2 (反射回填路径)
- [ ] 从文档中删除 VER/CV 系列

### Week 3-4：配置寻址重构
- [ ] 改造 `IConfigurationResolver` 接口（ProfileId 单元化）
- [ ] 改造 `TenantContextFactory`（仅支持 URL 显式提取）
- [ ] 改造 Redis Key 设计（三元组 → ProfileId）
- [ ] 更新所有 Endpoint（显式路由参数）

### Week 5-6：宪法执行
- [ ] 实现宪法 001-005（Contract 锁定、URL 寻址、隔离、职责拆分、脱网）
- [ ] 实现宪法 006-008（启动体检、零反射、四阶段）
- [ ] 实现宪法 009-010（Provider 主权、无状态）

### Week 7：安全与诊断
- [ ] 实现宪法 011（版本化加密存储）
- [ ] 实现宪法 012（NXC 结构化诊断）
- [ ] 统一异常处理（NXC 码生成）

### Week 8：验证与清理
- [ ] 更新所有单元测试（删除的逻辑）
- [ ] 删除 137→12 过程中的中间决策文档
- [ ] 生成"月月红"宪法最终确认版本

---

## 🏁 最终产出

**删除后的清洁代码库：**
- ✅ 零反射，零猜测，零降级
- ✅ 显式优于隐式
- ✅ 12 条宪法支撑所有功能

**最终文档：**
- `CONSTITUTIONAL_FRAMEWORK.md` - 12 条宪法的物理约束
- `ARCHITECTURE_CLEAN.md` - 删除污染逻辑后的架构蓝图
- `ROADMAP_FINAL.md` - 后续功能扩展的唯一参考

---

**执行许可：** 开始 Week 1 基础设施清理  
**预期完成：** 2026-02-28  
**验收标准：** 所有决策都能追溯到 12 条宪法之一

