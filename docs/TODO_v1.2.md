# NexusContract v1.2 实施清单 (ISV Multi-Tenant Execution)

> **生成时间:** 2026-01-10  
> **基于:** 架构蓝图 v1.2 vs 当前代码实现差距分析  
> **状态:** 🔴 待完成 | 🟡 进行中 | 🟢 已完成

---

## 🎯 架构蓝图 v1.2 核心目标

**v1.2 核心特性:**
- ✅ ISV 多商户动态接入（上百商户）
- ✅ JIT 配置解析（L1/L2 缓存）
- ✅ 租户上下文自动提取（SysId/AppId）
- ✅ Zero-Code Ingress（.NET 10 Primary Constructor）
- ✅ 类型推断设计（Endpoint 只需指定 TReq）

---

## 📊 实施状态总览

| 分类 | 总数 | 已完成 | 进行中 | 待完成 | 完成率 |
|------|------|--------|--------|--------|--------|
| **核心架构** | 8 | 2 | 0 | 6 | 25% |
| **抽象接口** | 10 | 0 | 0 | 10 | 0% |
| **基础设施** | 5 | 0 | 0 | 5 | 0% |
| **零代码 Ingress** | 4 | 1 | 0 | 3 | 25% |
| **文档完善** | 3 | 2 | 0 | 1 | 67% |
| **示例项目** | 4 | 3 | 0 | 1 | 75% |
| **总计** | 34 | 8 | 0 | 26 | 24% |

---

## 🔴 第一优先级：核心架构组件（关键路径）

### 1. INexusEngine 接口与实现 🔴 **缺失**

**架构位置:** 核心调度层（蓝图 §4.A）  
**当前状态:** ❌ 不存在  
**设计规格:**
```csharp
namespace NexusContract.Abstractions.Core;

/// <summary>
/// Nexus 引擎接口：多租户 ISV 网关的调度大脑
/// </summary>
public interface INexusEngine
{
    /// <summary>
    /// 执行请求（自动调度到对应 Provider）
    /// </summary>
    Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        TenantContext tenantContext,
        CancellationToken ct = default)
        where TResponse : class, new();
}
```

**实现要求:**
- [ ] 基于 OperationId 或 ProviderName 路由到具体 Provider
- [ ] 集成 IConfigurationResolver 进行 JIT 配置加载
- [ ] 支持多 Provider 注册（Alipay, WeChat, UnionPay...）
- [ ] 提供诊断日志和性能埋点
- [ ] 处理 Provider 调用失败的回退逻辑

**文件位置:**
- 接口: `src/NexusContract.Abstractions/Core/INexusEngine.cs`
- 实现: `src/NexusContract.Core/Engine/NexusEngine.cs`

**依赖项:**
- TenantContext（待实现 #2）
- IConfigurationResolver（待实现 #3）
- IProvider（待实现 #4）

---

### 2. TenantContext 租户上下文 🔴 **缺失**

**架构位置:** 核心契约层（蓝图 §3.A）  
**当前状态:** ❌ 不存在（架构蓝图提到但未实现）  
**设计规格:**
```csharp
namespace NexusContract.Abstractions.Contracts;

/// <summary>
/// 租户上下文：ISV 多商户场景的身份抽象
/// </summary>
public sealed class TenantContext
{
    /// <summary>系统标识（对应 Alipay 的 SysId / WeChat 的 SpMchId）</summary>
    public required string RealmId { get; init; }
    
    /// <summary>应用标识（对应 Alipay 的 AppId / WeChat 的 SubMchId）</summary>
    public required string ProfileId { get; init; }
    
    /// <summary>渠道标识（如 "Alipay", "WeChat"）</summary>
    public string? ProviderName { get; init; }
    
    /// <summary>扩展元数据（用于自定义租户属性）</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
```

**实现要求:**
- [ ] 从 HTTP Headers 自动提取（X-Realm-Id, X-Profile-Id, X-Provider）
- [ ] 从 Request Body 字段提取（如 `app_id`, `sys_id`）
- [ ] 提供 TenantContextFactory 工厂类
- [ ] 支持自定义提取策略（IHttpContextExtractor）
- [ ] 验证租户标识格式（Regex 或自定义规则）

