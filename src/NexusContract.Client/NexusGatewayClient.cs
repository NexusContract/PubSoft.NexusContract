// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Abstractions.Contracts;
using PubSoft.NexusContract.Abstractions.Exceptions;
using PubSoft.NexusContract.Abstractions.Policies;
using PubSoft.NexusContract.Client.Exceptions;
using PubSoft.NexusContract.Core;
using PubSoft.NexusContract.Core.Reflection;

namespace PubSoft.NexusContract.Client
{
    /// <summary>
    /// NexusGateway 同步客户端（.NET 10 Primary Constructor 版本）
    /// 
    /// 这是连接业务与网关的"大动脉"，提供最纯净的 API 体验：
    /// - 零构造函数冗余（Primary Constructor）
    /// - 一行搞定初始化（自动推断 TResponse）
    /// - 内置诊断体系（NXC 错误码转换）
    /// - 性能至上（零拷贝路由）
    /// 
    /// 【决策 A-501】Primary Constructor 的选择原因：
    /// .NET 10 引入的一级构造函数（Primary Constructor）是对传统样板代码的终极解决方案。
    /// 相比传统的字段赋值模式，它减少了 50%+ 的噪音代码，完全符合 NexusContract 的"纯净至上"理念。
    /// 代价：仅限 .NET 10+，但这在 2026 年已是完全可接受的约束。
    /// </summary>
    public sealed class NexusGatewayClient(
        HttpClient httpClient,
        INamingPolicy namingPolicy,
        Uri? baseUri = null)
    {
        private readonly Uri _baseUri = baseUri ?? httpClient.BaseAddress ?? throw new InvalidOperationException(
            "HttpClient must have BaseAddress or baseUri parameter");

        /// <summary>
        /// 发送请求（自动类型推断）
        /// 
        /// 使用示例：
        /// var response = await client.SendAsync(new PaymentRequest { Amount = 1000 });
        /// 
        /// 自动推断 TResponse，无需显式指定泛型参数
        /// </summary>
        public async Task<TResponse> SendAsync<TResponse>(
            IApiRequest<TResponse> request,
            CancellationToken ct = default)
            where TResponse : class, new()
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            try
            {
                // 1. 提取 [ApiOperation] 元数据
                var requestType = request.GetType();
                var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(requestType);
                var operation = metadata.Operation
                    ?? throw new InvalidOperationException($"[{requestType.Name}] missing [ApiOperation] attribute");

                // 2. 构建请求 URL（零拷贝倾向）
                var requestUri = new Uri(_baseUri, operation.Operation);

                // 3. 序列化请求体（利用 .NET 10 的 StringContent.Create）
                using var content = JsonContent.Create(
                    request,
                    options: System.Text.Json.JsonSerializerOptions.Default);

                // 4. 发送 HTTP 请求
                using var httpRequest = new HttpRequestMessage(
                    new HttpMethod(operation.Verb.ToString().ToUpperInvariant()),
                    requestUri)
                {
                    Content = content
                };

                var httpResponse = await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);

                // 5. 检查 HTTP 状态
                if (!httpResponse.IsSuccessStatusCode)
                {
                    var statusCodeInt = (int)httpResponse.StatusCode;
                    var errorContent = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                    // 尝试将 body 反序列化为共享的 NxcErrorEnvelope
                    string? parsedCode = null;
                    string? parsedMessage = null;
                    Dictionary<string, object>? parsedData = null;

                    try
                    {
                        var options = new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        var envelope = System.Text.Json.JsonSerializer.Deserialize<PubSoft.NexusContract.Abstractions.Contracts.NxcErrorEnvelope>(errorContent, options);
                        if (envelope != null && !string.IsNullOrWhiteSpace(envelope.Code))
                        {
                            parsedCode = envelope.Code;
                            parsedMessage = envelope.Message;

                            if (envelope.Data != null)
                            {
                                parsedData = new Dictionary<string, object>(envelope.Data);
                            }
                        }
                    }
                    catch
                    {
                        // 忽略解析错误，回退到原始字符串
                    }

                    var effectiveMessage = string.IsNullOrWhiteSpace(parsedMessage)
                        ? $"Gateway returned {httpResponse.StatusCode}: {errorContent}"
                        : parsedMessage;

                    if (!string.IsNullOrWhiteSpace(parsedCode) && parsedCode.StartsWith("NXC", StringComparison.OrdinalIgnoreCase))
                    {
                        throw NexusCommunicationException.FromHttpError(effectiveMessage, statusCodeInt, parsedCode, parsedData);
                    }

                    throw NexusCommunicationException.FromHttpError($"Gateway returned {httpResponse.StatusCode}: {errorContent}", statusCodeInt);
                }

                // 6. 反序列化响应（利用 System.Net.Http.Json）
                var responseStream = await httpResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var response = await System.Text.Json.JsonSerializer.DeserializeAsync<TResponse>(
                    responseStream,
                    System.Text.Json.JsonSerializerOptions.Default,
                    ct).ConfigureAwait(false)
                    ?? new TResponse();

                return response;
            }
            catch (NexusCommunicationException)
            {
                throw;
            }
            catch (ContractIncompleteException contractEx)
            {
                throw NexusCommunicationException.FromContractIncomplete(contractEx);
            }
            catch (HttpRequestException httpEx)
            {
                throw NexusCommunicationException.FromHttpError(
                    $"Network error: {httpEx.Message}",
                    500,
                    httpEx);
            }
            catch (OperationCanceledException)
            {
                throw NexusCommunicationException.Generic(
                    "Request was cancelled",
                    new OperationCanceledException());
            }
            catch (Exception ex)
            {
                throw NexusCommunicationException.Generic(
                    $"Unexpected error: {ex.Message}",
                    ex);
            }
            
            // 【决策 A-503】异常统一化原理：
            // 无论错误来自契约验证、JSON 序列化、HTTP 通信还是反序列化，
            // 都统一为 NexusCommunicationException 并自动填充 NXC 诊断码。
            // 这样做的好处：
            // 1. 调用者只需 catch 一个异常类型
            // 2. 结构化的 DiagnosticData 便于日志和监控
            // 3. 支付网关的问题诊断路径明确清晰
            // 注意：这不是"隐藏"错误，而是"分层暴露"——
            // 内部异常存储在 InnerException 中，供细粒度调试使用
        }

        /// <summary>
        /// 仅投影（用于需要单纯序列化的高级场景）
        /// </summary>
        public IDictionary<string, object> Project<TContract>(TContract contract)
            where TContract : notnull
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            try
            {
                var gateway = new NexusGateway(namingPolicy);
                return gateway.Project(contract);
            }
            catch (ContractIncompleteException contractEx)
            {
                throw NexusCommunicationException.FromContractIncomplete(contractEx);
            }
        }
    }
}
