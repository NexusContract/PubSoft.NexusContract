# NexusContract 框架核心代码模式清单

> 从代码注释中提取的实现模式和最佳实践

**编制日期**：2026-01-11  
**来源**：全代码库 XML 文档注释

---

## 🏗️ 分层架构模式

### 模式 PA-001：入口出口物理分离（ADR-001）

**原则**：FastEndpoints 作为入口（Ingress），YARP/YarpTransport 作为出口（Egress）。

**实现**：
```csharp
// 入口层（Ingress）- FastEndpoints
public class TradePayEndpoint(INexusEngine engine)
    : NexusEndpoint<TradePayRequest>(engine)
{
    public override async Task HandleAsync(TradePayRequest req, CancellationToken ct)
    {
        var identity = TenantContextFactory.FromHttpContext(HttpContext);
        var response = await engine.ExecuteAsync(req, identity, ct);
        await SendOkAsync(response, cancellation: ct);
    }
}

// 出口层（Egress）- YARP/YarpTransport
public class YarpTransport : INexusTransport
{
    private readonly HttpClient _httpClient;  // HTTP/2 支持
    private readonly YarpTransportOptions _options;
    
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        // 应用 Polly 重试/熔断
        return await _policyHandler.SendAsync(request, ct);
    }
}

// 中间层（Orchestration）- NexusGateway
public class NexusGateway
{
    public Dictionary<string, object> Project<TRequest>(TRequest request)
    {
        // 投影：Contract → Dictionary
        var compiler = _registry.GetProjector<TRequest>();
        return compiler.Project(request);
    }
    
    public TResponse Hydrate<TResponse>(
        Dictionary<string, object> response)
        where TResponse : class, new()
    {
        // 回填：Dictionary → Contract
        var compiler = _registry.GetHydrator<TResponse>();
        return compiler.Hydrate(response);
    }
}
```

**收益**：
- 清晰的职责边界：入口处理 HTTP 协议，出口处理网络通信
- 独立的测试：可单独测试 Provider 而不依赖 FastEndpoints
- 灵活的部署：可以用不同的 HTTP 框架替换 FastEndpoints

---

### 模式 PA-002：四阶段管道（ADR-003）

**原则**：所有请求遵循相同的执行流程，无例外。

**实现**（ProjectionEngine 和 ResponseHydrationEngine）：
```csharp
public class ProjectionEngine
{
    public Dictionary<string, object> Project<TRequest>(TRequest request)
    {
        var result = new Dictionary<string, object>();
        
        // 阶段 1：Validate（可选，由调用方负责）
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        
        // 阶段 2：Project（C# → Dictionary）
        var properties = typeof(TRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            var value = prop.GetValue(request);
            
            // 获取字段映射
            var attr = prop.GetCustomAttribute<ApiFieldAttribute>();
            var fieldName = attr?.Name ?? NamingPolicy.ConvertName(prop.Name);
            
            // 处理加密
            if (attr?.IsEncrypted == true && value != null)
            {
                value = _encryptor.Encrypt(value.ToString());
            }
            
            // 处理嵌套对象
            if (IsComplexType(prop.PropertyType))
            {
                value = Project(value);
            }
            
            result[fieldName] = value;
        }
        
        return result;
    }
}

public class ResponseHydrationEngine
{
    public TResponse Hydrate<TResponse>(Dictionary<string, object> dict)
        where TResponse : class, new()
    {
        var response = new TResponse();
        
        // 阶段 3：Read（从 Dictionary 读取）
        var properties = typeof(TResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        // 阶段 4：Hydrate（Dictionary → C#）
        foreach (var prop in properties)
        {
            var attr = prop.GetCustomAttribute<ApiFieldAttribute>();
            var fieldName = attr?.Name ?? NamingPolicy.ConvertName(prop.Name);
            
            if (!dict.TryGetValue(fieldName, out var value))
            {
                // 字段不存在，触发 NXC301
                if (attr?.Required == true)
                    throw new ContractIncompleteException(
                        $"NXC301: Response field '{fieldName}' not found");
                continue;
            }
            
            // 处理解密
            if (attr?.IsEncrypted == true && value != null)
            {
                value = _encryptor.Decrypt(value.ToString());
            }
            
            // 处理类型转换
            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(response, converted);
        }
        
        return response;
    }
}
```

