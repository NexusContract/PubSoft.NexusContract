using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PubSoft.NexusContract.Abstractions.Contracts;
using PubSoft.NexusContract.Core;
using PubSoft.NexusContract.Core.Endpoints;
using CoreExecutionContext = PubSoft.NexusContract.Core.ExecutionContext;

namespace PubSoft.NexusContract.Providers.Alipay.Endpoints
{
    /// <summary>
    /// Base class for Alipay endpoints.
    /// Provides reflection-based response type extraction from IApiRequest&lt;TResponse&gt;.
    /// Framework-level: no business logic, just contract adaptation.
    /// 
    /// 支付宝端点基类。框架层只负责适配，不涉及业务场景。
    /// </summary>
    /// <typeparam name="TRequest">Request contract implementing IApiRequest&lt;TResponse&gt;</typeparam>
    public abstract class AlipayProxyEndpoint<TRequest> : NexusProxyEndpoint<TRequest>
        where TRequest : class, IApiRequest
    {
        /// <summary>Gets or sets the Alipay provider instance</summary>
        protected AlipayProvider? AlipayProvider { get; set; }

        protected AlipayProxyEndpoint(NexusGateway gateway) : base(gateway)
        {
        }

        /// <summary>
        /// Initializes endpoint with Alipay provider.
        /// Must be called before execution.
        /// </summary>
        public virtual void Initialize(AlipayProvider provider)
        {
            AlipayProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// Executes Alipay request with automatic response type inference.
        /// </summary>
        public async Task<TResponse> ExecuteAlipayAsync<TResponse>(
            IApiRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            where TResponse : class, new()
        {
            if (AlipayProvider == null)
                throw new InvalidOperationException(
                    "AlipayProvider not initialized. Call Initialize() before execution.");

            return await AlipayProvider
                .ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Default executor. Subclasses override for business-specific handling.
        /// </summary>
        protected virtual Func<CoreExecutionContext, IDictionary<string, object>, Task<IDictionary<string, object>>>
            DefaultExecutor => async (context, projected) =>
        {
            var result = new Dictionary<string, object>();
            var operationId = context.OperationId ?? "unknown";

            projected["alipay_operation"] = operationId;

            // Actual business logic is in application/endpoint subclass
            return await Task.FromResult(result).ConfigureAwait(false);
        };
    }
}
