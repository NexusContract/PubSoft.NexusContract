// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Core.Reflection
{
    /// <summary>
    /// 【决策 A-305】结构化契约诊断信息
    /// </summary>
    public sealed record ContractDiagnostic
    {
        public string ContractName { get; init; }
        public string ErrorCode { get; init; }
        public string Message { get; init; }
        public DiagnosticSeverity Severity { get; init; }
        public string? PropertyName { get; init; }
        public string? PropertyPath { get; init; }
        public object[] ContextArgs { get; init; }

        public ContractDiagnostic(
            string contractName,
            string errorCode,
            string message,
            DiagnosticSeverity severity,
            string? propertyName = null,
            string? propertyPath = null,
            params object[] contextArgs)
        {
            ContractName = contractName ?? throw new ArgumentNullException(nameof(contractName));
            ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Severity = severity;
            PropertyName = propertyName;
            PropertyPath = propertyPath;
            ContextArgs = contextArgs ?? Array.Empty<object>();
        }

        public static ContractDiagnostic Create(
            string contractName,
            string errorCode,
            string? propertyName = null,
            string? propertyPath = null,
            params object[] contextArgs)
        {
            string message = ContractDiagnosticRegistry.Format(errorCode, contextArgs);
            var severity = ParseSeverity(ContractDiagnosticRegistry.GetSeverity(errorCode));

            return new ContractDiagnostic(
                contractName,
                errorCode,
                message,
                severity,
                propertyName,
                propertyPath,
                contextArgs
            );
        }

        public ContractIncompleteException ToException()
        {
            return new ContractIncompleteException(ContractName, ErrorCode, ContextArgs);
        }

        private static DiagnosticSeverity ParseSeverity(string severityText)
        {
            return severityText switch
            {
                "CRITICAL" => DiagnosticSeverity.Critical,
                "ERROR" => DiagnosticSeverity.Error,
                "WARNING" => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Error
            };
        }

        public override string ToString()
        {
            string location = !string.IsNullOrEmpty(PropertyPath)
                ? $" at {PropertyPath}"
                : !string.IsNullOrEmpty(PropertyName)
                    ? $".{PropertyName}"
                    : "";
            return $"[{Severity}] {ErrorCode}: {ContractName}{location}";
        }
    }

    public enum DiagnosticSeverity
    {
        Critical = 3,
        Error = 2,
        Warning = 1
    }
}


