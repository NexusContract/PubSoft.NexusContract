// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Demo.Alipay.Contract.Transactions;
using NexusContract.Providers.Alipay;

namespace Demo.Alipay.HttpApi.Endpoints;

/// <summary>交易预创建接口 - 契约: [ApiOperation("alipay.trade.precreate")]</summary>
public class TradePrecreateEndpoint(AlipayProvider alipayProvider) : AlipayEndpointBase<TradePrecreateRequest>(alipayProvider)
{
}
