// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastEndpoints;
using NexusContract.Abstractions.Attributes;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Core;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Core.Reflection;

namespace Demo.Alipay.HttpApi.Endpoints;

/// <summary>
/// 支付宝端点基类 - ISV多租户架构（宪法版）
/// 
/// 架构流程：
/// 1. FastEndpoints 接收 HTTP 请求
/// 2. 从 URL 路由强制提取 profileId 和 providerName（宪法 002）
/// 3. 调用 INexusEngine.ExecuteAsync(request, providerName, profileId, ct)
/// 4. Engine 自动路由到对应 IProvider（如 AlipayProviderAdapter）
/// 5. Provider 通过 INexusTransport 调用第三方 API
/// 
/// 设计约束（宪法）：
/// - 宪法 002（URL 资源寻址）：profileId 必须来自 URL 路由，是绝对权威
/// - 宪法 004（职责分离）：本类只负责路由识别，不参与身份转换
/// - 宪法 012（诊断先行）：参数缺失时抛出 NXC201 结构化错误
/// 
/// 路由示例：
/// - OperationId: "alipay.trade.create"
/// - HTTP 路由: POST /alipay/{profileId}/trade/create
/// 
/// 性能优化：
/// - 类级别静态缓存：每个 TRequest 类型的响应类型只解析一次
/// - Engine内部：HybridConfigResolver（L1/L2/L3缓存）
/// - Provider内部：AlipayProviderAdapter 缓存轻量级配置对象
/// </summary>
public abstract class AlipayEndpointBase<TRequest>(INexusEngine engine) : Endpoint<TRequest>
    where TRequest : class, IApiRequest
{
    private readonly INexusEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    /// <summary>
    /// 类级别缓存：TRequest 的响应类型（每个泛型实例化只计算一次）
    /// </summary>
    private static readonly Type CachedResponseType = ExtractResponseType();

    /// <summary>
    /// 从 TRequest 提取响应类型（启动时执行一次）
    /// </summary>
    private static Type ExtractResponseType()
    {
        Type apiRequestInterface = typeof(TRequest)
            .GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IApiRequest<>));

        return apiRequestInterface.GetGenericArguments()[0];
    }

    public override void Configure()
    {
        var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(typeof(TRequest));

        if (metadata.Operation == null)
        {
            throw new InvalidOperationException(
                $"Contract '{typeof(TRequest).Name}' must have [ApiOperation] attribute.");
        }

        // 宪法 002：强制路由模式 /{provider}/{profileId}/{operation}
        // 转换路由：alipay.trade.create → trade/create
        string methodName = metadata.Operation.OperationId;
        string route = methodName.StartsWith("alipay.", StringComparison.OrdinalIgnoreCase)
            ? methodName.Substring("alipay.".Length).Replace('.', '/')
            : methodName.Replace('.', '/');

        // 完整路由：/{provider}/{profileId}/{operation}
        string fullRoute = $"{{provider}}/{{profileId}}/{route}";

        // 根据HttpVerb配置路由
        switch (metadata.Operation.Verb)
        {
            case HttpVerb.POST:
                Post(fullRoute);
                break;
            case HttpVerb.GET:
                Get(fullRoute);
                break;
            case HttpVerb.PUT:
                Put(fullRoute);
                break;
            case HttpVerb.DELETE:
                Delete(fullRoute);
                break;
            default:
                Post(fullRoute);
                break;
        }

        AllowAnonymous();
    }

    /// <inheritdoc />
    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        // 步骤1：物理寻址提取（宪法 002）
        // 路由不匹配由 FastEndpoints 框架返回 404，逻辑层只负责提取
        var provider = Route<string>("provider");
        var profileId = Route<string>("profileId");
        
        // 物理寻址卫哨：确保两个参数都完整（失败则抛出 NXC201）
        NexusGuard.EnsurePhysicalAddress(provider, profileId, nameof(AlipayEndpointBase<TRequest>));

        // 步骤2：调用 Engine 执行请求（自动路由到 AlipayProviderAdapter）
        var executeMethod = typeof(INexusEngine)
            .GetMethod(nameof(INexusEngine.ExecuteAsync))!
            .MakeGenericMethod(CachedResponseType);

        var responseTask = (Task)executeMethod.Invoke(_engine, new object[] { req, provider, profileId, ct })!;
        await responseTask.ConfigureAwait(false);

        // 步骤3：提取结果并写入响应
        object? result = responseTask.GetType().GetProperty("Result")!.GetValue(responseTask);

        HttpContext.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(HttpContext.Response.Body, result, CachedResponseType, cancellationToken: ct);
    }
}
