// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexusContract.Abstractions.Transport;

namespace NexusContract.Hosting.Yarp
{
    /// <summary>
    /// YARP 传输层 DI 扩展方法
    /// 
    /// 使用方式：
    /// <code>
    /// // Startup.cs
    /// builder.Services.AddNexusYarpTransport(options =>
    /// {
    ///     options.RetryCount = 3;
    ///     options.CircuitBreakerFailureThreshold = 5;
    ///     options.RequestTimeout = TimeSpan.FromSeconds(30);
    /// });
    /// </code>
    /// </summary>
    public static class YarpServiceExtensions
    {
        /// <summary>
        /// 注册 YARP 传输层（出口层）
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configure">配置委托</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddNexusYarpTransport(
            this IServiceCollection services,
            Action<YarpTransportOptions>? configure = null)
        {
            // 1. 注册配置选项
            if (configure != null)
            {
                services.Configure(configure);
            }
            else
            {
                services.AddOptions<YarpTransportOptions>();
            }

            // 2. 注册 HTTP 客户端工厂（HTTP/2 连接池）
            services.AddHttpClient<INexusTransport, YarpTransport>((sp, client) =>
            {
                YarpTransportOptions options = sp.GetRequiredService<IOptions<YarpTransportOptions>>().Value;

                // 配置 HTTP/2 连接池
                client.Timeout = options.RequestTimeout;
                client.DefaultRequestVersion = new Version(2, 0);
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                YarpTransportOptions options = sp.GetRequiredService<IOptions<YarpTransportOptions>>().Value;

                return new SocketsHttpHandler
                {
                    // HTTP/2 连接池配置
                    MaxConnectionsPerServer = options.MaxConnectionsPerServer,
                    PooledConnectionIdleTimeout = options.PooledConnectionIdleTimeout,
                    PooledConnectionLifetime = options.PooledConnectionLifetime,
                    
                    // 启用 HTTP/2
                    EnableMultipleHttp2Connections = true,
                    
                    // 自动解压缩
                    AutomaticDecompression = System.Net.DecompressionMethods.All,
                    
                    // 超时设置
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                };
            });

            // 3. 注册为单例（INexusTransport）
            // 注意：不直接注册单例，而是通过 HttpClientFactory 管理生命周期

            return services;
        }

        /// <summary>
        /// 注册 YARP 传输层（使用默认配置）
        /// </summary>
        public static IServiceCollection AddNexusYarpTransport(this IServiceCollection services)
        {
            return AddNexusYarpTransport(services, configure: null);
        }
    }
}
