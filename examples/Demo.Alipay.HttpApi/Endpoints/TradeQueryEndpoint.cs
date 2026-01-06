// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Demo.Alipay.Contract.Transactions;
using PubSoft.NexusContract.Core;
using PubSoft.NexusContract.Core.Endpoints;

namespace Demo.Alipay.HttpApi.Endpoints;

/// <summary>交易查询接口</summary>
public class TradeQueryEndpoint : NexusProxyEndpoint<TradeQueryRequest>
{
    private readonly PubSoft.NexusContract.Providers.Alipay.AlipayProvider _alipayProvider;

    public TradeQueryEndpoint(
        PubSoft.NexusContract.Providers.Alipay.AlipayProvider alipayProvider,
        NexusGateway gateway)
        : base(gateway)
    {
        _alipayProvider = alipayProvider ?? throw new ArgumentNullException(nameof(alipayProvider));
    }

    public async Task<TradeQueryResponse> HandleAsync(
        TradeQueryRequest request,
        CancellationToken cancellationToken = default)
    {
    if (request == null)
            throw new ArgumentNullException(nameof(request));

        return await _alipayProvider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
