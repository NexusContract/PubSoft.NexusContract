// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NexusContract.Abstractions.Contracts;
using NexusContract.Core.Reflection;

namespace NexusContract.Providers.Alipay
{
    /// <summary>
    /// 支付宝 OpenAPI v3 极简代理（不依赖 NexusGateway）
    /// 
    /// 设计理念：
    /// ═══════════════════════════════════════════════════════════════════════════
    /// 支付宝 OpenAPI v3 是纯 RESTful 接口：
    /// - 请求：POST /v3/alipay/trade/pay + JSON Body
    /// - 响应：JSON Body
    /// - 字段名：snake_case（与 Contract 的 [ApiField] 一致）
    /// 
    /// 在这种"HTTP → HTTP 透传"场景下，NexusGateway 的四阶段管道（验证→投影→执行→回填）
    /// 显得过于复杂。本 Provider 采用极简设计：
    /// 
    /// 1. 直接使用 System.Text.Json 序列化 Contract → JSON
    /// 2. 通过 HttpClient（含 AlipaySignatureHandler）发送请求
    /// 3. 直接使用 System.Text.Json 反序列化 JSON → Response
    /// 
    /// 适用场景：
    /// - 纯透传代理（无字段加密、无复杂类型转换）
    /// - 高性能要求（减少中间层开销）
    /// - 调试友好（调用链短、日志清晰）
    /// 
    /// 何时使用 NexusGateway（AlipayProvider）：
    /// - 需要契约验证（必填字段检查、诊断错误码）
    /// - 需要字段加密/解密
    /// - 需要复杂命名策略转换
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    public sealed class AlipayProxyProvider : IAsyncDisposable
    {
        private readonly AlipayProviderConfig _config;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// 初始化极简代理
        /// </summary>
        /// <param name="config">支付宝配置</param>
        /// <param name="httpClient">HTTP 客户端（应注入 AlipaySignatureHandler）</param>
        public AlipayProxyProvider(AlipayProviderConfig config, HttpClient httpClient)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // 配置 JSON 序列化选项
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };

            ValidateConfiguration();
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_config.AppId))
                throw new InvalidOperationException("AppId is required");
            if (string.IsNullOrWhiteSpace(_config.PrivateKey))
                throw new InvalidOperationException("PrivateKey is required for request signing");
        }

        /// <summary>
        /// 执行支付宝请求（极简版）
        /// 
        /// 流程：
        /// 1. 从 Contract 的 [ApiOperation] 获取 API 路径
        /// 2. 序列化 Contract → JSON
        /// 3. POST 到支付宝 OpenAPI v3
        /// 4. 反序列化 JSON → TResponse
        /// </summary>
        public async Task<TResponse> ExecuteAsync<TResponse>(
            IApiRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            where TResponse : class, new()
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // 1. 获取 API 路径（从 [ApiOperation] 属性）
            Type requestType = request.GetType();
            ContractMetadata metadata = NexusContractMetadataRegistry.Instance.GetMetadata(requestType);

            if (metadata.Operation == null)
            {
                throw new InvalidOperationException(
                    $"Contract '{requestType.Name}' must have [ApiOperation] attribute.");
            }

            // 2. 构建请求 URI
            Uri requestUri = BuildRequestUri(metadata.Operation.OperationId);

            // 3. 序列化请求体
            string jsonBody = JsonSerializer.Serialize(request, requestType, _jsonOptions);

            // 4. 创建 HTTP 请求
            using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            // 5. 发送请求（签名由 AlipaySignatureHandler 自动处理）
            HttpResponseMessage httpResponse = await _httpClient
                .SendAsync(httpRequest, cancellationToken)
                .ConfigureAwait(false);

            // 6. 处理响应
            string responseBody = await httpResponse.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            // 7. 检查业务错误
            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new AlipayApiException(
                    $"Alipay API returned {(int)httpResponse.StatusCode}: {responseBody}",
                    httpResponse.StatusCode,
                    responseBody);
            }

            // 8. 反序列化响应
            TResponse? response = JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions);

            if (response == null)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize Alipay response to {typeof(TResponse).Name}");
            }

            return response;
        }

        /// <summary>
        /// 构建请求 URI
        /// 
        /// 示例：alipay.trade.pay → https://openapi.alipay.com/v3/alipay/trade/pay
        /// </summary>
        private Uri BuildRequestUri(string method)
        {
            // 转换：alipay.trade.pay → trade/pay
            string path = method.StartsWith("alipay.", StringComparison.OrdinalIgnoreCase)
                ? method.Substring("alipay.".Length).Replace('.', '/')
                : method.Replace('.', '/');

            Uri baseUri = _config.UseSandbox
                ? new Uri("https://openapi-sandbox.dl.alipaydev.com/")
                : _config.ApiGateway;

            return new Uri(baseUri, $"v3/alipay/{path}");
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            // HttpClient 由 DI 管理，不在这里 Dispose
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 支付宝 API 异常
    /// </summary>
    public class AlipayApiException(string message, System.Net.HttpStatusCode statusCode, string responseBody) : Exception(message)
    {
        /// <summary>HTTP 状态码</summary>
        public System.Net.HttpStatusCode StatusCode { get; } = statusCode;

        /// <summary>原始响应体</summary>
        public string ResponseBody { get; } = responseBody;
    }
}


