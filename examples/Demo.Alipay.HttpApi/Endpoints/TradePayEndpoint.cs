// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using PubSoft.NexusContract.Core;
using PubSoft.NexusContract.Core.Endpoints;
using Demo.Alipay.Contract.Transactions;

namespace Demo.Alipay.HttpApi.Endpoints
{
    /// <summary>
    /// 支付宝统一收单交易支付端点
    /// 
    /// 特点：
    /// - 仅继承 NexusProxyEndpoint&lt;TradePayRequest&gt;（单泛型）
    /// - TResponse 自动从 IApiRequest&lt;TResponse&gt; 推断
    /// - 路由由 [ApiOperation] 定义，无需 Configure()
    /// </summary>
    public class TradePayEndpoint : NexusProxyEndpoint<TradePayRequest>
    {
        private readonly PubSoft.NexusContract.Providers.Alipay.AlipayProvider _alipayProvider;

        public TradePayEndpoint(
            PubSoft.NexusContract.Providers.Alipay.AlipayProvider alipayProvider,
            NexusGateway gateway)
            : base(gateway)
        {
            _alipayProvider = alipayProvider ?? throw new ArgumentNullException(nameof(alipayProvider));
        }

        /// <summary>
        /// 执行支付宝支付请求
        /// FastEndpoints 自动调用此方法
        /// </summary>
        public async Task<TradePayResponse> HandleAsync(
            TradePayRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return await _alipayProvider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
