// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NexusContract.Providers.Alipay
{
    /// <summary>
    /// 支付宝 OpenAPI v3 签名处理器
    /// 
    /// 职责：
    /// 1. 自动为出向请求计算并添加 RSA2 签名到 Authorization 头。
    /// 2. 自动为入向响应验证签名。
    /// </summary>
    public class AlipaySignatureHandler(AlipayProviderConfig config) : DelegatingHandler
    {
        private readonly AlipayProviderConfig _config = config ?? throw new ArgumentNullException(nameof(config));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 1. 读取请求体
            string requestBody = string.Empty;
            if (request.Content != null)
            {
                requestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            // 2. 构建待签名字符串
            string method = request.Method.Method;
            string path = request.RequestUri?.PathAndQuery ?? "/";
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            string nonce = Guid.NewGuid().ToString("N");
            string signContent = $"{method}\n{path}\n{timestamp}\n{nonce}\n{requestBody}\n";

            // 3. 生成签名
            string signature = GenerateSignature(signContent);

            // 4. 设置 Authorization 头
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "ALIPAY-SHA256withRSA",
                $"app_id={_config.AppId},timestamp={timestamp},nonce={nonce},sign={signature}"
            );

            // 5. 发送请求
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // 6. 验证响应签名
            if (!await VerifyResponseSignature(response, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Alipay response signature verification failed.");
            }

            return response;
        }

        private string GenerateSignature(string content)
        {
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(_config.PrivateKey);
                byte[] data = Encoding.UTF8.GetBytes(content);
                byte[] signature = rsa.SignData(data, "SHA256");
                return Convert.ToBase64String(signature);
            }
        }

        private async Task<bool> VerifyResponseSignature(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (!response.Headers.TryGetValues("Alipay-Timestamp", out var timestamps) ||
                !response.Headers.TryGetValues("Alipay-Nonce", out var nonces) ||
                !response.Headers.TryGetValues("Alipay-Signature", out var signatures))
            {
                // 如果没有签名头，则不验证（例如非支付宝的响应）
                return true;
            }

            string timestamp = timestamps.First();
            string nonce = nonces.First();
            string signature = signatures.First();

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string verifyContent = $"{timestamp}\n{nonce}\n{responseBody}\n";

            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(_config.AlipayPublicKey);
                byte[] data = Encoding.UTF8.GetBytes(verifyContent);
                byte[] signBytes = Convert.FromBase64String(signature);
                return rsa.VerifyData(data, "SHA256", signBytes);
            }
        }
    }
}


