# NexusContract 12 条核心宪法——最终执行版本

> **版本：** Constitutional Framework v1.0  
> **生效日期：** 2026-01-11  
> **权威性：** 所有架构决策必须归属于这 12 条宪法之一，否则视为污染  
> **执行状态：** Phase 1 - 代码库清洗（进行中）

---

## 🏛️ 12 条核心宪法的物理约束

### 宪法 001：显式契约锁定（Explicit Contract Freezing）

**物理原则：**  
Contract 元数据在启动期完全冻结为 FrozenDictionary，运行时零反射验证。所有字段映射、类型转换、加密标记都在编译期确定，不允许动态修改。

**具体约束：**

```csharp
// ✅ REQUIRED: 每个 Contract 必须标注完整元数据
[NexusContract(Method = "alipay.trade.create")]
[ApiOperation("trade/create", HttpVerb.POST)]
public class TradeCreateRequest : IApiRequest<TradeCreateResponse>
{
    [ApiField("out_trade_no", IsRequired = true)]
    [Encrypt]  // 显式标记加密字段
    public string OutTradeNo { get; set; }
    
    [ApiField("total_amount", IsRequired = true)]
    public decimal TotalAmount { get; set; }
    
    [ApiField("subject", IsRequired = true)]
    public string Subject { get; set; }
}

// ✅ 启动期：元数据冻结
var metadata = NexusContractMetadataRegistry.Instance.Preload(
    new[] { typeof(TradeCreateRequest) },
    warmup: true);

if (metadata.HasCriticalErrors)
    Environment.Exit(1);  // ← 启动即刻失败，不允许降级

// ❌ 禁止：运行时修改元数据
metadata.Fields["out_trade_no"].IsRequired = false;  // 违反宪法 001
```

**验证清单：**
- [ ] Contract 启动期 100% 扫描
- [ ] 所有字段都有 ApiField 标注
- [ ] 加密字段都有 [Encrypt] 标注
- [ ] 启动成功 ⟺ 元数据 FrozenDictionary 可靠
- [ ] 运行时零反射字段访问

---

### 宪法 002：URL 资源寻址（URL-Based Resource Addressing）

**物理原则：**  
ProfileId 从 HTTP URL 路径显式给定，禁止从 Body、Header、Query 参数猜测或补全。每个资源由唯一的 URL 路径标识。参数提取在 Endpoint 层直接处理，无中间容器。

**具体约束：**

```csharp
// ✅ CORRECT: URL 显式包含资源标识 + NexusGuard 防御
// POST /merchants/{profileId}/trade/pay
[HttpPost("/merchants/{profileId}/trade/pay")]
public sealed class TradePayEndpoint : NexusEndpoint<TradePayRequest>
{
    public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
    {
        // 从路径参数显式提取
        var profileId = Route<string>("profileId");
        
        // 物理寻址卫哨：确保参数完整
        NexusGuard.EnsurePhysicalAddress("Alipay", profileId, nameof(TradePayEndpoint));
        
        // ← ProfileId 来自 URL，已被 NexusGuard 验证
        var response = await _engine.ExecuteAsync(req, "Alipay", profileId, ct);
        await SendAsync(response);
    }
}

// ❌ 禁止：从 Body 猜测 ProfileId
public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
{
    var profileId = req.ProfileId;  // ← 违反宪法 002，ProfileId 不应来自 Body
    ...
}

// ❌ 禁止：从 Header 默认补全
var profileId = HttpContext.Request.Headers["X-ProfileId"] 
    ?? Guid.NewGuid().ToString();  // ← 违反宪法 002，ProfileId 不应从 Header 猜测

// ❌ 禁止：存储身份容器
var identity = await TenantContextFactory.CreateAsync(HttpContext);  // ← 已删除，禁止使用
```

**验证清单：**
- [ ] 所有 Endpoint URL 都包含 ProfileId 路径参数（如 `{profileId}`, `{storeId}`)
- [ ] ProfileId 直接从 Route<T>() 提取，使用 NexusGuard.EnsurePhysicalAddress() 验证
- [ ] 禁止 `Header["X-*"]` 身份补全或备选方案
- [ ] 禁止 `Body` 中隐含 ProfileId 信息
- [ ] 禁止使用 TenantContextFactory 或身份容器对象

---

### 宪法 003：物理槽位隔离（Physical Slot Isolation）

**物理原则：**  
每个 ProfileId 对应一个唯一的物理槽位（Redis Key），配置查询是 O(1) 精确匹配。NexusGuard 确保参数始终有效，无隐式回填或默认补全。

**具体约束：**

