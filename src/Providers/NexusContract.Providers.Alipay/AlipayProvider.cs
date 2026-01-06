using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PubSoft.NexusContract.Abstractions.Contracts;
using PubSoft.NexusContract.Core;
using CoreExecutionContext = PubSoft.NexusContract.Core.ExecutionContext;
using PubSoft.NexusContract.Core.Endpoints;

namespace PubSoft.NexusContract.Providers.Alipay
{
    /// <summary>
    /// Configuration for Alipay provider adapter.
    /// 支付宝提供商配置
    /// </summary>
    public sealed class AlipayProviderConfig
    {
        /// <summary>APP ID for Alipay authentication</summary>
        public required string AppId { get; init; }

        /// <summary>Merchant ID for settlement</summary>
        public required string MerchantId { get; init; }

        /// <summary>RSA private key for signature generation</summary>
        public required string PrivateKey { get; init; }

        /// <summary>Alipay public key for response verification</summary>
        public required string AlipayPublicKey { get; init; }

        /// <summary>Request timeout (default: 30 seconds)</summary>
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>Enable sandbox/test mode</summary>
        public bool UseSandbox { get; init; } = false;
    }

    /// <summary>
    /// Generic Alipay Provider adapter.
    /// Adapts Alipay SDK to NexusContract gateway layer.
    /// Does NOT define business scenarios (enterprise code, campus, etc.).
    /// Scenarios are application-level concerns.
    /// 
    /// 通用支付宝提供商适配器。不定义业务场景（这是应用层的事）。
    /// </summary>
    public class AlipayProvider : IAsyncDisposable
    {
        private readonly AlipayProviderConfig _config;
        private readonly NexusGateway _gateway;

        public AlipayProvider(AlipayProviderConfig config, NexusGateway gateway)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            ValidateConfiguration();
        }

        /// <summary>
        /// Validates provider configuration for completeness.
        /// </summary>
        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_config.AppId))
                throw new InvalidOperationException("AppId is required for Alipay provider");

            if (string.IsNullOrWhiteSpace(_config.MerchantId))
                throw new InvalidOperationException("MerchantId is required for Alipay provider");

            if (string.IsNullOrWhiteSpace(_config.PrivateKey))
                throw new InvalidOperationException("PrivateKey is required for signature generation");

            if (string.IsNullOrWhiteSpace(_config.AlipayPublicKey))
                throw new InvalidOperationException("AlipayPublicKey is required for response verification");
        }

        /// <summary>
        /// Generic execute method for Alipay requests.
        /// Automatically infers response type from IApiRequest&lt;TResponse&gt;.
        /// </summary>
        public async Task<TResponse> ExecuteAsync<TResponse>(
            IApiRequest<TResponse> request,
            Func<CoreExecutionContext, IDictionary<string, object>, Task<IDictionary<string, object>>> executorAsync,
            CancellationToken cancellationToken = default)
            where TResponse : class, new()
        {
            return await _gateway.ExecuteAsync(
                request,
                executorAsync,
                cancellationToken)
            .ConfigureAwait(false);
        }

        /// <summary>
        /// Signature generation for Alipay API calls.
        /// Implement in derived class or factory with actual RSA logic.
        /// </summary>
        public virtual string GenerateSignature(IDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
                throw new ArgumentException("Parameters cannot be null or empty", nameof(parameters));

            throw new NotImplementedException(
                "Signature generation must be implemented by derived class or factory");
        }

        /// <summary>
        /// Verifies response signature from Alipay servers.
        /// Implement in derived class or factory with actual RSA logic.
        /// </summary>
        public virtual bool VerifyResponseSignature(IDictionary<string, object> response, string signature)
        {
            if (string.IsNullOrWhiteSpace(signature))
                return false;

            throw new NotImplementedException(
                "Signature verification must be implemented by derived class or factory");
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
