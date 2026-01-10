// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Demo.Alipay.Contract.Transactions;
using NexusContract.Abstractions.Core;

namespace Demo.Alipay.HttpApi.Endpoints
{
    /// <summary>
    /// 支付宝统一收单交易支付端点（ISV多租户架构）
    /// 
    /// 架构：FastEndpoints → NexusEngine → IProvider → INexusTransport → Alipay API
    /// 契约: [ApiOperation("alipay.trade.pay", HttpVerb.POST)] 自动决定路由
    /// 响应: IApiRequest&lt;TradePayResponse&gt; 自动决定返回类型
    /// 
    /// 租户标识：X-Tenant-Id header 或 ?tenantId=xxx query 参数
    /// </summary>
    public class TradePayEndpoint(INexusEngine engine) : AlipayEndpointBase<TradePayRequest>(engine)
    {
    }
}
