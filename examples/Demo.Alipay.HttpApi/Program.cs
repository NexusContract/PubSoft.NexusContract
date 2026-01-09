// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Linq;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexusContract.Providers.Alipay;
using NexusContract.Providers.Alipay.ServiceConfiguration;
using NexusContract.Core.Reflection;
using NexusContract.Abstractions.Attributes;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ==================== 步骤1：注册支付宝提供商 ====================
builder.Services.AddAlipayProvider(new AlipayProviderConfig
{
    AppId = builder.Configuration["Alipay:AppId"] ?? "2021...",
    MerchantId = builder.Configuration["Alipay:MerchantId"] ?? "2088...",
    PrivateKey = builder.Configuration["Alipay:PrivateKey"] ?? "MIIEvQIBA...",
    AlipayPublicKey = builder.Configuration["Alipay:AlipayPublicKey"] ?? "MIIBIjANBgkqh...",
    ApiGateway = new Uri("https://openapi.alipay.com/"),
    UseSandbox = builder.Configuration.GetValue<bool>("Alipay:UseSandbox"),
    RequestTimeout = TimeSpan.FromSeconds(30)
});

// ==================== 步骤2：注册FastEndpoints ====================
builder.Services.AddFastEndpoints();

var app = builder.Build();

// ==================== 步骤3：配置中间件 ====================
app.UseFastEndpoints(config =>
{
    config.Endpoints.RoutePrefix = "v3/alipay";
});

// ==================== 步骤4：测试端点 ====================
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

// ==================== 步骤5：启动期契约健康检查（Fail-Fast + 全量诊断）====================
// 【决策 A-307】无损全景扫描：启动期批量预加载并输出完整诊断报告
Console.WriteLine("========================================");
Console.WriteLine("NexusContract 启动期契约健康检查");
Console.WriteLine("========================================");

try
{
    // ✅ 新方式：使用 ContractStartupHealthCheck（一次性全量诊断）
    var report = NexusContract.Core.Diagnostics.ContractStartupHealthCheck.Run(
        assemblies: new[] { typeof(Program).Assembly },
        warmup: true,           // 预热投影器（推荐生产启用）
        throwOnError: true      // 发现错误时抛出 ContractIncompleteException（Fail-Fast）
    );

    // 如果没有抛出异常，说明所有契约都通过验证
    Console.WriteLine($"\n✅ 契约健康检查通过：{report.SuccessCount} 个契约已验证");
    
    // 可选：输出 JSON 报告（用于 CI/CD 集成）
    if (builder.Environment.IsDevelopment())
    {
        var jsonReport = NexusContract.Core.Diagnostics.ContractStartupHealthCheck.GenerateJsonReport(
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
    // ✅ 结构化异常处理
    Console.Error.WriteLine($"\n❌ 契约验证失败：");
    Console.Error.WriteLine($"   失败契约数：{ex.FailedContractCount}");
    Console.Error.WriteLine($"   错误总数：{ex.ErrorCount}（{ex.CriticalCount} 个致命错误）");
    Console.Error.WriteLine();
    
    // 输出详细报告
    ex.Report.PrintToConsole(includeDetails: true);
    
    // 保存 JSON 报告
    var jsonReport = NexusContract.Core.Diagnostics.ContractStartupHealthCheck.GenerateJsonReport(
        ex.Report,
        appId: "Demo.Alipay.HttpApi",
        environment: builder.Environment.EnvironmentName
    );
    System.IO.File.WriteAllText("contract-errors.json", jsonReport);
    Console.Error.WriteLine("\n📄 详细报告已保存到: contract-errors.json");
    
    // 阻断启动
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
 * 支付宝API使用示例（契约驱动 - OpenAPI v3）
 * 
 * 架构说明：
 * 1. 客户端调用：FastEndpoints REST 风格（POST /v3/alipay/trade/pay）
 * 2. AlipayProvider 转发到：支付宝 OpenAPI v3（https://openapi.alipay.com/v3/alipay/trade/pay）
 * 3. 支付宝网关处理并返回结果
 * 
 * 1. 交易支付接口
 * POST /v3/alipay/trade/pay
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
 * 2. 交易创建接口
 * POST /v3/alipay/trade/create
 * Content-Type: application/json
 * 
 * {
 *   "merchantOrderNo": "2024002",
 *   "totalAmount": 88.88,
 *   "subject": "测试订单2",
 *   "buyerId": "2088..."
 * }
 * 
 * 3. 交易查询接口
 * POST /v3/alipay/trade/query
 * Content-Type: application/json
 * 
 * {
 *   "merchantOrderNo": "2024001"
 * }
 * 
 * 路由规则：
 * - Contract中定义: [ApiOperation("alipay.trade.pay")]
 * - 自动转换为FastEndpoints路由: /v3/alipay/trade/pay
 * - AlipayProvider调用支付宝 OpenAPI v3: https://openapi.alipay.com/v3/alipay/trade/pay
 */
