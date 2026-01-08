// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastEndpoints;
using NexusContract.Abstractions.Attributes;
using NexusContract.Abstractions.Contracts;
using NexusContract.Providers.Alipay;
using NexusContract.Core.Reflection;

namespace Demo.Alipay.HttpApi.Endpoints
{
    /// <summary>
    /// 支付宝端点基类 - 契约驱动的自动化实现
    /// 
    /// 特性：
    /// 1. 从Contract的[ApiOperation]自动提取路由
    /// 2. 从Contract的IApiRequest&lt;TResponse&gt;自动推断响应类型
    /// 3. 自动调用AlipayProvider执行请求
    /// 4. 子类零代码实现，仅需类名声明
    /// 
    /// 性能优化：
    /// - 类级别静态缓存：每个 TRequest 类型的响应类型和 MethodInfo 只解析一次
    /// - 比字典查询更快（直接访问静态字段，零哈希计算）
    /// </summary>
    public abstract class AlipayEndpointBase<TRequest>(AlipayProvider alipayProvider) : Endpoint<TRequest>
        where TRequest : class, IApiRequest
    {
        private readonly AlipayProvider _alipayProvider = alipayProvider ?? throw new ArgumentNullException(nameof(alipayProvider));

        /// <summary>
        /// 类级别缓存：TRequest 的响应类型（每个泛型实例化只计算一次）
        /// 例如：AlipayEndpointBase&lt;TradePayRequest&gt; 和 AlipayEndpointBase&lt;TradeRefundRequest&gt; 各有独立的静态字段
        /// </summary>
        private static readonly Type CachedResponseType = ExtractResponseType();

        /// <summary>
        /// 类级别缓存：ExecuteAsync 的 MethodInfo（避免重复反射）
        /// </summary>
        private static readonly MethodInfo CachedExecuteMethod = typeof(AlipayProvider)
            .GetMethod(nameof(AlipayProvider.ExecuteAsync))!
            .MakeGenericMethod(CachedResponseType);

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
            // 使用契约元数据注册表获取元数据（自动缓存）
            var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(typeof(TRequest));

            if (metadata.Operation == null)
            {
                throw new InvalidOperationException(
                    $"Contract '{typeof(TRequest).Name}' must have [ApiOperation] attribute.");
            }

            // 转换路由：alipay.trade.create → trade/create
            // 支付宝Contract中定义的是method参数（如alipay.trade.create）
            // 需要转换为REST风格路由（如 /trade/create）
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
            // 直接使用类级别缓存（零开销）
            Task responseTask = (Task)CachedExecuteMethod.Invoke(_alipayProvider, new object[] { req, ct })!;
            await responseTask.ConfigureAwait(false);

            // 提取结果
            object? result = responseTask.GetType().GetProperty("Result")!.GetValue(responseTask);

            // 序列化并写入HTTP响应
            HttpContext.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(HttpContext.Response.Body, result, CachedResponseType, cancellationToken: ct);
        }
    }
}
