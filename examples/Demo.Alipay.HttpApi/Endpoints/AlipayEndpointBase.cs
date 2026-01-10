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
using NexusContract.Core.Reflection;
using NexusContract.Hosting.Factories;

namespace Demo.Alipay.HttpApi.Endpoints
{
    /// <summary>
    /// 支付宝端点基类 - ISV多租户架构（使用 NexusEngine）
    /// 
    /// 架构流程：
    /// 1. FastEndpoints 接收 HTTP 请求
    /// 2. 从 HttpContext 提取租户身份（TenantContextFactory）
    /// 3. 调用 INexusEngine.ExecuteAsync(request, identity, ct)
    /// 4. Engine 自动路由到对应 IProvider（如 AlipayProviderAdapter）
    /// 5. Provider 通过 INexusTransport 调用第三方 API
    /// 
    /// 租户标识（优先级顺序）：
    /// - HTTP Header: X-Tenant-Id
    /// - Query参数: ?tenantId=xxx
    /// - JWT Claim: "sub" claim
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

            // 转换路由：alipay.trade.create → trade/create
            string methodName = metadata.Operation.OperationId;
            string route = methodName.StartsWith("alipay.", StringComparison.OrdinalIgnoreCase)
                ? methodName.Substring("alipay.".Length).Replace('.', '/')
                : methodName.Replace('.', '/');

            // 根据HttpVerb配置路由
            switch (metadata.Operation.Verb)
            {
                case HttpVerb.POST:
                    Post(route);
                    break;
                case HttpVerb.GET:
                    Get(route);
                    break;
                case HttpVerb.PUT:
                    Put(route);
                    break;
                case HttpVerb.DELETE:
                    Delete(route);
                    break;
                default:
                    Post(route);
                    break;
            }

            AllowAnonymous();
        }

        /// <inheritdoc />
        public override async Task HandleAsync(TRequest req, CancellationToken ct)
        {
            // 步骤1：从 HttpContext 提取租户身份
            var identity = await TenantContextFactory.CreateAsync(HttpContext);

            // 步骤2：调用 Engine 执行请求（自动路由到 AlipayProviderAdapter）
            var executeMethod = typeof(INexusEngine)
                .GetMethod(nameof(INexusEngine.ExecuteAsync))!
                .MakeGenericMethod(CachedResponseType);

            var responseTask = (Task)executeMethod.Invoke(_engine, new object[] { req, identity, ct })!;
            await responseTask.ConfigureAwait(false);

            // 步骤3：提取结果并写入响应
            object? result = responseTask.GetType().GetProperty("Result")!.GetValue(responseTask);

            HttpContext.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(HttpContext.Response.Body, result, CachedResponseType, cancellationToken: ct);
        }
    }
}
