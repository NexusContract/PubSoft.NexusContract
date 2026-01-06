// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Demo.Alipay.Contract.Transactions;
using PubSoft.NexusContract.Providers.Alipay;

namespace Demo.Alipay.HttpApi.Endpoints;

/// <summary>交易创建接口 - 契约: [ApiOperation("alipay.trade.create")]</summary>
public class TradeCreateEndpoint(AlipayProvider alipayProvider) : AlipayEndpointBase<TradeCreateRequest>(alipayProvider)
{
}