**文件位置:**
- `src/NexusContract.Abstractions/Contracts/TenantContext.cs`
- `src/NexusContract.Core/Utilities/TenantContextFactory.cs`

**使用场景:**
```csharp
// Endpoint 中自动提取
var tenantCtx = TenantContextFactory.Create(req, HttpContext);

// Engine 调度时使用
var response = await _engine.ExecuteAsync(req, tenantCtx, ct);
```

---

### 3. IConfigurationResolver 接口与实现 🔴 **缺失**

**架构位置:** 策略层（蓝图 §4.B）  
**当前状态:** ❌ 不存在  
**设计规格:**
```csharp
namespace NexusContract.Abstractions.Configuration;

/// <summary>
/// 配置解析器接口：将业务身份映射为物理配置
/// </summary>
public interface IConfigurationResolver
{
    /// <summary>
    /// JIT 解析配置（支持 L1/L2 缓存）
    /// </summary>
    Task<ProviderSettings> ResolveAsync(
        ConfigurationContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Provider 物理配置（含私钥）
/// </summary>
public sealed class ProviderSettings
{
    public required string ProviderName { get; init; }
    public required string AppId { get; init; }
    public required string MerchantId { get; init; }
    public required string PrivateKey { get; init; }
    public required string PublicKey { get; init; }
    public required Uri GatewayUrl { get; init; }
    public Dictionary<string, object>? ExtendedSettings { get; init; }
}
```

**实现清单:**

#### 3.1 HybridConfigResolver（混合解析器） 🔴
- [ ] L1 内存缓存（MemoryCache，TTL 5 分钟）
- [ ] L2 Redis 缓存（可选，TTL 30 分钟）
- [ ] 数据库回源（ITenantRepository）
- [ ] 缓存失效策略（主动刷新 / 被动过期）
- [ ] 配置热更新（无需重启服务）

#### 3.2 ITenantRepository（租户仓储） 🔴
```csharp
public interface ITenantRepository
{
    Task<ProviderSettings?> GetAsync(
        string providerName,
        string realmId,
        string profileId);
    
    Task<bool> UpdateAsync(ProviderSettings settings);
    Task<bool> DeleteAsync(string providerName, string realmId, string profileId);
}
```

- [ ] SQL Server 实现: `SqlServerTenantRepository`
- [ ] PostgreSQL 实现: `PostgresTenantRepository`
- [ ] Redis 实现: `RedisTenantRepository`
- [ ] 内存实现: `InMemoryTenantRepository`（用于测试）

**文件位置:**
- `src/NexusContract.Abstractions/Configuration/IConfigurationResolver.cs`
- `src/NexusContract.Abstractions/Configuration/ProviderSettings.cs`
- `src/NexusContract.Core/Configuration/HybridConfigResolver.cs`
- `src/NexusContract.Infrastructure/Repositories/ITenantRepository.cs`

---

### 4. IProvider 接口标准化 🔴 **缺失**

**架构位置:** Provider 层（蓝图 §4.C）  
**当前状态:** ❌ 不存在（AlipayProvider 是独立实现，无统一接口）  
**设计规格:**
```csharp
namespace NexusContract.Abstractions.Providers;

/// <summary>
/// Provider 接口：无状态单例，动态配置
/// </summary>
public interface IProvider
{
    /// <summary>Provider 标识（如 "Alipay", "WeChat"）</summary>
    string ProviderName { get; }
    
    /// <summary>
    /// 执行请求（由 Engine 调度）
    /// </summary>
    Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        ProviderSettings settings,
        CancellationToken ct = default)
        where TResponse : class, new();
}
```

**实现清单:**

#### 4.1 AlipayProvider 重构 🟡
- [ ] 实现 IProvider 接口
- [ ] 移除构造函数中的静态配置注入
- [ ] 接收 `ProviderSettings` 作为方法参数（而非字段）
- [ ] 保留 NexusGateway 集成（投影/回填）
- [ ] 保留签名逻辑（RSA2）

#### 4.2 新增 WeChatProvider 🔴
- [ ] 实现微信支付 V3 接口
- [ ] 支持服务商模式（SpMchId / SubMchId）
- [ ] 微信签名算法（Wechatpay-Serial / AEAD_AES_256_GCM）
- [ ] 平台证书管理（自动更新）

