// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using FastEndpoints;
using NexusContract.Abstractions;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Core;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Hosting.Endpoints;

/// <summary>
/// 宪法版 NexusEndpoint：强制 URL 寻址，物理抹除所有身份猜测。
/// 
/// 职责：
/// 1. 从 URL 路由提取 provider 和 profileId（宪法 002 - URL 资源寻址）
/// 2. 自动调用 NexusEngine 调度执行
/// 3. 如果缺少关键参数，直接抛出 NXC201 诊断异常（宪法 012）
/// 4. 自动推断响应类型（从 IApiRequest&lt;TResponse&gt;）
/// 
/// 设计原则：
/// - 宪法 002（URL 资源寻址）：profileId 必须从 URL 路由提取，是绝对权威
/// - 宪法 004（职责分离）：本类只是"参数搬运工"，不参与身份转换
/// - 宪法 007（性能优先）：消除所有中间 Context 对象，直接传递字符串
/// - 宪法 012（诊断先行）：参数缺失时抛出 NXC201 结构化错误码
/// 
/// 使用示例（真正的一行代码）：
/// <code>
/// public class TradeCreateEndpoint(INexusEngine engine) : NexusEndpoint&lt;TradeCreateRequest&gt;(engine)
/// {
/// }
/// </code>
/// 
/// 路由示例（由元数据注册中心生成）：
/// - OperationId: "alipay.trade.create"
/// - HTTP 路由: POST /alipay/{profileId}/trade/create
/// </summary>
public abstract class NexusEndpoint<TRequest, TResponse>(INexusEngine engine)
    : Endpoint<TRequest, TResponse>
    where TRequest : IApiRequest<TResponse>, new()
    where TResponse : class, new()
{
    /// <summary>
    /// 配置 Endpoint（自动路由）
    /// 
    /// 宪法 002：强制路由模式 /{provider}/{profileId}/{operation}
    /// 这里的路由应由元数据注册中心动态生成，暂以占位形式表达物理约束。
    /// </summary>
    public override void Configure()
    {
        Post("/api/{provider}/{profileId}/" + typeof(TRequest).Name.ToLower());
        AllowAnonymous();
    }

    /// <summary>
    /// 处理请求（Zero-Code 核心逻辑）
    /// 
    /// 流程：
    /// 1. 物理寻址提取 (宪法 002) - 路径不匹配由框架返回 404，逻辑层只负责提取
    /// 2. 调度执行 (宪法 004/008)
    /// 3. 响应回填 (宪法 008)
    /// </summary>
    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        // 1. 物理寻址提取 (宪法 002) - 路径不匹配直接由框架返回 404，
        // 逻辑层只负责提取。若缺少关键参数，直接抛出 NXC 结构化诊断码。
        string? provider = Route<string>("provider");
        string? profileId = Route<string>("profileId");

        // 物理寻址卫哨：确保两个参数都完整（失败则抛出 NXC201）
        NexusGuard.EnsurePhysicalAddress(provider, profileId, nameof(NexusEndpoint<TRequest, TResponse>));

        // 2. 调度执行 (宪法 004/008)
        // 抛弃 ITenantIdentity，直接传递物理字符串。
        var response = await engine.ExecuteAsync(req, provider, profileId, ct);

        // 3. 响应回填 (宪法 008) - 序列化响应并发送
        HttpContext.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(HttpContext.Response.Body, response, typeof(TResponse), cancellationToken: ct);
    }
}

/// <summary>
/// NexusEndpoint 简化版：自动推断 EmptyResponse
/// 
/// 使用示例（推荐）：
/// <code>
/// public class TradeCreateEndpoint(INexusEngine engine) : NexusEndpoint&lt;TradeCreateRequest&gt;(engine)
/// {
/// }
/// </code>
/// </summary>
public abstract class NexusEndpoint<TRequest>(INexusEngine engine)
    : NexusEndpoint<TRequest, Abstractions.EmptyResponse>(engine)
    where TRequest : IApiRequest<Abstractions.EmptyResponse>, new();

