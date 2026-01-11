// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;using NexusContract.Abstractions.Exceptions;using NexusContract.Abstractions.Policies;
using NexusContract.Core.Policies.Impl;

namespace NexusContract.Client.DependencyInjection
{
    /// <summary>
    /// .NET 10 DI 扩展方法（零外部依赖）
    /// 
    /// 支持将 NexusGatewayClient 和 Factory 集成到服务容器中
    /// 
    /// 【决策 A-504】NamingPolicy Singleton 的必要性：
    /// NamingPolicy 的核心职责是将 C# 属性名（PascalCase）转换为支付网关字段名（snake_case 等）。
    /// 之所以必须注册为 Singleton：
    /// 
    /// 1. 缓存效率：ProjectionEngine/ResponseHydrationEngine 内部依赖 NamingPolicy 的转换规则。
    ///    在高频交易（100+ TPS）下，多个 NamingPolicy 实例会导致转换规则重复计算。
    ///    
    /// 2. 一致性保证：整个应用程序对接一个网关，必须有统一的命名规则。
    ///    Singleton 模式确保字段映射始终一致，避免同一字段在不同实例中有不同映射。
    ///    
    /// 3. 内存效率：每个支付请求不必携带命名规则副本，直接引用全局 Singleton。
    ///    
    /// 注意：如果需要支持多个命名规则（例如同时接入支付宝的 snake_case 和银联的 camelCase），
    /// 可以创建多个 NamingPolicy 实例，并通过 CreateBuilder 传入不同的策略——但此时也必须
    /// 在 Factory 级别管理这些策略的单例性。
    /// </summary>
    public static class NexusContractClientServiceCollectionExtensions
    {
        /// <summary>
        /// 添加 NexusGateway 客户端和工厂
        /// 
        /// 使用示例：
        /// services
        ///     .AddNexusContractClient(namingPolicy: new SnakeCaseNamingPolicy())
        ///     .AddGateway("allinpay", new Uri("https://alipay.yunst.api/"))
        ///     .AddGateway("unionpay", new Uri("https://union.api.com/"))
        ///     .RegisterFactory();
        /// </summary>
        public static NexusContractClientBuilder AddNexusContractClient(
            this IServiceCollection services,
            INamingPolicy? namingPolicy = null)
        {
            if (services == null)
                NexusGuard.EnsurePhysicalAddress(services);

            var policy = namingPolicy ?? new SnakeCaseNamingPolicy();

            services.AddHttpClient();
            services.AddSingleton(policy);

            return new NexusContractClientBuilder(services);
        }
    }

    /// <summary>
    /// NexusGateway 客户端构建器（.NET 10 原生）
    /// </summary>
    public sealed class NexusContractClientBuilder(IServiceCollection services)
    {
        private readonly Dictionary<string, Uri> _gateways = new();

        /// <summary>
        /// 添加单个网关
        /// </summary>
        public NexusContractClientBuilder AddGateway(string providerKey, Uri gatewayUri)
        {
            NexusGuard.EnsureNonEmptyString(providerKey);

            if (gatewayUri == null)
                NexusGuard.EnsurePhysicalAddress(gatewayUri);

            _gateways[providerKey] = gatewayUri;

            // 为每个网关注册命名的 HttpClient（.NET 10 原生支持）
            services.AddHttpClient(providerKey)
                .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = gatewayUri;
                    client.Timeout = TimeSpan.FromSeconds(30);
                });

            return this;
        }

        /// <summary>
        /// 添加多个网关
        /// </summary>
        public NexusContractClientBuilder AddGateways(params (string key, Uri uri)[] gateways)
        {
            foreach (var (key, uri) in gateways)
            {
                AddGateway(key, uri);
            }

            return this;
        }

        /// <summary>
        /// 完成配置，注册工厂和单个客户端
        /// </summary>
        public void RegisterFactory()
        {
            NexusGuard.EnsureMinCount(_gateways);

            var frozenMap = _gateways.ToFrozenDictionary();

            // 注册工厂
            services.AddSingleton<NexusGatewayClientFactory>(sp =>
            {
                return new NexusGatewayClientFactory(frozenMap);
            });

            // 注册便利访问：直接注入 NexusGatewayClient（默认使用第一个网关）
            services.AddScoped<NexusGatewayClient>(sp =>
            {
                var factory = sp.GetRequiredService<NexusGatewayClientFactory>();
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                string firstKey = _gateways.Keys.First();
                var httpClient = httpClientFactory.CreateClient(firstKey);
                return factory.CreateClient(firstKey, httpClient);
            });
        }
    }
}