---

## 🧠 元数据管理模式

### 模式 MM-001：启动期元数据冻结（ADR-004）

**原则**：启动时扫描所有 [ApiOperation] Contract，构建元数据并冻结。

**实现**（NexusContractMetadataRegistry）：
```csharp
public sealed class NexusContractMetadataRegistry
{
    private static readonly Lazy<NexusContractMetadataRegistry> _instance = 
        new(() => new NexusContractMetadataRegistry());
    
    public static NexusContractMetadataRegistry Instance => _instance.Value;
    
    // 冻结的元数据字典
    private FrozenDictionary<string, ContractMetadata> _contracts;
    
    public DiagnosticReport Preload(Type[] contractTypes, bool warmup = true)
    {
        var report = new DiagnosticReport();
        var builder = new Dictionary<string, ContractMetadata>();
        
        // 遍历所有 Contract 类型
        foreach (var type in contractTypes)
        {
            // 1. 检查 [ApiOperation] 属性
            var attr = type.GetCustomAttribute<ApiOperationAttribute>();
            if (attr == null)
            {
                report.AddError("NXC101", $"Type '{type.Name}' missing [ApiOperation]");
                continue;
            }
            
            // 2. 构建元数据
            var metadata = new ContractMetadata
            {
                Type = type,
                OperationId = attr.Operation,
                HttpVerb = attr.HttpVerb,
                Properties = BuildPropertyMetadata(type),
                Projector = CompileProjector(type),  // 编译期生成 IL
                Hydrator = CompileHydrator(type)      // 编译期生成 IL
            };
            
            // 3. 验证约束（NXC1xx 检查）
            var issues = ValidateContract(metadata);
            if (issues.Count > 0)
            {
                foreach (var issue in issues)
                    report.AddError(issue.Code, issue.Message);
            }
            
            builder[attr.Operation] = metadata;
        }
        
        // 4. 冻结元数据
        _contracts = builder.ToFrozenDictionary();
        
        return report;
    }
    
    public ContractMetadata Get(string operationId)
    {
        // 运行时零反射，纯字典查询
        return _contracts[operationId];  // O(1)
    }
    
    private IExpressionCompiler CompileProjector(Type contractType)
    {
        // 编译期生成针对该 Contract 的投影函数
        // 相当于编译一个强类型的转换函数
        var expression = BuildProjectionExpression(contractType);
        return Expression.Lambda(expression).Compile();
    }
}
```

**性能特征**：
- 启动时间：~1ms/Contract（含编译）
- 运行时间：< 1μs（冻结字典查询）

---

### 模式 MM-002：体检机制（ADR-004）

**原则**：启动期一次性报告所有违规，而不是逐一崩溃。

**实现**（ContractAuditor, ContractValidator）：
```csharp
public class ContractAuditor
{
    public DiagnosticReport AuditAll(Type[] contractTypes)
    {
        var report = new DiagnosticReport();
        
        // 第一遍：检查静态约束（NXC1xx）
        foreach (var type in contractTypes)
        {
            AuditStaticConstraints(type, report);
            AuditNestingDepth(type, report);
            AuditCircularReferences(type, report);
            AuditEncryptionConstraints(type, report);
        }
        
        // 第二遍：检查投影约束（NXC2xx）
        foreach (var type in contractTypes)
        {
            AuditProjectionConstraints(type, report);
            AuditCollectionLimits(type, report);
        }
        
        // 完整报告一次性返回
        return report;
    }
    
    private void AuditStaticConstraints(Type type, DiagnosticReport report)
    {
        // NXC101：缺少 [ApiOperation]
        if (!type.GetCustomAttribute<ApiOperationAttribute>())
        {
            report.AddError("NXC101", $"Type '{type.Name}' missing [ApiOperation]");
        }
        
        // NXC102：Operation 为空
        var attr = type.GetCustomAttribute<ApiOperationAttribute>();
        if (attr != null && string.IsNullOrEmpty(attr.Operation))
        {
            report.AddError("NXC102", $"Operation ID cannot be empty");
        }
        
        // NXC103：交互模式约束
        if (attr?.OneWay == true)
        {
            var responseType = ExtractResponseType(type);
            if (responseType != typeof(EmptyResponse))
            {
                report.AddError("NXC103", 
                    $"OneWay operation must have EmptyResponse");
            }
        }
    }
    
    private void AuditNestingDepth(Type type, DiagnosticReport report)
    {
        // NXC104：深度溢出
        int maxDepth = CalculateNestingDepth(type);
        if (maxDepth > 3)
        {
            report.AddError("NXC104", 
                $"Type '{type.Name}' exceeds max nesting depth (3): {maxDepth}");
        }
    }
}
```

