// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Demo.Alipay.Contract.Transactions;
using PubSoft.NexusContract.Core;
using PubSoft.NexusContract.Core.Endpoints;

namespace Demo.Alipay.HttpApi.Endpoints;

/// <summary>线下交易预创建接口</summary>
public class TradePrecreateEndpoint : NexusProxyEndpoint<TradePrecreateRequest>
{
    private readonly PubSoft.NexusContract.Providers.Alipay.AlipayProvider _alipayProvider;

    public TradePrecreateEndpoint(
        PubSoft.NexusContract.Providers.Alipay.AlipayProvider alipayProvider,
        NexusGateway gateway)
        : base(gateway)
    {
        _alipayProvider = alipayProvider ?? throw new ArgumentNullException(nameof(alipayProvider));
    }

    public async Task<TradePrecreateResponse> HandleAsync(
        TradePrecreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        return await _alipayProvider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
