// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Client.Exceptions
{
    /// <summary>
    /// NexusGateway 通信异常
    /// 
    /// 承载 NXC 诊断码，提供结构化的错误信息
    /// 支持与 ContractIncompleteException 的无缝转换
    /// </summary>
    public sealed class NexusCommunicationException : Exception
    {
        /// <summary>
        /// 诊断错误码（NXC1xx/NXC2xx/NXC3xx）
        /// </summary>
        public string? ErrorCode { get; }

        /// <summary>
        /// 错误分类
        /// </summary>
        public string? ErrorCategory { get; }

        /// <summary>
        /// 诊断上下文（如类名、属性名、路径等）
        /// </summary>
        public Dictionary<string, object>? DiagnosticData { get; }

        /// <summary>
        /// HTTP 状态码（仅在网络层异常时填充）
        /// </summary>
        public int? HttpStatusCode { get; }

        private NexusCommunicationException(
            string message,
            string? errorCode,
            string? errorCategory,
            Dictionary<string, object>? diagnosticData,
            int? httpStatusCode,
            Exception? innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            ErrorCategory = errorCategory;
            DiagnosticData = diagnosticData;
            HttpStatusCode = httpStatusCode;
        }

        /// <summary>
        /// 从契约验证异常转换
        /// </summary>
        public static NexusCommunicationException FromContractIncomplete(
            ContractIncompleteException contractEx)
        {
            if (contractEx == null)
                NexusGuard.EnsurePhysicalAddress(contractEx);

            string category = ContractDiagnosticRegistry.GetCategory(contractEx.ErrorCode);
            var diagnosticData = contractEx.GetDiagnosticData();

            return new NexusCommunicationException(
                $"[{contractEx.ErrorCode}] Contract validation failed: {contractEx.Message}",
                contractEx.ErrorCode,
                category,
                diagnosticData,
                null,
                contractEx);
        }

        /// <summary>
        /// HTTP 通信异常
        /// </summary>
        public static NexusCommunicationException FromHttpError(
            string message,
            int statusCode,
            Exception? innerException = null)
        {
            return new NexusCommunicationException(
                $"[HTTP-{statusCode}] {message}",
                $"NXC-HTTP-{statusCode}",
                "NetworkError",
                null,
                statusCode,
                innerException);
        }

        /// <summary>
        /// HTTP 错误转换（支持服务端返回的 NXC 错误码与诊断数据）
        /// </summary>
        public static NexusCommunicationException FromHttpError(
            string message,
            int statusCode,
            string? errorCode,
            Dictionary<string, object>? diagnosticData = null,
            Exception? innerException = null)
        {
            string category = !string.IsNullOrWhiteSpace(errorCode)
                ? ContractDiagnosticRegistry.GetCategory(errorCode)
                : "NetworkError";

            return new NexusCommunicationException(
                $"[HTTP-{statusCode}] {message}",
                errorCode ?? $"NXC-HTTP-{statusCode}",
                category,
                diagnosticData,
                statusCode,
                innerException);
        }

        /// <summary>
        /// 通用通信异常
        /// </summary>
        public static NexusCommunicationException Generic(
            string message,
            Exception? innerException = null)
        {
            return new NexusCommunicationException(
                message,
                null,
                "UnknownError",
                null,
                null,
                innerException);
        }

        /// <summary>
        /// 获取诊断摘要
        /// </summary>
        public string GetDiagnosticSummary()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(ErrorCode))
                parts.Add($"ErrorCode: {ErrorCode}");

            if (!string.IsNullOrEmpty(ErrorCategory))
                parts.Add($"Category: {ErrorCategory}");

            if (HttpStatusCode.HasValue)
                parts.Add($"HttpStatus: {HttpStatusCode}");

            if (DiagnosticData?.Count > 0)
            {
                string dataStr = string.Join(", ",
                    DiagnosticData.Where(kv => true).Select(kv => $"{kv.Key}={kv.Value}"));
                parts.Add($"Data: {{{dataStr}}}");
            }

            return string.Join(" | ", parts);
        }

        public override string ToString()
        {
            string summary = GetDiagnosticSummary();
            return $"{base.ToString()}\n{summary}";
        }
    }
}