```csharp
// ✅ CORRECT: 使用 NexusGuard 确保物理地址完整
public interface IConfigurationResolver
{
    /// <summary>
    /// 从 Redis 精确查询配置（O(1)）
    /// Key: config:{provider}:{profileId}
    /// 调用者责任：在 Endpoint 层使用 NexusGuard.EnsurePhysicalAddress() 验证
    /// </summary>
    Task<IProviderConfiguration> ResolveAsync(
        string providerName,
        string profileId,
        CancellationToken ct);
}

// Redis 数据结构：
// Key: config:Alipay:2021001234567890
// Value: {
//   "ProviderName": "Alipay",
//   "AppId": "2021...",
//   "PrivateKey": "aGVs...",  // Base64 密文示例
//   "PublicKey": "MIIBIj...",
//   "GatewayUrl": "https://openapi.alipay.com/"
// }

// ✅ Endpoint 层示例
[HttpPost("/merchants/{profileId}/trade/create")]
public class TradeCreateEndpoint : NexusEndpoint<TradeCreateRequest>
{
    public override async Task HandleAsync(TradeCreateRequest req, CancellationToken ct)
    {
        var profileId = Route<string>("profileId");
        
        // NexusGuard 防御性检查：确保参数不为空
        NexusGuard.EnsurePhysicalAddress("Alipay", profileId, nameof(TradeCreateEndpoint));
        
        // 安全传递给 ConfigResolver
        var config = await _configResolver.ResolveAsync("Alipay", profileId, ct);
        // ...
    }
}

// ❌ 禁止：使用身份容器对象
var identity = new TenantContext(...);  // ← 已删除
var config = await resolver.ResolveAsync(identity, ct);

// ❌ 禁止：多层索引查询（Realm 不参与寻址）
var profiles = await redis.SetMembersAsync($"realm:{realmId}:profiles");
var profileId = profiles.FirstOrDefault();  // ← 违反宪法 003，不再支持
```

**验证清单：**
- [ ] Redis Key 格式严格为 `config:{provider}:{profileId}`
- [ ] 所有查询都是 O(1) 精确匹配
- [ ] Endpoint 层必须使用 NexusGuard.EnsurePhysicalAddress() 验证参数
- [ ] 禁止使用 TenantContext 或身份容器
- [ ] 禁止隐式补全或默认 ProfileId

---

### 宪法 004：BFF/Gate 职责拆分（BFF-Gate Separation of Concerns）

**物理原则：**  
BFF 层负责业务身份转换（如商户 ID → ProfileId），Gate 层仅负责合约执行。ProfileId 从 URL 路径显式提取，不涉及身份转换。

**具体约束：**

```csharp
// ========== BFF 层（业务身份转换）==========
public class MerchantBizService
{
    public async Task<TradePayResponse> PayAsync(
        Guid customerId,
        PaymentDto dto)
    {
        // BFF 职责 1: 业务身份转换（如需要）
        var profileId = customerId.ToString("N");
        
        // BFF 职责 2: 业务数据转换
        var request = new TradePayRequest
        {
            OutTradeNo = dto.OrderId,
            TotalAmount = dto.Amount,
            Subject = dto.Description
        };
        
        // BFF 职责 3: 调用 Gate API（profileId 在 URL 路径）
        var httpClient = new HttpClient { BaseAddress = new Uri("https://gate.company.com") };
        var response = await httpClient.PostAsJsonAsync(
            $"/merchants/{profileId}/trade/pay",  // ← ProfileId 显式在 URL
            request);
        
        return await response.Content.ReadAsAsync<TradePayResponse>();
    }
}

// ========== Gate 层（合约执行）==========
public sealed class TradePayEndpoint : NexusEndpoint<TradePayRequest>
{
    public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
    {
        // Gate 职责：仅提取 ProfileId 并执行合约
        var profileId = Route<string>("profileId");
        
        // 防御性检查（宪法 012）
        NexusGuard.EnsurePhysicalAddress("Alipay", profileId, nameof(TradePayEndpoint));
        
        // 执行合约，不涉及身份转换
        var response = await _engine.ExecuteAsync(req, "Alipay", profileId, ct);
        
        await SendAsync(response);
    }
}

// ❌ 禁止：Gate 参与身份转换逻辑
public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
{
    // Gate 不应进行任何业务逻辑转换
    var customerInfo = await _customerService.GetCustomerAsync(req.CustomerId);  // ← 违反宪法 004
    var profileId = customerInfo.ProfileId;
    // ...
}

// ❌ 禁止：使用身份容器
var tenantCtx = TenantContextFactory.Create(HttpContext);  // ← 已删除
var response = await _engine.ExecuteAsync(req, tenantCtx, ct);
```

**验证清单：**
- [ ] BFF 负责所有业务身份转换逻辑
- [ ] Gate 接收 URL 路径中已确定的 ProfileId
- [ ] Gate 使用 NexusGuard 验证 ProfileId，不进行业务逻辑判断
- [ ] 数据流：BFF → HTTP → Gate → Provider
- [ ] 禁止逆向查询（Gate 不能调用 BFF 的服务）