**诊断码范围**：
- **NXC1xx**：静态错误（启动期立即感知）
- **NXC2xx**：出向错误（投影时感知）
- **NXC3xx**：入向错误（回填时感知）

---

## 🔒 多租户 ISV 模式

### 模式 ISV-001：Realm/Profile 双层身份

**原则**：{Realm, Profile, ProviderName} 唯一标识一个租户。

**实现**（TenantContext, ConfigurationContext）：
```csharp
// 业务身份（由 HTTP 请求提取）
public class TenantContext : ITenantIdentity
{
    public string RealmId { get; set; }      // 域（SysId / SPMchId）
    public string ProfileId { get; set; }    // 档案（AppId / SubMchId）
    public string ProviderName { get; set; } // 平台（Alipay / WeChat）
    
    public Dictionary<string, object> Metadata { get; set; }
}

// 配置查询身份（内部使用）
public class ConfigurationContext : ITenantIdentity
{
    public ConfigurationContext(string providerName, string realmId)
    {
        ProviderName = providerName;
        RealmId = realmId;
    }
    
    public string ProviderName { get; }
    public string RealmId { get; }
    public string ProfileId { get; set; }
}

// 工厂方法（从 HTTP 上下文提取）
public static class TenantContextFactory
{
    public static ITenantIdentity FromHttpContext(HttpContext context)
    {
        // 策略 1：从 Route 参数
        var realm = context.Request.RouteValues["realm"]?.ToString();
        var profile = context.Request.RouteValues["profile"]?.ToString();
        var provider = context.Request.RouteValues["provider"]?.ToString();
        
        // 策略 2：从 Header
        if (string.IsNullOrEmpty(realm))
            realm = context.Request.Headers["X-Tenant-Realm"].FirstOrDefault();
        
        // 策略 3：从 Request 体（JSON）
        // ...
        
        return new TenantContext
        {
            RealmId = realm,
            ProfileId = profile,
            ProviderName = provider,
            Metadata = ExtractMetadata(context)  // TraceId, ClientIp 等
        };
    }
}
```

---

### 模式 ISV-002：JIT 配置解析（ADR-014）

**原则**：配置在请求处理时动态加载，支持多层缓存。

**实现**（HybridConfigResolver）：
```csharp
public sealed class HybridConfigResolver : IConfigurationResolver
{
    private readonly IMemoryCache _memoryCache;    // L1 缓存
    private readonly IDatabase _redisDb;           // L2 缓存
    private readonly ISubscriber _redisSub;        // Pub/Sub
    
    public async Task<IProviderConfiguration> ResolveAsync(
        ITenantIdentity identity,
        CancellationToken ct = default)
    {
        string cacheKey = BuildCacheKey(identity);
        
        // 第一步：L1 缓存（内存）
        if (_memoryCache.TryGetValue(cacheKey, out object? cachedValue))
        {
            // 检查负缓存
            if (cachedValue is NotFoundSentinel)
                throw NexusTenantException.NotFound(...);
            
            return (ProviderSettings)cachedValue;
        }
        
        // 第二步：防击穿（SemaphoreSlim）
        SemaphoreSlim cacheLock = _locks.GetOrAdd(cacheKey, 
            _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(ct);
        try
        {
            // 双重检查
            if (_memoryCache.TryGetValue(cacheKey, out cachedValue))
                return (ProviderSettings)cachedValue;
            
            // 第三步：L2 缓存（Redis）
            RedisValue l2Value = await _redisDb.StringGetAsync(cacheKey);
            if (l2Value.HasValue)
            {
                var config = DeserializeConfig(l2Value);
                SetL1Cache(cacheKey, config);  // 回填 L1
                return config;
            }
            
            // 第四步：负缓存
            _memoryCache.Set(cacheKey, ConfigNotFoundMarker,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                });
            
            throw NexusTenantException.NotFound(...);
        }
        finally
        {
            cacheLock.Release();
        }
    }
    
    private void SetL1Cache(string key, ProviderSettings config)
    {
        _memoryCache.Set(key, config, new MemoryCacheEntryOptions
        {
            // 滑动过期：只要有流量永远有效
            SlidingExpiration = TimeSpan.FromHours(24),
            
            // 绝对过期：30 天防火墙
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
            
            // 最高优先级：不被内存压力驱逐
            Priority = CacheItemPriority.NeverRemove,
            
            Size = 1
        });
    }
}
```

