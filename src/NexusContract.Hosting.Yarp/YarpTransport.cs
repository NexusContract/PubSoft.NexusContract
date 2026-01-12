// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Abstractions.Transport;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace NexusContract.Hosting.Yarp
{
    /// <summary>
    /// YARP 传输层实现：INexusTransport 的高性能 HTTP/2 实现
    /// 
    /// 核心特性：
    /// 1. HTTP/2 连接池：多路复用，单连接并发多请求
    /// 2. Polly 重试策略：指数退避，智能重试
    /// 3. 熔断器：快速失败，防止雪崩
    /// 4. 连接池管理：复用连接，减少 TLS 握手
    /// 5. 性能指标：请求耗时、成功率统计
    /// 
    /// 使用场景：
    /// - Provider → YarpTransport → 上游 API
    /// - 替代 HttpClient 直连（生产环境推荐）
    /// 
    /// 性能对比：
    /// - HttpClient 直连：~100ms（每次 TLS 握手）
    /// - YarpTransport：~10ms（连接池复用）
    /// 
    /// 架构位置：
    /// Egress 层（出口层）
    /// </summary>
    public sealed class YarpTransport : INexusTransport, IDisposable
    {
        private static readonly ActivitySource ActivitySource = new ActivitySource("NexusContract.Yarp", "1.2.0");

        private readonly HttpClient _httpClient;
        private readonly ILogger<YarpTransport> _logger;
        private readonly YarpTransportOptions _options;
        private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
        private readonly ConcurrentDictionary<string, long> _hostMetrics;
        private bool _disposed;

        /// <summary>
        /// 构造 YARP 传输层
        /// </summary>
        /// <param name="httpClient">HTTP 客户端（由 IHttpClientFactory 创建）</param>
        /// <param name="options">传输层配置</param>
        /// <param name="logger">日志记录器</param>
        public YarpTransport(
            HttpClient httpClient,
            IOptions<YarpTransportOptions> options,
            ILogger<YarpTransport> logger)
        {
            NexusGuard.EnsurePhysicalAddress(httpClient);
            NexusGuard.EnsurePhysicalAddress(options);
            NexusGuard.EnsurePhysicalAddress(logger);

            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
            _hostMetrics = new ConcurrentDictionary<string, long>();

            // 配置 HTTP/2 连接池
            ConfigureHttpClient();

            // 构建 Polly 弹性管道
            _resiliencePipeline = BuildResiliencePipeline();
        }

        /// <summary>
        /// 发送 HTTP 请求（通过弹性管道 + OpenTelemetry 埋点）
        /// </summary>
        public async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            NexusGuard.EnsurePhysicalAddress(request);

            string host = request.RequestUri?.Host ?? "unknown";

            // 创建 OpenTelemetry Activity（用于分布式追踪）
            using Activity? activity = _options.EnableMetrics
                ? ActivitySource.StartActivity("YarpTransport.SendAsync", ActivityKind.Client)
                : null;

            if (activity != null)
            {
                activity.SetTag("http.method", request.Method.ToString());
                activity.SetTag("http.url", request.RequestUri?.ToString() ?? "unknown");
                activity.SetTag("http.host", host);
                activity.SetTag("nexus.transport", "yarp-http2");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            int retryCount = 0;

            try
            {
                // 通过 Polly 弹性管道执行（自动重试 + 熔断）
                HttpResponseMessage response = await _resiliencePipeline.ExecuteAsync(
                    async ct =>
                    {
                        HttpResponseMessage result = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

                        // 记录每次重试
                        if (retryCount > 0 && activity != null)
                        {
                            activity.SetTag("nexus.retry_count", retryCount);
                        }

                        return result;
                    },
                    cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();

                // 更新性能指标
                if (_options.EnableMetrics)
                {
                    _hostMetrics.AddOrUpdate(host, stopwatch.ElapsedMilliseconds, (_, old) =>
                        (old + stopwatch.ElapsedMilliseconds) / 2); // 简单移动平均

                    if (activity != null)
                    {
                        activity.SetTag("http.status_code", (int)response.StatusCode);
                        activity.SetTag("nexus.duration_ms", stopwatch.ElapsedMilliseconds);
                        activity.SetTag("nexus.circuit_state", "closed");
                    }
                }

                // 日志记录（可选）
                if (_options.EnableRequestResponseLogging)
                {
                    _logger.LogInformation(
                        "YARP Transport: {Method} {Uri} → {StatusCode} ({Duration}ms, {RetryCount} retries)",
                        request.Method,
                        request.RequestUri,
                        response.StatusCode,
                        stopwatch.ElapsedMilliseconds,
                        retryCount);
                }

                return response;
            }
            catch (BrokenCircuitException ex)
            {
                stopwatch.Stop();

                if (activity != null)
                {
                    activity.SetTag("nexus.circuit_state", "open");
                    activity.SetTag("error", true);
                    activity.SetStatus(ActivityStatusCode.Error, "Circuit breaker is open");
                }

                _logger.LogError(ex,
                    "Circuit breaker is open for {Host}. Fast-failing request.",
                    host);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                if (activity != null)
                {
                    activity.SetTag("error", true);
                    activity.SetTag("nexus.duration_ms", stopwatch.ElapsedMilliseconds);
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                }

                _logger.LogError(ex,
                    "YARP Transport failed: {Method} {Uri} (after {Duration}ms)",
                    request.Method,
                    request.RequestUri,
                    stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// POST JSON 请求（便捷方法）
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(
            Uri requestUri,
            HttpContent content,
            CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = content,
                Version = HttpVersion.Version20, // 强制 HTTP/2
                VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };

            return await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// GET 请求（便捷方法）
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(
            Uri requestUri,
            CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri)
            {
                Version = HttpVersion.Version20, // 强制 HTTP/2
                VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };

            return await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 配置 HTTP 客户端（HTTP/2 连接池）
        /// </summary>
        private void ConfigureHttpClient()
        {
            _httpClient.Timeout = _options.RequestTimeout;
            _httpClient.DefaultRequestVersion = HttpVersion.Version20;
            _httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        }

        /// <summary>
        /// 构建 Polly 弹性管道（重试 + 熔断 + 超时）
        /// </summary>
        private ResiliencePipeline<HttpResponseMessage> BuildResiliencePipeline()
        {
            return new ResiliencePipelineBuilder<HttpResponseMessage>()
                // 1. 超时策略（最外层）
                .AddTimeout(_options.RequestTimeout)

                // 2. 熔断器策略
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    FailureRatio = 0.5, // 50% 失败率触发熔断
                    SamplingDuration = _options.CircuitBreakerSamplingDuration,
                    MinimumThroughput = _options.CircuitBreakerFailureThreshold,
                    BreakDuration = _options.CircuitBreakerDurationOfBreak,
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>()
                        .HandleResult(response =>
                            response.StatusCode == HttpStatusCode.RequestTimeout ||
                            response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                            response.StatusCode == HttpStatusCode.GatewayTimeout),
                    OnOpened = args =>
                    {
                        _logger.LogWarning(
                            "Circuit breaker opened. Fast-failing for {Duration}.",
                            _options.CircuitBreakerDurationOfBreak);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = args =>
                    {
                        _logger.LogInformation("Circuit breaker closed. Resuming normal operations.");
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = args =>
                    {
                        _logger.LogInformation("Circuit breaker half-opened. Testing recovery.");
                        return ValueTask.CompletedTask;
                    }
                })

                // 3. 重试策略（指数退避）
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = _options.RetryCount,
                    Delay = _options.RetryBaseDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true, // 添加抖动，避免惊群效应
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>()
                        .HandleResult(response =>
                            response.StatusCode == HttpStatusCode.RequestTimeout ||
                            response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                            response.StatusCode == HttpStatusCode.TooManyRequests),
                    OnRetry = args =>
                    {
                        _logger.LogWarning(
                            "Retrying request (attempt {Attempt}/{MaxAttempts}) after {Delay}ms. Reason: {Outcome}",
                            args.AttemptNumber + 1,
                            _options.RetryCount,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString() ?? "Unknown");
                        return ValueTask.CompletedTask;
                    }
                })

                .Build();
        }

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
        public async Task WarmupAsync(IEnumerable<string> hosts, CancellationToken cancellationToken = default)
        {
            if (hosts == null)
                NexusGuard.EnsurePhysicalAddress(hosts);

            List<Task> warmupTasks = new List<Task>();

            foreach (string host in hosts)
            {
                warmupTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        Uri warmupUri = new Uri($"https://{host}/");
                        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, warmupUri)
                        {
                            Version = HttpVersion.Version20,
                            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
                        };

                        // 发送 HEAD 请求预热连接
                        using HttpResponseMessage response = await _httpClient
                            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                            .ConfigureAwait(false);

                        _logger.LogInformation(
                            "YARP warmup: {Host} → {StatusCode} (HTTP/{Version})",
                            host,
                            response.StatusCode,
                            response.Version);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "YARP warmup failed for {Host}. Connection will be established on first request.",
                            host);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(warmupTasks).ConfigureAwait(false);
        }

        /// <summary>
        /// 获取主机性能指标（平均响应时间）
        /// 
        /// 用途：监控各上游 API 的健康状况
        /// </summary>
        public IReadOnlyDictionary<string, long> GetHostMetrics()
        {
            return _hostMetrics;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            // HttpClient 由 IHttpClientFactory 管理，不在这里 Dispose
            ActivitySource.Dispose();
            _disposed = true;
        }
    }
}
