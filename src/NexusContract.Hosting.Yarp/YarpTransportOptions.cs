// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace NexusContract.Hosting.Yarp
{
    /// <summary>
    /// YARP 传输层配置选项
    /// 
    /// 配置项：
    /// - HTTP/2 连接池设置
    /// - 重试策略（Polly）
    /// - 熔断器阈值
    /// - 超时设置
    /// - 负载均衡策略
    /// </summary>
    public sealed class YarpTransportOptions
    {
        /// <summary>
        /// 请求超时（默认 30 秒）
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 重试次数（默认 3 次）
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 重试间隔基数（指数退避，默认 200ms）
        /// </summary>
        public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// 熔断器：连续失败次数阈值（默认 5 次）
        /// </summary>
        public int CircuitBreakerFailureThreshold { get; set; } = 5;

        /// <summary>
        /// 熔断器：采样时间窗口（默认 30 秒）
        /// </summary>
        public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 熔断器：断开持续时间（默认 30 秒）
        /// </summary>
        public TimeSpan CircuitBreakerDurationOfBreak { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// HTTP/2 连接池：最大连接数（默认 10）
        /// </summary>
        public int MaxConnectionsPerServer { get; set; } = 10;

        /// <summary>
        /// HTTP/2 连接池：连接空闲超时（默认 90 秒）
        /// </summary>
        public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromSeconds(90);

        /// <summary>
        /// HTTP/2 连接池：连接生命周期（默认 10 分钟）
        /// </summary>
        public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// 是否启用请求/响应日志（默认 false，生产环境慎用）
        /// </summary>
        public bool EnableRequestResponseLogging { get; set; } = false;

        /// <summary>
        /// 是否启用性能指标（默认 true）
        /// </summary>
        public bool EnableMetrics { get; set; } = true;

        /// <summary>
        /// 负载均衡策略（默认：RoundRobin）
        /// </summary>
        public LoadBalancingStrategy LoadBalancing { get; set; } = LoadBalancingStrategy.RoundRobin;
    }

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public enum LoadBalancingStrategy
    {
        /// <summary>轮询（默认）</summary>
        RoundRobin,

        /// <summary>随机</summary>
        Random,

        /// <summary>最少连接</summary>
        LeastConnections,

        /// <summary>加权轮询</summary>
        WeightedRoundRobin
    }
}