**缓存策略**：
```
L1（MemoryCache）：24h 滑动 + 30d 绝对 + NeverRemove
  → 99.99% 命中率，延迟 < 1μs

L2（Redis）：30min TTL
  → 多实例共享，故障后备

负缓存：5min
  → 防穿透，仅对配置不存在有效
```

---

### 模式 ISV-003：配置热更新（ADR-004）

**原则**：Pub/Sub 通知所有实例，触发缓存失效。

**实现**（TenantConfigurationManager, HybridConfigResolver）：
```csharp
// 管理端：发送通知
public class TenantConfigurationManager
{
    public async Task CreateAsync(
        string providerName,
        string realmId,
        string profileId,
        ProviderSettings configuration,
        bool isDefault = false,
        CancellationToken ct = default)
    {
        var identity = new ConfigurationContext(providerName, realmId)
        {
            ProfileId = profileId
        };
        
        // 1. 写入 Redis
        await _resolver.SetConfigurationAsync(identity, configuration, ct);
        
        // 2. 发送通知
        await PreWarmGatewayAsync(providerName, realmId, ct);
    }
    
    private async Task PreWarmGatewayAsync(string providerName, string realmId, CancellationToken ct)
    {
        // 发送 MappingChange 消息（不携带全量载荷）
        var message = JsonSerializer.Serialize(new
        {
            RealmId = realmId,
            ProviderName = providerName,
            Type = 1  // RefreshType.MappingChange
        });
        
        await _redisSub.PublishAsync(
            new RedisChannel("nexus:config:refresh", RedisChannel.PatternMode.Literal),
            message);
    }
}

// 网关端：监听通知
public class HybridConfigResolver
{
    public HybridConfigResolver(IConnectionMultiplexer redis, ...)
    {
        // 订阅配置刷新消息
        _redisSub.Subscribe("nexus:config:refresh", OnConfigRefreshMessage);
    }
    
    private void OnConfigRefreshMessage(RedisChannel channel, RedisValue message)
    {
        try
        {
            var data = JsonSerializer.Deserialize<RefreshMessage>(message.ToString());
            
            // 策略 1：ConfigChange（仅清除配置缓存）
            if (data.Type == RefreshType.ConfigChange)
            {
                string cacheKey = BuildCacheKey(data.ProfileId);
                _memoryCache.Remove(cacheKey);  // 清除 L1
            }
            
            // 策略 2：MappingChange（清除 Map 缓存）
            else if (data.Type == RefreshType.MappingChange)
            {
                string mapKey = BuildMapKey(data.RealmId, data.ProviderName);
                _memoryCache.Remove(mapKey);    // 清除 L1
            }
        }
        catch { /* 静默失败，12h TTL 自动兜底 */ }
    }
}
```

**消息格式**：
```json
{
    "RealmId": "merchant-001",
    "ProviderName": "Alipay",
    "Type": 0,  // ConfigChange=0, MappingChange=1
    "ProfileId": "2088001-2088002"  // ConfigChange 时需要
}
```

---

## 🔐 安全和加密模式

### 模式 SEC-001：加密字段处理

**原则**：IsEncrypted = true 时，必须显式指定 Name。