#### 4.3 新增 UnionPayProvider 🔴（可选）
- [ ] 实现银联支付接口
- [ ] 支持 RSA 签名
- [ ] 支持后台通知验签

**文件位置:**
- `src/NexusContract.Abstractions/Providers/IProvider.cs`
- `src/NexusContract.Providers.Alipay/AlipayProvider.cs`（重构）
- `src/NexusContract.Providers.WeChat/WeChatProvider.cs`（新建）

---

### 5. ConfigurationContext 完善 🟡 **部分存在**

**架构位置:** 核心契约层（蓝图 §3.A）  
**当前状态:** ⚠️ 架构蓝图中有定义，但代码中未找到  
**改进要求:**
- [ ] 添加 `ProviderName` 字段（用于多 Provider 场景）
- [ ] 添加 `Metadata` 字典（扩展属性）
- [ ] 强化构造函数校验（RealmId / ProfileId 非空）
- [ ] 提供 `ToString()` 方法（用于日志）

**文件位置:**
- `src/NexusContract.Abstractions/Configuration/ConfigurationContext.cs`

---

### 6. RoutingContext 与 IUpstreamUrlBuilder 🔴 **缺失**

**架构位置:** 核心契约层（蓝图 §3.B）  
**当前状态:** ❌ 架构蓝图中有定义，但代码中未实现  
**设计规格:**
```csharp
namespace NexusContract.Abstractions.Routing;

public sealed class RoutingContext
{
    public required Uri BaseUrl { get; init; }
    public string? Version { get; init; }
    public Dictionary<string, string>? QueryParams { get; init; }
}

public interface IUpstreamUrlBuilder
{
    /// <summary>
    /// 构建上游 API URL（不接收 ProviderSettings，防止密钥泄露）
    /// </summary>
    Uri Build(string operationId, RoutingContext context);
}
```

**实现清单:**
- [ ] AlipayUrlBuilder（支持 OpenAPI v3 / v1）
- [ ] WeChatUrlBuilder（支持 V2 / V3）
- [ ] 支持沙箱环境切换
- [ ] 支持版本号自动注入

**文件位置:**
- `src/NexusContract.Abstractions/Routing/RoutingContext.cs`
- `src/NexusContract.Abstractions/Routing/IUpstreamUrlBuilder.cs`
- `src/NexusContract.Core/Routing/AlipayUrlBuilder.cs`

---

### 7. NexusEndpointBase 框架基类 🔴 **缺失**

**架构位置:** Ingress 层（蓝图 §4.A）  
**当前状态:** ❌ 不存在（Demo 中有 AlipayEndpointBase，但不是框架级基类）  
**设计规格:**
```csharp
namespace NexusContract.Core.Endpoints;

/// <summary>
/// Zero-Code Endpoint 基类：完全自动化的 HTTP 端点
/// 🔥 关键设计：只需指定 TReq，响应类型自动从 IApiRequest<TResp> 推断
/// </summary>
public abstract class NexusEndpointBase<TReq>(INexusEngine engine) 
    : Endpoint<TReq, TReq.TResponse>
    where TReq : class, IApiRequest<TReq.TResponse>, new()
{
    private readonly INexusEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public override void Configure()
    {
        // 1. 自动提取 [ApiOperation] 元数据
        var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(typeof(TReq));
        if (metadata?.Operation == null)
            throw new InvalidOperationException($"Missing [ApiOperation] on {typeof(TReq).Name}");

        // 2. 自动生成路由（alipay.trade.create → /api/trade/create）
        string route = RouteStrategy.Convert(metadata.Operation.OperationId);
        
        // 3. 根据 HttpVerb 注册路由
        switch (metadata.Operation.Verb)
        {
            case HttpVerb.POST: Post(route); break;
            case HttpVerb.GET: Get(route); break;
            case HttpVerb.PUT: Put(route); break;
            case HttpVerb.DELETE: Delete(route); break;
            default: Post(route); break;
        }
        
        AllowAnonymous();
    }

    public override async Task HandleAsync(TReq req, CancellationToken ct)
    {
        try
        {
            // 2. 自动提取租户上下文
            var tenantCtx = TenantContextFactory.Create(req, HttpContext);

            // 3. 委托给 Engine 调度
            var response = await _engine.ExecuteAsync(req, tenantCtx, ct);
            
            await SendAsync(response, cancellation: ct);
        }
        catch (ContractIncompleteException ex)
        {
            await SendEnvelopeAsync(400, "NXC200", ex.Message, ex.GetDiagnosticData(), ct);
        }
        catch (NexusTenantException ex)
        {
            await SendEnvelopeAsync(403, "TENANT_INVALID", ex.Message, null, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Gateway Error");
            await SendEnvelopeAsync(500, "NXC999", "Internal Server Error", null, ct);
        }
    }
    
    /// <summary>发送标准错误信封</summary>
    private Task SendEnvelopeAsync(int statusCode, string errorCode, string message, 
        IDictionary<string, object>? diagnostics, CancellationToken ct)
    {
        var envelope = new NxcErrorEnvelope
        {
            ErrorCode = errorCode,
            Message = message,
            Diagnostics = diagnostics,
            Timestamp = DateTimeOffset.UtcNow
        };
        return SendAsync(envelope, statusCode, cancellation: ct);
    }
}
```

