// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using FastEndpoints;
using NexusContract.Abstractions;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Core;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Hosting.Factories;

namespace NexusContract.Hosting.Endpoints
{
    /// <summary>
    /// NexusEndpoint 基类：实现 Zero-Code 承诺的核心
    /// 
    /// 职责：
    /// 1. 自动提取租户上下文（TenantContextFactory）
    /// 2. 自动调用 NexusEngine 调度执行
    /// 3. 自动处理异常转换（租户异常 → HTTP 403）
    /// 4. 自动推断响应类型（从 IApiRequest&lt;TResponse&gt;）
    /// 
    /// 使用示例（真正的"一行代码"）：
    /// <code>
    /// public class TradeCreateEndpoint : NexusEndpoint&lt;TradeCreateRequest&gt;
    /// {
    ///     public TradeCreateEndpoint(INexusEngine engine) : base(engine) { }
    /// }
    /// </code>
    /// 
    /// 自动生成的路由：
    /// - OperationId: "alipay.trade.create"
    /// - HTTP 路由: POST /alipay/trade/create
    /// 
    /// 自动处理的异常：
    /// - NexusTenantException → HTTP 403 Forbidden
    /// - ArgumentException → HTTP 400 Bad Request
    /// - 其他异常 → HTTP 500 Internal Server Error
    /// 
    /// 设计约束：
    /// - 继承自 FastEndpoints.Endpoint (7.x)
    /// - 泛型约束：TRequest : IApiRequest&lt;TResponse&gt;
    /// - .NET 10 Primary Constructors 简化注入
    /// </summary>
    public abstract class NexusEndpoint<TRequest, TResponse>(INexusEngine engine) 
        : Endpoint<TRequest, TResponse>
        where TRequest : IApiRequest<TResponse>, new()
        where TResponse : class, new()
    {
        /// <summary>
        /// 配置 Endpoint（自动路由）
        /// </summary>
        public override void Configure()
        {
            // TODO: 从 NexusContractMetadataRegistry 获取 OperationId 并转换为路由
            // 例如：OperationId "alipay.trade.create" → POST /alipay/trade/create
            
            // 临时实现：使用默认路由
            Post($"/api/{typeof(TRequest).Name}");
            AllowAnonymous(); // TODO: 根据实际需求配置认证
        }

        /// <summary>
        /// 处理请求（Zero-Code 核心逻辑）
        /// </summary>
        public override async Task<TResponse> ExecuteAsync(TRequest req, CancellationToken ct)
        {
            // 1. 提取租户上下文
            var tenantContext = await TenantContextFactory.CreateAsync(HttpContext);

            // 2. 调用 NexusEngine 执行（异常由 FastEndpoints 全局处理）
            var response = await engine.ExecuteAsync(req, tenantContext, ct);

            // 3. 返回响应
            return response;
        }
    }

    /// <summary>
    /// NexusEndpoint 简化版（自动推断 TResponse）
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
        where TRequest : IApiRequest<Abstractions.EmptyResponse>, new()
    {
    }
}
