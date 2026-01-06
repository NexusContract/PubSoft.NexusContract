// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net.Http;
using PubSoft.NexusContract.Abstractions.Policies;

namespace PubSoft.NexusContract.Client
{
    /// <summary>
    /// NexusGateway 客户端工厂（.NET 10 静态工厂）
    /// 
    /// 支持点分标识符（如 "allinpay.yunst"）映射到 Uri 实例
    /// 
    /// 【决策 A-502】FrozenDictionary + 点分标识符路由：
    /// 1. 为什么 FrozenDictionary？
    ///    - 启动期注册所有网关 URI → 编译成不可变集合
    ///    - 运行时 O(1) 查询，无锁、无哈希碰撞风险
    ///    - 内存布局紧凑，适合高频路由查询（每次 SendAsync 一次）
    ///    - 符合"启动期锁定，运行期零开销"的设计哲学
    /// 
    /// 2. 为什么点分标识符？
    ///    - 支付网关命名规范往往是 "provider.endpoint.resource" 形式
    ///    - 按第一部分路由（provider）最符合多网关架构
    ///    - 示例：allinpay.yunst.trade.pay → 路由到 allinpay 网关
    ///    - 保持扩展性：未来可轻松加入新的支付方供应商
    /// </summary>
    public sealed class NexusGatewayClientFactory(
        INamingPolicy namingPolicy,
        FrozenDictionary<string, Uri> gatewayMap)
    {
        /// <summary>
        /// 创建客户端（按点分标识符）
        /// </summary>
        public NexusGatewayClient CreateClient(string operationKey, HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
                throw new ArgumentException("Operation key cannot be null or empty", nameof(operationKey));

            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));

            // 点分标识符解析（如 "allinpay.yunst" → "allinpay"）
            var providerKey = operationKey.Split('.')[0];

            if (!gatewayMap.TryGetValue(providerKey, out var gatewayUri))
            {
                throw new KeyNotFoundException(
                    $"Gateway '{providerKey}' not found in map. Available: {string.Join(", ", gatewayMap.Keys)}");
            }

            return new NexusGatewayClient(httpClient, namingPolicy, gatewayUri);
        }

        /// <summary>
        /// 创建工厂（Builder 模式）
        /// </summary>
        public static Builder CreateBuilder(INamingPolicy namingPolicy)
        {
            return new Builder(namingPolicy);
        }

        public sealed class Builder(INamingPolicy namingPolicy)
        {
            private readonly Dictionary<string, Uri> _gatewayMap = new();

            /// <summary>
            /// 注册单个网关
            /// </summary>
            public Builder RegisterGateway(string providerKey, Uri gatewayUri)
            {
                if (string.IsNullOrWhiteSpace(providerKey))
                    throw new ArgumentException("Provider key cannot be null or empty", nameof(providerKey));

                if (gatewayUri == null)
                    throw new ArgumentNullException(nameof(gatewayUri));

                _gatewayMap[providerKey] = gatewayUri;
                return this;
            }

            /// <summary>
            /// 注册多个网关
            /// </summary>
            public Builder RegisterGateways(params (string key, Uri uri)[] gateways)
            {
                foreach (var (key, uri) in gateways)
                {
                    RegisterGateway(key, uri);
                }

                return this;
            }

            /// <summary>
            /// 构建工厂实例
            /// </summary>
            public NexusGatewayClientFactory Build()
            {
                if (_gatewayMap.Count == 0)
                    throw new InvalidOperationException("At least one gateway must be registered");

                return new NexusGatewayClientFactory(
                    namingPolicy,
                    _gatewayMap.ToFrozenDictionary());
            }
        }
    }
}
