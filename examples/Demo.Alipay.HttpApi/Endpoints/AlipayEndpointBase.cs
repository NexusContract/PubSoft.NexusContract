// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastEndpoints;
using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Abstractions.Contracts;
using PubSoft.NexusContract.Providers.Alipay;
using NexusReflectionCache = PubSoft.NexusContract.Core.Reflection.ReflectionCache;

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
    /// </summary>
    public abstract class AlipayEndpointBase<TRequest> : Endpoint<TRequest>
        where TRequest : class, IApiRequest
    {
        private readonly AlipayProvider _alipayProvider;

        protected AlipayEndpointBase(AlipayProvider alipayProvider)
        {
            _alipayProvider = alipayProvider ?? throw new ArgumentNullException(nameof(alipayProvider));
        }

        public override void Configure()
        {
            // 使用ReflectionCache获取契约元数据（自动缓存）
            var metadata = NexusReflectionCache.Instance.GetMetadata(typeof(TRequest));
            
            if (metadata.Operation == null)
            {
                throw new InvalidOperationException(
                    $"Contract '{typeof(TRequest).Name}' must have [ApiOperation] attribute.");
            }

            // 转换路由：alipay.trade.create → trade/create
            // 支付宝Contract中定义的是method参数（如alipay.trade.create）
            // 需要转换为REST风格路由（如 /trade/create）
            var methodName = metadata.Operation.Operation;
            var route = methodName.StartsWith("alipay.", StringComparison.OrdinalIgnoreCase)
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

        public override async Task HandleAsync(TRequest req, CancellationToken ct)
        {
            // 使用ReflectionCache获取契约元数据（已缓存，O(1)查询）
            var metadata = NexusReflectionCache.Instance.GetMetadata(typeof(TRequest));
            
            // 从IApiRequest<TResponse>提取响应类型
            var apiRequestInterface = typeof(TRequest)
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition().Name == "IApiRequest`1");

            if (apiRequestInterface == null)
            {
                throw new InvalidOperationException(
                    $"Request type '{typeof(TRequest).Name}' must implement IApiRequest<TResponse>.");
            }

            var responseType = apiRequestInterface.GetGenericArguments()[0];

            // 动态调用 ExecuteAsync<TResponse>（使用缓存的MethodInfo提高性能）
            var executeMethod = typeof(AlipayProvider)
                .GetMethod("ExecuteAsync")!
                .MakeGenericMethod(responseType);

            var responseTask = (Task)executeMethod.Invoke(_alipayProvider, new object[] { req, ct })!;
            await responseTask.ConfigureAwait(false);

            // 提取结果
            var result = responseTask.GetType().GetProperty("Result")!.GetValue(responseTask);

            // 序列化并写入HTTP响应
            HttpContext.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(HttpContext.Response.Body, result, result!.GetType(), cancellationToken: ct);
        }
    }
}
