using System;
using System.Linq;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using NexusContract.Abstractions.Security;
using NexusContract.Abstractions.Configuration;
using NexusContract.Abstractions.Transport;
using NexusContract.Abstractions.Core;
using NexusContract.Core;
using NexusContract.Core.Engine;
using NexusContract.Hosting.Security;
using NexusContract.Hosting.Configuration;
using NexusContract.Hosting.Yarp;
using NexusContract.Providers.Alipay;
using NexusContract.Core.Reflection;
using System.Text.Json;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);

// ==================== 步骤1：注册 Redis（L2缓存 + 跨实例失效通知） ====================
string redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// ==================== 步骤2：注册敏感数据保护器（AES加密/解密配置敏感信息） ====================
string masterKey = builder.Configuration["Security:MasterKey"] ?? "DEMO-MASTER-KEY-32-BYTES-LONG!"; // 生产环境必须从安全存储加载
var secretProtector = new AesSecurityProvider(masterKey);
builder.Services.AddSingleton<ISecretProtector>(secretProtector);

// ==================== 步骤3：注册配置解析器（L1 MemoryCache + L2 Redis + L3 Database） ====================
var memoryCache = new MemoryCache(new MemoryCacheOptions());
// register memory cache with DI so other services (e.g., resolver) can consume it
builder.Services.AddSingleton<IMemoryCache>(memoryCache);

// register IConfigurationResolver via DI factory so ILogger can be injected
builder.Services.AddSingleton<IConfigurationResolver>(sp =>
    new HybridConfigResolver(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<ISecretProtector>(),
        sp.GetRequiredService<ILogger<HybridConfigResolver>>(),
        redisKeyPrefix: null,
        l1Ttl: TimeSpan.FromMinutes(5),
        l2Ttl: TimeSpan.FromMinutes(30)
    ));

// ==================== 步骤4：注册 YARP HTTP/2 传输层（带重试+熔断器） ====================
builder.Services.AddNexusYarpTransport(options =>
{
    options.RequestTimeout = TimeSpan.FromSeconds(30);
    options.RetryCount = 3;
    options.CircuitBreakerFailureThreshold = 5;
});

// ==================== 步骤5：注册 NexusEngine（ISV多租户调度引擎） ====================
builder.Services.AddSingleton<INexusEngine>(sp =>
{
    var transport = sp.GetRequiredService<INexusTransport>();
    var gateway = new NexusGateway(new NexusContract.Core.Policies.Impl.SnakeCaseNamingPolicy());
    var configResolver = sp.GetRequiredService<IConfigurationResolver>();
    var engine = new NexusEngine(configResolver);

    // 注册支付宝提供商适配器（桥接 IProvider → AlipayProvider）
    var alipayAdapter = new AlipayProviderAdapter(transport, gateway);
    engine.RegisterProvider("Alipay", alipayAdapter);

    return engine;
});

// ==================== 步骤6：注册FastEndpoints ====================
builder.Services.AddFastEndpoints();

var app = builder.Build();

// ==================== 步骤7：传输层预热（HTTP/2连接池初始化） ====================
var transport = app.Services.GetRequiredService<INexusTransport>();
await transport.WarmupAsync(new[] { "https://openapi.alipay.com" }, CancellationToken.None);

// ==================== 步骤8：配置中间件 ====================
app.UseFastEndpoints(config =>
{
    config.Endpoints.RoutePrefix = "v3/alipay";
});

// ==================== 步骤9：测试端点 ====================
app.MapGet("/health", () => new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    architecture = "ISV Multi-Tenant (NexusEngine)",
    providers = new[] { "Alipay" }!
});

// ==================== 步骤10：启动期契约健康检查（Fail-Fast + 全量诊断）====================
Console.WriteLine("========================================");
Console.WriteLine("NexusContract 启动期契约健康检查");
Console.WriteLine("========================================");

