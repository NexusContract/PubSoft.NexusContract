// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NexusContract.Abstractions.Transport
{
    /// <summary>
    /// Nexus 传输层接口：出口层（Egress）HTTP/2 传输抽象
    /// 
    /// 职责：
    /// - 高性能 HTTP/2 连接池（替代 HttpClient 单连接模式）
    /// - 自动重试（Polly 集成）
    /// - 熔断器（Circuit Breaker，防止雪崩）
    /// - 负载均衡（支持多个上游网关地址）
    /// - 请求/响应日志（便于审计）
    /// 
    /// 使用场景：
    /// - Provider 发送请求到上游 API（支付宝、微信支付等）
    /// - 替代直接使用 HttpClient（生产环境推荐）
    /// 
    /// 架构位置：
    /// Provider → INexusTransport → 传输层实现 → 上游 API
    /// 
    /// 性能特征：
    /// - HTTP/2 多路复用（单连接并发多个请求）
    /// - 连接池复用（减少 TLS 握手开销）
    /// - 自动重试（指数退避）
    /// - 熔断保护（快速失败）
    /// </summary>
    public interface INexusTransport
    {
        /// <summary>
        /// 发送 HTTP 请求（通过 YARP 传输层）
        /// </summary>
        /// <param name="request">HTTP 请求消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>HTTP 响应消息</returns>
        Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// POST JSON 请求（便捷方法）
        /// </summary>
        /// <param name="requestUri">请求 URI</param>
        /// <param name="content">请求内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>HTTP 响应消息</returns>
        Task<HttpResponseMessage> PostAsync(
            Uri requestUri,
            HttpContent content,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// GET 请求（便捷方法）
        /// </summary>
        /// <param name="requestUri">请求 URI</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>HTTP 响应消息</returns>
        Task<HttpResponseMessage> GetAsync(
            Uri requestUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 预热 HTTP/2 连接（生产环境推荐在启动时调用）
        /// 
        /// 原理：发送 HEAD 请求到目标网关，建立 HTTP/2 连接并缓存
        /// 效果：首次业务请求跳过 TLS 握手，延迟降低 ~100ms
        /// 
        /// 使用场景：
        /// - 应用启动时预热常用 Provider 网关（支付宝、微信）
        /// - JIT 加载新租户配置后，预热对应网关
        /// </summary>
        /// <param name="hosts">要预热的主机列表（如：openapi.alipay.com）</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task WarmupAsync(IEnumerable<string> hosts, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取主机性能指标（平均响应时间）
        /// 
        /// 用途：监控各上游 API 的健康状况
        /// </summary>
        IReadOnlyDictionary<string, long> GetHostMetrics();
    }
}