**实现要求:**
- [ ] 支持 .NET 10 Primary Constructor 语法
- [ ] 自动路由生成（RouteStrategy）
- [ ] 自动租户提取（TenantContextFactory）
- [ ] 统一异常处理（NxcErrorEnvelope）
- [ ] 集成日志记录（ILogger）
- [ ] 性能埋点（OpenTelemetry 兼容）

**文件位置:**
- `src/NexusContract.Core/Endpoints/NexusEndpointBase.cs`
- `src/NexusContract.Core/Utilities/RouteStrategy.cs`

---

### 8. NxcErrorEnvelope 统一错误契约 🔴 **缺失**

**架构位置:** 核心契约层（蓝图 §4.A）  
**当前状态:** ❌ 不存在  
**设计规格:**
```csharp
namespace NexusContract.Abstractions.Contracts;

/// <summary>
/// Nexus 契约错误信封：全局统一的错误格式
/// </summary>
public sealed class NxcErrorEnvelope
{
    /// <summary>错误代码（如 NXC200, TENANT_INVALID）</summary>
    public required string ErrorCode { get; init; }
    
    /// <summary>错误消息</summary>
    public required string Message { get; init; }
    
    /// <summary>诊断数据（用于调试）</summary>
    public IDictionary<string, object>? Diagnostics { get; init; }
    
    /// <summary>时间戳</summary>
    public DateTimeOffset Timestamp { get; init; }
    
    /// <summary>跟踪ID（用于链路追踪）</summary>
    public string? TraceId { get; init; }
}

/// <summary>
/// Nexus 租户异常
/// </summary>
public sealed class NexusTenantException : Exception
{
    public NexusTenantException(string message) : base(message) { }
    public NexusTenantException(string message, Exception inner) : base(message, inner) { }
}
```

**文件位置:**
- `src/NexusContract.Abstractions/Contracts/NxcErrorEnvelope.cs`
- `src/NexusContract.Abstractions/Exceptions/NexusTenantException.cs`

---

## 🟡 第二优先级：基础设施与工具类

### 9. RouteStrategy 路由策略 🔴 **缺失**

**功能:** 将 OperationId 转换为 HTTP 路由  
**示例:** `alipay.trade.create` → `/api/trade/create`

```csharp
public static class RouteStrategy
{
    public static string Convert(string operationId)
    {
        // 移除 provider 前缀
        var parts = operationId.Split('.');
        if (parts.Length < 2) return $"/api/{operationId}";
        
        // alipay.trade.create → trade/create
        var path = string.Join('/', parts.Skip(1));
        return $"/api/{path}";
    }
}
```

**文件位置:** `src/NexusContract.Core/Utilities/RouteStrategy.cs`

---

### 10. YARP Transport 集成 🔴 **缺失**