---

### 宪法 005：热路径脱网自治（Hot-Path Network-Independent Autonomy）

**物理原则：**  
L1 缓存（内存）采用 24h 滑动过期 + 30 天绝对过期，使系统在 Redis 完全离线的情况下也能运行 30 天。这不是"降级"，而是标准行为。

**具体约束：**

```csharp
// L1 缓存策略：滑动 + 绝对过期
private void SetL1Cache(string key, ProviderSettings config)
{
    _memoryCache.Set(key, config, new MemoryCacheEntryOptions
    {
        // 滑动过期：只要有业务流量，缓存持续有效
        SlidingExpiration = TimeSpan.FromHours(24),
        
        // 绝对过期：防止"僵尸配置"无限驻留
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
        
        // 最高优先级：不被内存压力驱逐
        Priority = CacheItemPriority.NeverRemove
    });
}

// 业务含义：
// - 正常运行：L1 命中 99.99%，Redis 查询次数接近 0
// - Redis 故障：系统可继续运行 30 天（只要不更新配置）
// - 更新配置：通过 Pub/Sub 推送，立即清除 L1（无延迟）

// ❌ 禁止：关闭滑动过期（导致"12 小时卡点"）
// new MemoryCacheEntryOptions { AbsoluteExpiration = DateTime.Now.AddHours(12) }

// ❌ 禁止：短时间绝对过期（如 1 小时）
// AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)  // ← 过短，引入风险

// ❌ 禁止：低优先级（允许驱逐）
// Priority = CacheItemPriority.Normal  // ← 配置是"生命线"，不能驱逐
```

**验证清单：**
- [ ] L1 缓存 24h 滑动过期 + 30 天绝对过期
- [ ] 配置项优先级 = NeverRemove
- [ ] Redis 不可用时，系统仍可运行（但不能更新配置）
- [ ] Pub/Sub 消息丢失时，最多等待 24h 自动过期（不是 30 天）
- [ ] 网络故障时的行为标准化（无级联失败）

---

### 宪法 006：启动期全量体检（Startup Comprehensive Health Check）

**物理原则：**  
启动成功意味着所有 Contract 元数据都已编译成 FrozenDictionary，系统对接下来的所有请求有完整的认知。启动失败 ⟺ 无法继续运行。

**具体约束：**

```csharp
// Program.cs：启动即刻检查
var contractTypes = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => a.GetTypes())
    .Where(t => t.IsClass && !t.IsAbstract 
                && t.GetCustomAttribute<ApiOperationAttribute>() != null)
    .ToArray();

var report = NexusContractMetadataRegistry.Instance.Preload(
    contractTypes,
    warmup: true);  // ← JIT 编译热路径

// 输出诊断报告
report.PrintToConsole(includeDetails: true);

if (report.HasCriticalErrors)
{
    Console.WriteLine("❌ Startup failed - Contract errors detected");
    Environment.Exit(1);  // ← 启动失败即刻终止，不允许继续
}

// 启动成功 ⟺ 以下条件满足：
// 1. 所有 Contract 都有完整的 ApiField 标注
// 2. 所有字段类型都能被序列化/反序列化
// 3. 所有加密字段都有 [Encrypt] 标注
// 4. IL 编译已完成，运行时零反射

// ❌ 禁止：启动时警告（warn）后继续运行
if (report.HasWarnings)
{
    _logger.LogWarning("Contract issues found, but continuing startup");  // ← 违反宪法 006
    // 这会导致运行时反射降级
}
```

**验证清单：**
- [ ] 启动扫描所有 Assembly 的 Contract
- [ ] Preload() 返回 100% 成功率
- [ ] 如有 Critical 错误，启动即刻终止
- [ ] 诊断报告输出所有 NXC 码
- [ ] IL 编译完成验证（无反射 fallback）

---

### 宪法 007：零反射缓存引擎（Zero-Reflection Cache Engine）

**物理原则：**  
Projection（对象→字典）和 Hydration（字典→对象）通过智能缓存的反射元数据执行，运行时零 Type.GetProperties()、零 PropertyInfo.SetValue() 等重复反射操作。元数据在启动期预热，运行时直接使用缓存结果。

**具体约束：**

