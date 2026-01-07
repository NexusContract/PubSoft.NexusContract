using System;
using System.Collections.Generic;

namespace NexusContract.Abstractions.Exceptions
{
    /// <summary>
    /// Fail-Fast Contract Enforcement: 契约不完整时抛出的异常
    /// 
    /// 结构化诊断模型：
    /// - ErrorCode: 唯一标识码（NXC1xx 静态/NXC2xx 运行期）
    /// - ContextArgs: 运行时上下文（类名、属性名、路径等）
    /// - ContractType: 违规的契约类型
    /// 
    /// 设计权衡：
    /// - ContractIncompleteException (本类): 用于运行时 Fail-Fast，立即中断非法操作。
    /// - ContractDiagnostic (记录): 用于启动期批量扫描，收集所有问题生成报告，不抛出异常。
    /// </summary>
    public class ContractIncompleteException : Exception
    {
        public string ContractType { get; }
        public string ErrorCode { get; }
        public object[] ContextArgs { get; }

        /// <summary>
        /// 创建诊断异常（使用错误码 + 上下文参数）
        /// </summary>
        public ContractIncompleteException(string contractType, string errorCode, params object[] contextArgs)
            : base(FormatMessage(errorCode, contextArgs))
        {
            ContractType = contractType;
            ErrorCode = errorCode;
            ContextArgs = contextArgs ?? Array.Empty<object>();
        }

        /// <summary>
        /// 创建诊断异常（包含内异常链）
        /// </summary>
        public ContractIncompleteException(string contractType, string errorCode, Exception innerException, params object[] contextArgs)
            : base(FormatMessage(errorCode, contextArgs), innerException)
        {
            ContractType = contractType;
            ErrorCode = errorCode;
            ContextArgs = contextArgs ?? Array.Empty<object>();
        }

        /// <summary>
        /// 兼容旧版本的自由形式消息构造器
        /// </summary>
        public ContractIncompleteException(string contractType, string message)
            : base($"Contract '{contractType}' is incomplete: {message}")
        {
            ContractType = contractType;
            ErrorCode = "UNKNOWN";
            ContextArgs = Array.Empty<object>();
        }

        public ContractIncompleteException(string contractType, string message, Exception innerException)
            : base($"Contract '{contractType}' is incomplete: {message}", innerException)
        {
            ContractType = contractType;
            ErrorCode = "UNKNOWN";
            ContextArgs = Array.Empty<object>();
        }

        /// <summary>
        /// 获取结构化的诊断数据（用于日志序列化）
        /// </summary>
        public Dictionary<string, object> GetDiagnosticData()
        {
            return new()
            {
                { "ErrorCode", ErrorCode },
                { "ContractType", ContractType },
                { "ContextArgs", ContextArgs },
                { "Message", Message }
            };
        }

        private static string FormatMessage(string errorCode, object[] contextArgs)
        {
            // 延迟到 ContractDiagnosticRegistry 进行格式化
            return ContractDiagnosticRegistry.Format(errorCode, contextArgs ?? Array.Empty<object>());
        }
    }
}