**架构位置:** Egress 层（蓝图 §2）  
**当前状态:** ❌ 不存在（架构蓝图提到但未实现）  
**功能:**
- [ ] HTTP/2 连接池
- [ ] 自动重试（Polly 集成）
- [ ] 熔断器（Circuit Breaker）
- [ ] 负载均衡（支持多个上游地址）
- [ ] 请求/响应日志

**文件位置:**
- `src/NexusContract.Transport.Yarp/YarpTransport.cs`
- `src/NexusContract.Transport.Yarp/YarpTransportOptions.cs`

---

### 11. OpenTelemetry 集成 🔴 **可选**

**功能:**
- [ ] 分布式追踪（Trace）
- [ ] 性能指标（Metrics）
- [ ] 日志关联（Logs）

**埋点位置:**
- Engine 调度（OperationId, TenantId）
- Provider 调用（Duration, Success/Failure）
- 签名耗时（RSA 计算）

---

## 🟢 第三优先级：文档与示例

### 12. 完善 README.md 🟡 **部分完成**

**待补充内容:**
- [ ] v1.2 ISV 多租户特性说明
- [ ] 配置解析器使用示例
- [ ] YARP Transport 集成指南
- [ ] 性能基准测试结果

---

### 13. 完善 IMPLEMENTATION.md 🟡 **部分完成**

**待补充章节:**
- [ ] ISV 多商户接入指南
- [ ] 配置热更新机制
- [ ] 租户上下文提取策略
- [ ] YARP 传输层配置

---

### 14. 新增 MIGRATION_GUIDE.md 🔴 **缺失**

**内容:**
- [ ] 从 v1.0 升级到 v1.2 的迁移指南
- [ ] AlipayProvider 重构说明
- [ ] Endpoint 基类变更
- [ ] 配置文件格式调整

---

### 15. 新增 ISV_COOKBOOK.md 🔴 **缺失**

**内容:**
- [ ] ISV 服务商架构模式
- [ ] 动态商户接入流程
- [ ] 配置管理最佳实践
- [ ] 安全隔离策略

---

## 🧪 第四优先级：测试与质量保证

### 16. 单元测试覆盖率 🟡 **部分完成**

**待补充测试:**
- [ ] INexusEngine 调度逻辑测试
- [ ] TenantContextFactory 提取逻辑测试
- [ ] HybridConfigResolver 缓存策略测试
- [ ] RouteStrategy 路由转换测试
- [ ] NexusEndpointBase 端到端测试

**目标覆盖率:** ≥ 80%

---

### 17. 集成测试 🔴 **缺失**

**测试场景:**
- [ ] 多租户并发调用（100 TPS）
- [ ] 配置热更新验证
- [ ] 缓存失效与重建
- [ ] Provider 故障回退

---

### 18. 性能基准测试 🔴 **缺失**

**测试指标:**
- [ ] 冷启动延迟（首次请求）
- [ ] 热路径延迟（缓存命中）
- [ ] 内存占用（多租户场景）
- [ ] GC 压力（高并发场景）

**工具:** BenchmarkDotNet（已集成）

---

## 📦 第五优先级：包发布与部署

### 19. NuGet 包版本规划 🔴 **待完成**

**包列表:**
- `NexusContract.Abstractions` v1.2.0
- `NexusContract.Core` v1.2.0
- `NexusContract.Client` v1.2.0
- `NexusContract.Providers.Alipay` v1.2.0
- `NexusContract.Providers.WeChat` v1.2.0（新增）
- `NexusContract.Transport.Yarp` v1.2.0（新增）

---

### 20. CI/CD Pipeline 🟡 **部分完成**

**待完善:**
- [ ] 自动化版本号管理
- [ ] NuGet 包自动发布
- [ ] 多目标框架测试（.NET 10, .NET Standard 2.0）
- [ ] 代码覆盖率报告

---

## 🔧 技术债务清单

### 21. AlipayProvider 重构 🟡 **高优先级**

**现有问题:**
- 构造函数注入静态配置（违背 v1.2 无状态设计）
- 未实现 IProvider 接口
- 签名逻辑与配置耦合

**重构目标:**
- 实现 IProvider 接口
- 接收 ProviderSettings 作为方法参数
- 保留 NexusGateway 集成

---

