// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Demo.Alipay.Contract.Transactions;
using PubSoft.NexusContract.Providers.Alipay;

namespace Demo.Alipay.HttpApi.Endpoints
{
    /// <summary>
    /// 支付宝统一收单交易支付端点
    /// 契约: [ApiOperation("alipay.trade.pay", HttpVerb.POST)] 自动决定路由
    /// 响应: IApiRequest<TradePayResponse> 自动决定返回类型
    /// </summary>
    public class TradePayEndpoint : AlipayEndpointBase<TradePayRequest>
    {
        public TradePayEndpoint(AlipayProvider alipayProvider) 
            : base(alipayProvider)
        {
        }
    }
}