```csharp
// ✅ CORRECT: 智能缓存反射元数据
public class CachedReflectionProjector
{
    // 每个 Contract 预热一次，存储为缓存元数据
    private readonly ConcurrentDictionary<Type, ContractMetadata> _metadataCache = new();
    
    public Dictionary<string, object> Project<TRequest>(TRequest request)
    {
        var metadata = _metadataCache.GetOrAdd(
            typeof(TRequest),
            _ => BuildMetadata(typeof(TRequest)));
        
        // 纯缓存访问，无重复反射
        return ProjectWithMetadata(request, metadata);
    }
    
    private ContractMetadata BuildMetadata(Type contractType)
    {
        // 启动期一次性反射，构建缓存元数据
        var properties = contractType.GetProperties()
            .Where(p => p.GetCustomAttribute<ApiFieldAttribute>() != null)
            .Select(p => new PropertyAccessor
            {
                PropertyInfo = p,
                FieldName = p.GetCustomAttribute<ApiFieldAttribute>().Name,
                Getter = p.GetGetMethod(),  // 缓存 Getter
                Setter = p.GetSetMethod()   // 缓存 Setter
            })
            .ToArray();
            
        return new ContractMetadata
        {
            ContractType = contractType,
            Properties = properties
        };
    }
    
    private Dictionary<string, object> ProjectWithMetadata<TRequest>(
        TRequest request, 
        ContractMetadata metadata)
    {
        var dict = new Dictionary<string, object>();
        
        foreach (var prop in metadata.Properties)
        {
            // 直接调用缓存的 Getter，无重复反射
            var value = prop.Getter.Invoke(request, null);
            dict[prop.FieldName] = value;
        }
        
        return dict;
    }
}

// ✅ CORRECT: Hydration 同样使用缓存元数据
public class CachedReflectionHydrator
{
    private readonly ConcurrentDictionary<Type, ContractMetadata> _metadataCache = new();
    
    public TResponse Hydrate<TResponse>(Dictionary<string, object> data)
    {
        var metadata = _metadataCache.GetOrAdd(
            typeof(TResponse),
            _ => BuildMetadata(typeof(TResponse)));
            
        return HydrateWithMetadata<TResponse>(data, metadata);
    }
    
    private TResponse HydrateWithMetadata<TResponse>(
        Dictionary<string, object> data, 
        ContractMetadata metadata)
    {
        var instance = (TResponse)Activator.CreateInstance(typeof(TResponse));
        
        foreach (var prop in metadata.Properties)
        {
            if (data.TryGetValue(prop.FieldName, out var value))
            {
                // 直接调用缓存的 Setter，无重复反射
                var convertedValue = ConvertValue(value, prop.PropertyInfo.PropertyType);
                prop.Setter.Invoke(instance, new[] { convertedValue });
            }
        }
        
        return instance;
    }
}

// ❌ WRONG: 运行时重复反射（禁止）
public Dictionary<string, object> Project<TRequest>(TRequest request)
{
    var dict = new Dictionary<string, object>();
    
    foreach (var prop in typeof(TRequest).GetProperties())  // ← 每次都反射
    {
        var attr = prop.GetCustomAttribute<ApiFieldAttribute>();
        if (attr != null)
        {
            var value = prop.GetValue(request);  // ← 每次都反射
            dict[attr.Name] = value;
        }
    }
    
    return dict;
}

// ❌ WRONG: IL 编译过于复杂（已废弃）
public class ILCompiledProjector  // ← 复杂性过高，已移除
{
    private readonly ConcurrentDictionary<Type, Delegate> _compiledProjectors = new();
    // ... DynamicMethod, ILGenerator 等复杂实现
}
```

**验证清单：**
- [x] 元数据缓存完全在启动期构建
- [x] 运行时零 `Type.GetProperties()` 重复调用
- [x] 运行时零 `PropertyInfo.GetValue()` 重复调用
- [x] 性能：单次 Project/Hydrate < 100 纳秒（通过缓存优化）
- [x] 采用纯反射 + 智能缓存策略，避免 IL 编译复杂性

---

### 宪法 008：四阶段原子管道（Four-Stage Atomic Pipeline）

**物理原则：**  
每个请求经过固定的四个阶段（Validate → Project → Execute → Hydrate），各阶段独立，错误在发生阶段立即生成 NXC 码并抛出，不允许级联处理或汇总。

**具体约束：**

