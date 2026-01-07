// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Linq;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

// ==================== 步骤5：启动期契约体检示例 ====================
// 【决策 A-307】无损全景扫描：启动期批量预加载并输出完整诊断报告
Console.WriteLine("========================================");
Console.WriteLine("NexusContract 启动期契约体检");
Console.WriteLine("========================================");

try
{
    var types = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a =>
        {
            try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
        })
        .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttributes(typeof(ApiOperationAttribute), inherit: false).Any())
        .ToArray();

    Console.WriteLine($"扫描到 {types.Length} 个契约类型，开始体检...\n");

    var report = NexusContractMetadataRegistry.Instance.Preload(types, warmup: true);

    // 1. 打印人类可读的报告（带颜色）
    report.PrintToConsole(includeDetails: true);

    // 2. 可选：序列化为 JSON（便于 CI/CD 集成）
    string json = JsonSerializer.Serialize(report.Diagnostics, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine("\n[JSON 诊断输出] (可用于 CI/CD):");
    Console.WriteLine(json);

    // 3. 检查致命错误
    if (report.HasCriticalErrors)
    {
        Console.Error.WriteLine("\n❌ 检测到致命的契约错误，系统即将中止启动。");
        Console.Error.WriteLine("请修复以上 [Critical] 或 [Error] 标记的问题后重试。");
        Environment.Exit(1);
    }

    Console.WriteLine("\n✅ 所有契约均已通过体检，系统启动成功。");
    Console.WriteLine("========================================\n");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\n❌ Preload 执行失败: {ex.Message}");
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