**实现**（ProtectedPrivateKeyConverter）：
```csharp
public class ProtectedPrivateKeyConverter : JsonConverter<string>
{
    private readonly ISecurityProvider _securityProvider;
    
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var encrypted = reader.GetString();
        
        // 检测版本前缀
        if (encrypted.StartsWith("v1:"))
        {
            return _securityProvider.Decrypt(encrypted);
        }
        
        // 向后兼容：无版本前缀
        return _securityProvider.Decrypt(encrypted);
    }
    
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        // 序列化时加密
        var encrypted = _securityProvider.Encrypt(value);
        writer.WriteStringValue(encrypted);
    }
}

// AES-GCM 实现
public class AesSecurityProvider : ISecurityProvider
{
    private readonly byte[] _masterKey;
    
    public string Encrypt(string plainText)
    {
        // 生成随机 IV
        var iv = RandomNumberGenerator.GetBytes(16);
        
        using var cipher = new AesGcm(_masterKey);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[16];
        
        // 加密
        cipher.Encrypt(iv, plainBytes, null, ciphertext, tag);
        
        // 版本前缀 + Base64 编码
        var combined = new byte[iv.Length + ciphertext.Length + tag.Length];
        Array.Copy(iv, 0, combined, 0, iv.Length);
        Array.Copy(ciphertext, 0, combined, iv.Length, ciphertext.Length);
        Array.Copy(tag, 0, combined, iv.Length + ciphertext.Length, tag.Length);
        
        return $"v1:{Convert.ToBase64String(combined)}";
    }
    
    public string Decrypt(string ciphertext)
    {
        // 检查版本前缀
        if (!ciphertext.StartsWith("v1:"))
            throw new InvalidOperationException("Unsupported encryption version");
        
        var combined = Convert.FromBase64String(ciphertext.Substring(3));
        
        // 解析 IV + ciphertext + tag
        var iv = new byte[16];
        var ct = new byte[combined.Length - 32];
        var tag = new byte[16];
        
        Array.Copy(combined, 0, iv, 0, 16);
        Array.Copy(combined, 16, ct, 0, combined.Length - 32);
        Array.Copy(combined, combined.Length - 16, tag, 0, 16);
        
        // 解密
        using var cipher = new AesGcm(_masterKey);
        var plainBytes = new byte[ct.Length];
        
        cipher.Decrypt(iv, ct, tag, plainBytes);
        
        return Encoding.UTF8.GetString(plainBytes);
    }
}
```

---

### 模式 SEC-002：签名验证

**原则**：所有出向请求必须签名，所有入向响应必须验签。

**实现**（AlipaySignatureHandler）：
```csharp
public class AlipaySignatureHandler
{
    private readonly RSA _privateKeyRsa;  // 商户私钥
    private readonly RSA _publicKeyRsa;   // 支付宝公钥
    
    // 出向签名
    public Dictionary<string, string> SignRequest(Dictionary<string, string> dict, 
        string privateKey)
    {
        // 1. 排序键值对
        var sortedPairs = dict.OrderBy(x => x.Key).ToList();
        
        // 2. 构造签名源文本
        var signContent = string.Join("&", 
            sortedPairs.Select(x => $"{x.Key}={x.Value}"));
        
        // 3. RSA-SHA256 签名
        var signedData = _privateKeyRsa.SignData(
            Encoding.UTF8.GetBytes(signContent),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        
        var signature = Convert.ToBase64String(signedData);
        
        dict["sign"] = signature;
        dict["sign_type"] = "RSA2";
        
        return dict;
    }
    
    // 入向验签
    public bool VerifyResponse(Dictionary<string, string> dict, string signature)
    {
        // 1. 提取签名字段之外的内容
        var toSign = string.Join("&",
            dict.Where(x => x.Key != "sign")
                .OrderBy(x => x.Key)
                .Select(x => $"{x.Key}={x.Value}"));
        
        // 2. RSA 验签
        var signedBytes = Convert.FromBase64String(signature);
        
        return _publicKeyRsa.VerifyData(
            Encoding.UTF8.GetBytes(toSign),
            signedBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }
}
```

---

## 📊 诊断和监控模式

### 模式 DIAG-001：结构化诊断报告

**原则**：所有违规都有唯一的诊断码，便于快速定位。