### 22. AlipayEndpointBase 迁移 🟡 **高优先级**

**现有问题:**
- Demo 项目中的实现，非框架级基类
- 使用反射提取响应类型（性能损失）
- 未集成租户上下文提取

**迁移目标:**
- 迁移到 NexusContract.Core
- 改名为 NexusEndpointBase
- 集成 INexusEngine 和 TenantContextFactory

---

### 23. 配置管理统一化 🔴 **中优先级**

**现有问题:**
- AlipayProviderConfig 是独立类
- 缺少通用的 ProviderSettings 抽象
- 配置来源单一（构造函数注入）

**改进目标:**
- 定义 ProviderSettings 基类
- 支持多种配置源（数据库、Redis、配置文件）
- 实现配置热更新

---

### 24. 错误处理标准化 🔴 **中优先级**

**现有问题:**
- 缺少 NxcErrorEnvelope 统一错误格式
- 异常处理分散在各层
- 诊断信息不完整

**改进目标:**
- 定义 NxcErrorEnvelope
- 在 NexusEndpointBase 中统一异常捕获
- 集成诊断代码（NXC200, NXC999...）

---

## 📋 实施路径建议

### 阶段 1：核心架构搭建（2-3 周）
1. 实现 TenantContext + TenantContextFactory
2. 定义 IProvider 接口
3. 实现 INexusEngine + 基础调度逻辑
4. 定义 IConfigurationResolver 接口
5. 实现 InMemoryConfigResolver（用于测试）

### 阶段 2：Ingress 层实现（1-2 周）
1. 实现 NexusEndpointBase 框架基类
2. 实现 RouteStrategy 路由转换
3. 定义 NxcErrorEnvelope 错误契约
4. 重构 Demo 项目使用新基类

### 阶段 3：Provider 层重构（2 周）
1. 重构 AlipayProvider 实现 IProvider
2. 实现 HybridConfigResolver（L1/L2 缓存）
3. 实现 ITenantRepository SQL 版本
4. 实现 WeChatProvider（可选）

### 阶段 4：基础设施补全（1-2 周）
1. 实现 RoutingContext + IUpstreamUrlBuilder
2. 集成 YARP Transport（可选）
3. 添加 OpenTelemetry 埋点（可选）

### 阶段 5：测试与文档（1-2 周）
1. 补充单元测试（目标 80% 覆盖率）
2. 添加集成测试
3. 完善文档（README, IMPLEMENTATION, MIGRATION_GUIDE）
4. 性能基准测试

### 阶段 6：发布准备（1 周）
1. 版本号管理
2. NuGet 包发布
3. Release Notes 编写
4. 示例项目更新

---

## 🎯 关键里程碑

- [ ] **M1 (Week 3):** 核心架构完成，可运行 Demo
- [ ] **M2 (Week 5):** Ingress 层完成，支持 Zero-Code Endpoint
- [ ] **M3 (Week 7):** Provider 重构完成，支持多租户
- [ ] **M4 (Week 9):** 基础设施完成，集成 YARP
- [ ] **M5 (Week 11):** 测试与文档完成
- [ ] **M6 (Week 12):** v1.2.0 正式发布

---

## 📊 风险评估

| 风险项 | 影响 | 可能性 | 缓解措施 |
|--------|------|--------|----------|
| **AlipayProvider 重构破坏现有功能** | 高 | 中 | 保留原实现，并行开发新版本 |
| **YARP 集成复杂度超预期** | 中 | 高 | 降级为可选功能，使用 HttpClient |
| **多租户配置性能问题** | 高 | 中 | 提前进行压力测试，优化缓存策略 |
| **WeChatProvider 实现延期** | 低 | 中 | 标记为可选功能，优先保证 Alipay |

---

## 🔗 相关文档

- [架构蓝图 v1.2（中文）](./ARCHITECTURE_BLUEPRINT.zh-CN.md)
- [架构蓝图 v1.2（英文）](./ARCHITECTURE_BLUEPRINT.md)
- [实施手册](./IMPLEMENTATION.md)
- [宪法文档](../src/NexusContract.Abstractions/CONSTITUTION.md)

---

**最后更新:** 2026-01-10  
**维护者:** NexusContract Team
