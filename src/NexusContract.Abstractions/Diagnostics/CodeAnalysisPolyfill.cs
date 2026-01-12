// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Polyfill for C# 11 [DoesNotReturn] 属性。
    /// 
    /// 在 .NET Standard 2.0 环境下，此属性帮助 C# 编译器进行"不可到达代码"分析。
    /// 编译器会自动识别此属性，即使它不在 BCL 中也能提供分析支持。
    /// 
    /// 设计原则：
    /// - 编译器只需要"看到"属性的定义即可，不需要运行时支持
    /// - 对于 .NET 6+ 的用户，此定义会被忽略（因为 BCL 已包含）
    /// - 对于 .NET Standard 2.0，此定义确保完整的静态分析能力
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class DoesNotReturnAttribute : Attribute
    {
    }

    /// <summary>
    /// Polyfill for C# 10 [CallerArgumentExpression] 属性。
    /// 
    /// 在 .NET Standard 2.0 环境下，此属性使编译器能够自动填充参数的源代码表达式。
    /// 
    /// 使用场景：
    /// - 避免手动传递 `nameof(param)`，代码自动捕获参数名
    /// - 减少调用点的代码冗余
    /// - 提升代码清晰度
    /// 
    /// 示例：
    /// ```csharp
    /// public static void Guard(
    ///     object? value,
    ///     [CallerArgumentExpression("value")] string paramName = "")
    /// {
    ///     if (value == null)
    ///         throw new ArgumentNullException(paramName);
    /// }
    ///
    /// Guard(someObject);  // 编译器自动将 "someObject" 作为参数名传入
    /// ```
    ///
    /// 设计原则：
    /// - 此属性由 C# 10 编译器在编译时处理，无运行时开销
    /// - 仅在编译器支持的版本（C# 10+）下起作用
    /// - 对于较旧的编译器，默认参数值 "" 保证向后兼容
    /// </summary>
    /// <remarks>
    /// 初始化新的 CallerArgumentExpressionAttribute 实例。
    /// </remarks>
    /// <param name="parameterName">目标参数的名称，其表达式将被捕获。</param>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
    {

        /// <summary>
        /// 获取目标参数的名称。
        /// </summary>
        public string ParameterName { get; } = parameterName;
    }
}