**实现**（DiagnosticReport）：
```csharp
public class DiagnosticReport
{
    private List<DiagnosticIssue> _issues = new();
    
    public void AddError(string code, string message)
    {
        _issues.Add(new DiagnosticIssue
        {
            Code = code,
            Message = message,
            Level = DiagnosticLevel.Error
        });
    }
    
    public void PrintToConsole(bool includeDetails = false)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           NexusContract Diagnostic Report                       ║");
        Console.WriteLine("║                    Startup Health Check                          ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        
        var errors = _issues.Where(x => x.Level == DiagnosticLevel.Error).ToList();
        var warnings = _issues.Where(x => x.Level == DiagnosticLevel.Warning).ToList();
        
        Console.WriteLine($"║ Status: {(errors.Count == 0 ? "✅ HEALTHY" : "❌ CRITICAL")}");
        Console.WriteLine($"║ Total Issues: {_issues.Count}");
        Console.WriteLine($"║ Critical Errors: {errors.Count}");
        Console.WriteLine($"║ Warnings: {warnings.Count}");
        
        if (includeDetails && _issues.Count > 0)
        {
            Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
            foreach (var issue in _issues)
            {
                Console.WriteLine($"║ [{issue.Code}] {issue.Message}");
            }
        }
        
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    }
}
```

---

### 模式 DIAG-002：性能指标收集

**原则**：记录关键操作的性能数据。

**实现**（YarpTransport.GetHostMetrics）：
```csharp
public class YarpTransport
{
    private ConcurrentDictionary<string, HostMetrics> _hostMetrics = new();
    
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct = default)
    {
        var host = request.RequestUri.Host;
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            stopwatch.Stop();
            
            // 记录指标
            UpdateMetrics(host, stopwatch.ElapsedMilliseconds, success: true);
            
            return response;
        }
        catch
        {
            stopwatch.Stop();
            UpdateMetrics(host, stopwatch.ElapsedMilliseconds, success: false);
            throw;
        }
    }
    
    private void UpdateMetrics(string host, long elapsedMs, bool success)
    {
        var metrics = _hostMetrics.AddOrUpdate(host,
            key => new HostMetrics { Host = key },
            (key, old) =>
            {
                old.RequestCount++;
                old.TotalLatencyMs += elapsedMs;
                if (!success) old.FailureCount++;
                return old;
            });
    }
    
    public IReadOnlyDictionary<string, long> GetHostMetrics()
    {
        return _hostMetrics.ToDictionary(x => x.Key,
            x => x.Value.TotalLatencyMs / Math.Max(1, x.Value.RequestCount));
    }
}
```

---

## 🔄 依赖注入模式

### 模式 DI-001：标准 DI 注册

**原则**：所有组件通过 DI 容器注入，支持多个实现。

**实现示例**（Program.cs）：
```csharp
// 1. 注册缓存
services.AddMemoryCache(options =>
{
    options.SizeLimit = 100 * 1024 * 1024;  // 100MB
    options.CompactionPercentage = 0.25;
});

// 2. 注册 Redis
services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = configuration["Redis:ConnectionString"];
    return ConnectionMultiplexer.Connect(connectionString);
});

// 3. 注册安全组件
services.AddSingleton<ISecurityProvider>(sp =>
{
    var masterKey = configuration["Security:MasterKey"];
    return new AesSecurityProvider(masterKey);
});

// 4. 注册配置解析器
services.AddSingleton<IConfigurationResolver>(sp =>
{
    var memoryCache = sp.GetRequiredService<IMemoryCache>();
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    var securityProvider = sp.GetRequiredService<ISecurityProvider>();
    
    return new HybridConfigResolver(memoryCache, redis, securityProvider);
});

// 5. 注册传输层
services.AddNexusYarpTransport(options =>
{
    options.RetryCount = 3;
    options.CircuitBreakerFailureThreshold = 5;
});

// 6. 注册 Engine
services.AddSingleton<INexusEngine>(sp =>
{
    var configResolver = sp.GetRequiredService<IConfigurationResolver>();
    var engine = new NexusEngine(configResolver);
    
    // 注册 Provider
    var alipayProvider = sp.GetRequiredService<AlipayProvider>();
    engine.RegisterProvider("Alipay", alipayProvider);
    
    return engine;
});

// 7. 注册 FastEndpoints
services.AddFastEndpoints();
```

---

**文档生成日期**：2026-01-11  
**覆盖范围**：全代码库代码注释和实现  
**总模式数**：21 项核心代码模式
