// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;
using NexusContract.Abstractions.Policies;
using NexusContract.Core;
using NexusContract.Core.Policies.Impl;

namespace NexusContract.Providers.Alipay.ServiceConfiguration
{
    /// <summary>
    /// 支付宝提供商DI扩展
    /// 
    /// 使用方式：
    ///   services.AddAlipayProvider(new AlipayProviderConfig { ... });
    /// </summary>
    public static class AlipayServiceExtensions
    {
        public static IServiceCollection AddAlipayProvider(
            this IServiceCollection services,
            AlipayProviderConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            // 注册配置
            services.AddSingleton(config);

            // 注册HTTP客户端
            services.AddHttpClient<AlipayProvider>()
                .ConfigureHttpClient(client =>
                {
                    client.Timeout = config.RequestTimeout;
                });

            // 注册命名策略（支付宝使用snake_case）
            services.AddSingleton<INamingPolicy>(new SnakeCaseNamingPolicy());

            // 注册NexusGateway（支付宝统一使用一个实例）
            services.AddSingleton(sp =>
                new NexusGateway(
                    sp.GetRequiredService<INamingPolicy>(),
                    encryptor: null,
                    decryptor: null)
            );

            return services;
        }
    }
}