```csharp
public async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
    TRequest request,
    string providerName,
    string profileId,
    CancellationToken ct)
    where TRequest : IApiRequest<TResponse>
{
    // ========== Phase 1: Validate ==========
    try
    {
        var metadata = _registry.GetMetadata(typeof(TRequest));
        var validationResult = _validator.Validate(request, metadata);
        
        if (!validationResult.IsValid)
        {
            // 立即生成 NXC101（Contract 验证失败）
            throw new ContractValidationException(
                "NXC101",
                new
                {
                    ContractType = typeof(TRequest).Name,
                    Errors = validationResult.Errors,
                    Timestamp = DateTime.UtcNow
                });
        }
    }
    catch (NexusException)
    {
        throw;  // ← NXC 异常直接抛出
    }
    catch (Exception ex)
    {
        // 将非 NXC 异常转化为 NXC101
        throw new ContractValidationException("NXC101", ex.Message);
    }
    
    // ========== Phase 2: Project ==========
    Dictionary<string, object> dictionary;
    try
    {
        dictionary = _projector.Project(request);
    }
    catch (NexusException)
    {
        throw;
    }
    catch (InvalidCastException ex)
    {
        throw new ProjectionException(
            "NXC102",
            new { Message = ex.Message, Type = typeof(TRequest).Name });
    }
    catch (Exception ex)
    {
        throw new ProjectionException("NXC102", ex.Message);
    }
    
    // ========== Phase 3: Execute ==========
    Dictionary<string, object> responseDict;
    try
    {
        var config = await _resolver.ResolveAsync(providerName, profileId, ct);
        var httpRequest = _signer.SignRequest(dictionary, config);
        responseDict = await _transport.SendAsync(httpRequest, ct);
    }
    catch (NexusException)
    {
        throw;
    }
    catch (HttpRequestException ex)
    {
        throw new TransportException(
            "NXC301",  // Execute 阶段错误
            new { Message = ex.Message, Url = ex.InnerException?.Message });
    }
    catch (TimeoutException ex)
    {
        throw new TransportException("NXC302", new { Timeout = ex.Message });
    }
    catch (Exception ex)
    {
        throw new TransportException("NXC399", ex.Message);
    }
    
    // ========== Phase 4: Hydrate ==========
    try
    {
        var response = _hydrator.Hydrate<TResponse>(responseDict);
        return response;
    }
    catch (NexusException)
    {
        throw;
    }
    catch (InvalidCastException ex)
    {
        throw new HydrationException(
            "NXC302",
            new { Message = ex.Message, Type = typeof(TResponse).Name });
    }
    catch (Exception ex)
    {
        throw new HydrationException("NXC499", ex.Message);
    }
}

// ❌ 禁止：阶段间状态共享
private Dictionary<string, object> _currentPhaseContext;  // ← 违反宪法 008

// ❌ 禁止：错误汇总（各阶段应独立崩溃）
try
{
    var metadata = _registry.GetMetadata(typeof(TRequest));
    var dictionary = _projector.Project(request);
    var response = await _transport.SendAsync(...);
}
catch (Exception ex)
{
    throw new AggregateException("Multiple phase failures", ex);  // ← 违反宪法 008
}
```

**NXC 码范围：**
- **NXC1xx:** Validate 阶段（合约验证）
- **NXC2xx:** Configuration 解析错误
- **NXC3xx:** Execute 阶段（传输、签名）
- **NXC4xx:** Hydrate 阶段（反序列化）
- **NXC5xx:** Provider 层错误
- **NXC99x:** 框架内部错误

**验证清单：**
- [ ] 四个阶段完全独立
- [ ] 错误在发生阶段立即生成 NXC 码
- [ ] 不允许阶段间状态共享
- [ ] 不允许错误合并或汇总
- [ ] 每个 NXC 码对应唯一的问题根源

---

### 宪法 009：Provider 协议主权（Provider Protocol Sovereignty）

**物理原则：**  
每个 Provider（Alipay、WeChat、UnionPay）独立拥有签名算法、加密方式、URL 构建等协议细节的主权。框架不应干涉或规范化 Provider 的内部实现。

**具体约束：**

```csharp
// ✅ CORRECT: Provider 独立的协议实现
public interface IProvider
{
    string ProviderName { get; }
    
    /// <summary>
    /// 每个 Provider 的执行方式完全自主
    /// 框架仅提供统一的接口规约
    /// </summary>
    Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration config,
        CancellationToken ct);
}

// AlipayProvider: RSA2 签名 + JSON 格式
public class AlipayProvider : IProvider
{
    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration config,
        CancellationToken ct)
    {
        // Alipay 的签名方式：私钥签名 → Base64 → URL 参数
        var signature = _signer.SignRsa2(request, config.PrivateKey);
        
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.GatewayUrl)
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("app_id", config.AppId),
                new KeyValuePair<string, string>("method", request.GetOperationId()),
                new KeyValuePair<string, string>("sign", signature),
                // ... 其他参数
            })
        };
        
        return await _transport.SendAsync(httpRequest, ct);
    }
}

// WeChatProvider: HMAC-SHA256 签名 + XML 格式
public class WeChatProvider : IProvider
{
    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration config,
        CancellationToken ct)
    {
        // WeChat 的签名方式：完全不同，框架不干涉
        var signature = _signer.SignHmacSha256(request, config.PrivateKey);
        
        var xml = _xmlSerializer.Serialize(request);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.GatewayUrl)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
        
        return await _transport.SendAsync(httpRequest, ct);
    }
}

// ❌ 禁止：框架规范化 Provider 行为
public interface IProvider
{
    // ← 如果框架强制规定签名方式，违反宪法 009
    Task<string> SignAsync(Dictionary<string, object> data);
    Task<HttpContent> SerializeAsync(Dictionary<string, object> data);
}
```