try
{
    var report = NexusContract.Core.Diagnostics.ContractStartupHealthCheck.Run(
        assemblies: new[] { typeof(Program).Assembly },
        warmup: true,
        throwOnError: true
    );

    Console.WriteLine($"\n✅ 契约健康检查通过：{report.SuccessCount} 个契约已验证");

    if (builder.Environment.IsDevelopment())
    {
        string jsonReport = NexusContract.Core.Diagnostics.ContractStartupHealthCheck.GenerateJsonReport(
            report,
            appId: "Demo.Alipay.HttpApi",
            environment: builder.Environment.EnvironmentName
        );
        Console.WriteLine("\n[JSON 诊断报告]:");
        Console.WriteLine(jsonReport);
    }

    Console.WriteLine("========================================\n");
}
catch (NexusContract.Core.Exceptions.ContractIncompleteException ex)
{
    Console.Error.WriteLine($"\n❌ 契约验证失败：");
    Console.Error.WriteLine($"   失败契约数：{ex.FailedContractCount}");
    Console.Error.WriteLine($"   错误总数：{ex.ErrorCount}（{ex.CriticalCount} 个致命错误）");
    Console.Error.WriteLine();

    ex.Report.PrintToConsole(includeDetails: true);

    string jsonReport = NexusContract.Core.Diagnostics.ContractStartupHealthCheck.GenerateJsonReport(
        ex.Report,
        appId: "Demo.Alipay.HttpApi",
        environment: builder.Environment.EnvironmentName
    );
    if (!string.IsNullOrEmpty(jsonReport))
    {
        System.IO.File.WriteAllText("contract-errors.json", jsonReport);
        Console.Error.WriteLine("\n📄 详细报告已保存到: contract-errors.json");
    }

    Console.Error.WriteLine("\n❌ 系统启动已阻断，请修复上述错误后重试。");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\n❌ 启动检查失败: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    Environment.Exit(2);
}

app.Run();

/*
 * 支付宝API ISV多租户架构使用示例
 * 
 * 架构流程：
 * 1. HTTP请求 → FastEndpoints → 显式提取 profileId 和 providerName（禁止隐式容器）
 * 2. 参数传递 → INexusEngine.ExecuteAsync(request, providerName, profileId, ct)
 * 3. Engine查询 IConfigurationResolver → L1/L2/L3 加载租户配置
 * 4. IProvider.ExecuteAsync(request, config, ct) → AlipayProviderAdapter
 * 5. Adapter缓存配置 → 调用 AlipayProvider.ExecuteAsync(request, ct)
 * 6. INexusTransport(YARP) → HTTP/2 + Retry + Circuit Breaker
 * 7. 支付宝 OpenAPI v3 → 返回响应
 * 
 * 参数提取方式（优先级顺序）：
 * - profileId：URL路由参数（{profileId}，绝对权威）> X-Profile-Id Header > ?profileId=xxx 查询参数
 * - providerName：X-Provider-Name Header > ?provider=xxx 查询参数
 * 
 * 示例请求：
 * POST /v3/alipay/{profileId}/trade/pay
 * X-Provider-Name: Alipay
 * Content-Type: application/json
 * 
 * {
 *   "merchantOrderNo": "2024001",
 *   "totalAmount": 100.00,
 *   "subject": "测试订单",
 *   "scene": "bar_code",
 *   "authCode": "285015833990941919"
 * }
 * 
 * 配置存储（HybridConfigResolver）：
 * - L1: MemoryCache（5分钟TTL，进程内）
 * - L2: Redis（30分钟TTL，跨实例共享 + Pub/Sub失效通知）
 * - L3: Database（ITenantRepository接口，TODO待实现）
 * 
 * 路由规则：
 * - Contract定义: [ApiOperation("alipay.trade.pay")]
 * - FastEndpoints路由: POST /v3/alipay/{profileId}/trade/pay
 * - OpenAPI v3调用: POST https://openapi.alipay.com/v3/alipay/trade/pay
 */
