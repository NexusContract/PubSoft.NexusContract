// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Demo.Alipay.Contract.Transactions;
using PubSoft.NexusContract.Core;
using PubSoft.NexusContract.Core.Endpoints;

namespace Demo.Alipay.HttpApi.Endpoints;

/// <summary>交易创建接口</summary>
public class TradeCreateEndpoint : NexusProxyEndpoint<TradeCreateRequest>
{
    private readonly PubSoft.NexusContract.Providers.Alipay.AlipayProvider _alipayProvider;

    public TradeCreateEndpoint(
        PubSoft.NexusContract.Providers.Alipay.AlipayProvider alipayProvider,
        NexusGateway gateway)
        : base(gateway)
    {
        _alipayProvider = alipayProvider ?? throw new ArgumentNullException(nameof(alipayProvider));
    }

    public async Task<TradeCreateResponse> HandleAsync(
        TradeCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        return await _alipayProvider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