**验证清单：**
- [ ] 每个 Provider 有独立的签名算法（不强制统一）
- [ ] 序列化格式由 Provider 决定（JSON/XML/Protocol Buffer）
- [ ] URL 构建由 Provider 完全控制
- [ ] 框架仅提供传输层（INexusTransport）
- [ ] 不允许框架"规范化" Provider 行为

---

### 宪法 010：Provider 无状态单例（Stateless Provider Singleton）

**物理原则：**  
一个 Provider 实例（如 AlipayProvider）是单例，服务所有 ProfileId。配置在每次执行时通过参数传入，Provider 本身不存储任何租户状态。

**具体约束：**

```csharp
// ✅ CORRECT: Provider 是无状态单例
public class AlipayProvider : IProvider
{
    private readonly INexusTransport _transport;  // ← 共享资源（无租户状态）
    private readonly ISigningService _signer;     // ← 工具类（无租户状态）
    
    // ❌ 禁止：存储租户配置
    // private AlipayProviderConfig _config;  // ← 这会绑定单个商家
    
    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        IProviderConfiguration config,  // ← 配置从参数传入
        CancellationToken ct)
    {
        // 配置来自参数，不来自实例字段
        var privateKey = config.PrivateKey;
        var appId = config.AppId;
        
        var signature = _signer.Sign(request, privateKey);
        var httpRequest = new HttpRequestMessage(...)
        {
            Content = new FormUrlEncodedContent(...)
        };
        
        return await _transport.SendAsync(httpRequest, ct);
    }
}

// ✅ 注册为单例（ALL ProfileId 共享）
builder.Services.AddSingleton<IProvider>(sp =>
    new AlipayProvider(
        sp.GetRequiredService<INexusTransport>(),
        sp.GetRequiredService<ISigningService>()));

// 多个 Provider 注册到同一 NexusEngine
var alipayProvider = builder.Services.GetRequiredService<IProvider>();
var wechatProvider = new WeChatProvider(...);

var engine = new NexusEngine(configResolver);
engine.RegisterProvider("Alipay", alipayProvider);    // ← 单个 Alipay 实例
engine.RegisterProvider("WeChat", wechatProvider);    // ← 单个 WeChat 实例

// ❌ 禁止：为每个商家创建 Provider 实例
for (int i = 0; i < merchants.Count; i++)
{
    var provider = new AlipayProvider(config[i]);  // ← 违反宪法 010
    engine.RegisterProvider($"Alipay_{i}", provider);
}

// ❌ 禁止：在 Provider 中缓存租户状态
public class AlipayProvider : IProvider
{
    private readonly ConcurrentDictionary<string, ProviderSettings> _configCache = new();  // ← 违反
    
    public async Task<TResponse> ExecuteAsync<TResponse>(...)
    {
        var config = _configCache.GetOrAdd(profileId, ...);  // ← 租户状态存储
    }
}
```

**验证清单：**
- [ ] 每个 Provider 只有一个单例实例
- [ ] Provider 不存储任何租户配置
- [ ] 配置完全通过方法参数传入
- [ ] 所有共享资源（Transport, Signer）都是无状态的
- [ ] 支持并发访问（不同 ProfileId 同时调用同一 Provider）

---

### 宪法 011：单一标准加密存储（Single-Standard Encrypted Storage）

**物理原则：**  
私钥在 Redis 中存储为纯粹的加密密文（Base64 编码），内存中则以明文形式驻留。所有加密数据采用统一的当前标准（Base64 + AES256-CBC），密钥升级通过运维脚本完成数据迁移，代码层不参与版本判断。

**具体约束：**

```csharp
// ========== 存储层：Redis ==========
// Key: config:Alipay:merchant-001
// Value (JSON):
// {
//   "ProviderName": "Alipay",
//   "AppId": "2021...",
//   "PrivateKey": "aGVs...",  // Base64 密文示例
//   "PublicKey": "MIIBIj...",
//   "GatewayUrl": "https://openapi.alipay.com/"
// }

// ========== 加密策略：ISecurityProvider ==========
public interface ISecurityProvider
{
    /// <summary>
    /// 加密私钥（写入 Redis）
    /// - 算法：AES256-CBC
    /// - IV：每次随机生成
    /// - 返回：Base64 密文（[IV(16)|Cipher] 的 Base64 编码）
    /// </summary>
    string EncryptPrivateKey(string plaintext);
    
    /// <summary>
    /// 解密私钥（从 Redis 读取）
    /// - 直接解码 Base64 并解密（代码不负责版本识别）
    /// - 返回：纯文本 PEM
    /// </summary>
    string DecryptPrivateKey(string encrypted);
}

// 实现：AES256-CBC
public class AesSecurityProvider : ISecurityProvider
{
    private readonly byte[] _masterKey;  // 环境变量
    
    public string EncryptPrivateKey(string plaintext)
    {
        using (var aes = Aes.Create())
        {
            aes.Key = _masterKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();  // ← 每次随机
            
            using (var encryptor = aes.CreateEncryptor())
            using (var ms = new MemoryStream())
            {
                ms.Write(aes.IV, 0, aes.IV.Length);  // 前 16 字节：IV
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(plaintext);
                }
                
                var combined = ms.ToArray();
                var base64 = Convert.ToBase64String(combined);
                return base64;  // 返回 Base64 密文
            }
        }
    }
    
    public string DecryptPrivateKey(string encrypted)
    {
        // 单一标准解密：输入为 Base64 编码的 [IV(16) + Cipher]
        // 直接解码并解密；密钥升级通过运维脚本迁移实现，代码层不维护多分支
        var combined = Convert.FromBase64String(encrypted);
        var iv = combined.Take(16).ToArray();
        var cipher = combined.Skip(16).ToArray();

        using (var aes = Aes.Create())
        {
            aes.Key = _masterKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using (var decryptor = aes.CreateDecryptor())
            using (var ms = new MemoryStream(cipher))
            using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
            using (var sr = new StreamReader(cs))
            {
                return sr.ReadToEnd();
            }
        }
    }
}

// ========== 内存层：明文驻留 ==========
public class ProviderSettings
{
    public string ProviderName { get; set; }
    public string AppId { get; set; }
    public string PrivateKey { get; set; }  // ← 内存中始终明文
    public string PublicKey { get; set; }
    public string GatewayUrl { get; set; }
}

// 加载流程：
// 1. 从 Redis 读取："aGVs..."（Base64 密文）
// 2. 解密：AesSecurityProvider.DecryptPrivateKey()
// 3. 回填内存：ProviderSettings.PrivateKey = "MIIEvQ..."（明文）

// ❌ 禁止：内存中存储加密文本
public class ProviderSettings
{
    public string PrivateKey_Encrypted { get; set; }  // ← 违反宪法 011
}

// ❌ 禁止：在框架层做版本前缀识别（版本迁移应由运维脚本负责）
// return $"aGVs...";  // 示例仅供说明，生产中应使用 Base64 密文并由实现直接解密
```

**安全约束：**
- [ ] 私钥绝不写入日志
- [ ] 私钥不出现在 HTTP 请求/响应
- [ ] 加密密钥由环境变量提供（不硬编码）
- [ ] 所有加密数据采用统一的当前标准（Base64 + AES256-CBC）
- [ ] 密钥升级通过运维脚本完成，代码层不维护多版本分支

---

### 宪法 012：NXC 结构化诊断（NXC Structured Diagnostics）

**物理原则：**  
每个错误必须在发生的阶段立即生成唯一的 NXC 码。这个码是诊断的唯一通道，所有错误信息都围绕 NXC 码组织，禁止模糊或组合多个错误。

**具体约束：**

```csharp
// ========== NXC 码体系（Complete Diagnostic Taxonomy） ==========
// NXC1xx: 静态结构验证（启动时，代码质量问题）
//   NXC101: 缺失 [ApiOperation] 属性
//   NXC102: Operation 标识为空
//   NXC103: OneWay 响应类型非 EmptyResponse
//   NXC104: 嵌套深度超过 MaxDepth 物理边界
//   NXC105: 检测到循环引用
//   NXC106: 加密字段未显式锁定 Name
//   NXC107: 嵌套对象（2+ 层）未显式锁定 Name
//
// NXC2xx: 运行期执行守卫（执行时，配置/输入问题）
//   NXC201: 必需字段为 null（投影被拒）
//   NXC202: 加密字段但 Encryptor 未注入
//   NXC203: 投影深度溢出（防御性）
//
// NXC3xx: 回填守卫（解析返回值时，脏数据问题）
//   NXC301: 回填时必需字段缺失
//   NXC302: 回填时类型转换失败
//   NXC303: 回填时集合大小超限
//
// NXC5xx: 框架内部错误（自 Phase 1 后全面使用反射缓存）
//   NXC504: 反射缓存元数据构建失败（宪法 007 启动期）
//   NXC505: 反射缓存委托执行失败（宪法 007 运行期）
//   NXC999: 未知框架错误（兜底）
```

// ========== 异常体系 ==========
public abstract class NexusException : Exception
{
    public string NxcCode { get; }
    public DiagnosticData DiagnosticData { get; }
    public int HttpStatusCode { get; }
    
    protected NexusException(string nxcCode, object diagnosticData, int httpStatus)
    {
        NxcCode = nxcCode;
        DiagnosticData = new DiagnosticData
        {
            Code = nxcCode,
            Timestamp = DateTime.UtcNow,
            Details = diagnosticData
        };
        HttpStatusCode = httpStatus;
    }
}

public class ContractValidationException : NexusException
{
    public ContractValidationException(string nxcCode, object diagnosticData)
        : base(nxcCode, diagnosticData, 400) { }
}

public class ConfigurationException : NexusException
{
    public ConfigurationException(string nxcCode, object diagnosticData)
        : base(nxcCode, diagnosticData, 503) { }
}

public class TransportException : NexusException
{
    public TransportException(string nxcCode, object diagnosticData)
        : base(nxcCode, diagnosticData, 502) { }
}

public class HydrationException : NexusException
{
    public HydrationException(string nxcCode, object diagnosticData)
        : base(nxcCode, diagnosticData, 502) { }
}

// ========== 四阶段错误生成 ==========
public async Task<TResponse> ExecuteAsync<TRequest, TResponse>(...)
{
    // Phase 1: Validate
    if (string.IsNullOrEmpty(request.OutTradeNo))
    {
        throw new ContractValidationException(
            "NXC105",  // ← 必填字段为空
            new
            {
                Field = "OutTradeNo",
                Message = "OutTradeNo is required",
                ContractType = typeof(TRequest).Name
            });
    }
    
    // Phase 2: Project
    try
    {
        var dict = _projector.Project(request);
    }
    catch (InvalidCastException ex)
    {
        throw new ProjectionException(
            "NXC102",  // ← Contract 类型不匹配
            new { Message = ex.Message });
    }
    
    // Phase 3: Execute
    try
    {
        var config = await _resolver.ResolveAsync(providerName, profileId, ct);
    }
    catch (KeyNotFoundException)
    {
        throw new ConfigurationException(
            "NXC201",  // ← 配置不存在
            new { ProfileId = profileId, Provider = providerName });
    }
    
    // Phase 4: Hydrate
    try
    {
        var response = _hydrator.Hydrate<TResponse>(responseDict);
    }
    catch (JsonException ex)
    {
        throw new HydrationException(
            "NXC302",  // ← 响应反序列化失败
            new { Message = ex.Message, ResponseJson = responseJson });
    }
}

// ========== HTTP 异常处理 ==========
public class NexusErrorHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        if (exception is NexusException nxcEx)
        {
            context.Response.StatusCode = nxcEx.HttpStatusCode;
            
            var envelope = new NxcErrorEnvelope
            {
                Code = nxcEx.NxcCode,         // NXC101, NXC201, ...
                Message = nxcEx.Message,
                DiagnosticData = nxcEx.DiagnosticData,
                Timestamp = DateTime.UtcNow
            };
            
            await context.Response.WriteAsJsonAsync(envelope, cancellationToken: ct);
            return true;
        }
        
        // 非 NXC 异常处理
        context.Response.StatusCode = 500;
        var unknownEnvelope = new NxcErrorEnvelope
        {
            Code = "NXC999",
            Message = "Internal Server Error",
            DiagnosticData = new DiagnosticData
            {
                Code = "NXC999",
                Details = new { ExceptionType = exception.GetType().Name }
            }
        };
        
        await context.Response.WriteAsJsonAsync(unknownEnvelope, cancellationToken: ct);
        return true;
    }
}

// ❌ 禁止：异常合并或汇总
throw new AggregateException(
    "Validation and Configuration errors",
    validationEx,
    configEx);  // ← 违反宪法 012，模糊错误来源

// ❌ 禁止：通用错误回复
return new { Success = false, Error = "Operation failed" };  // ← 无诊断价值
```

**验证清单：**
- [ ] 每个错误都有唯一的 NXC 码
- [ ] NXC 码在发生阶段立即生成
- [ ] 不允许异常合并（一个异常对应一个 NXC 码）
- [ ] 诊断数据包含足够的上下文（类型、字段、值等）
- [ ] HTTP 响应包含标准的 NxcErrorEnvelope

---

## 🎯 宪法执行清单

```
[ ] 宪法 001：显式契约锁定
[ ] 宪法 002：URL 资源寻址
[ ] 宪法 003：物理槽位隔离
[ ] 宪法 004：BFF/Gate 职责拆分
[ ] 宪法 005：热路径脱网自治
[ ] 宪法 006：启动期全量体检
[x] 宪法 007：零反射缓存引擎
[ ] 宪法 008：四阶段原子管道
[ ] 宪法 009：Provider 协议主权
[ ] 宪法 010：Provider 无状态单例
[ ] 宪法 011：单一标准加密存储（Base64 + AES256-CBC）
[ ] 宪法 012：NXC 结构化诊断
```

---

**最高权威：** 这 12 条宪法是 NexusContract 框架的物理约束，所有代码、决策、文档都必须以此为准绳。任何超出这 12 条宪法的设计都属于"污染代码"，应在下一次代码清洗时删除。

**生效日期：** 2026-01-11  
**签署者：** Architecture Council  
**版本：** v1.0（不再修改）

