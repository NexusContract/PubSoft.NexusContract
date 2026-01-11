// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using NexusContract.Abstractions.Attributes;
using NexusContract.Core.Hydration;
using NexusContract.Core.Hydration.Compilers;
using NexusContract.Abstractions.Policies;

namespace NexusContract.Core.Tests.Hydration.Compilers
{
    /// <summary>
    /// IL 编译器性能验证测试
    /// 
    /// 目的：验证编译后的委托能够正常工作
    /// 注：真实性能测试应使用 BenchmarkDotNet（参见 NexusContract.Benchmarks）
    /// 
    /// 关于 ApiField：
    /// 【重要】ApiField 属性**不是必须的**，仅在以下情况使用：
    /// 
    ///   1. 需要显式指定字段名
    ///      [ApiField("explicit_field_name")]
    ///      public string FieldName { get; set; }
    /// 
    ///   2. 标记字段为加密（此时**必须**指定 Name）- 宪法 R-201
    ///      [ApiField("encrypted_field", IsEncrypted = true)]
    ///      public string SecretData { get; set; }
    /// 
    ///   3. 标记字段为必填
    ///      [ApiField(IsRequired = true)]
    ///      public long TransactionId { get; set; }
    /// 
    /// 默认情况：
    /// - 属性名由 INamingPolicy 自动转换为协议字段名
    /// - 例如：UserName → user_name（snake_case）
    /// 
    /// 参考：
    /// - TestResponseTypes.cs - 各种 ApiField 使用示例
    /// - APIFIELD_USAGE_GUIDE.md - ApiField 详细使用指南
    /// - ApiFieldAttribute.cs - 官方定义和约束（宪法 R-201）
    /// </summary>
    public class ILCompilerPerformanceTests
    {
        [Fact]
        public void RobustConvertCompiler_Compile_Long_ShouldWork()
        {
            // Arrange
            var targetType = typeof(long);
            var value = "12345";

            // Act
            var converter = RobustConvertCompiler.Compile(targetType);
            var result = converter(value, targetType);

            // Assert
            Assert.Equal(12345L, (long)result);
        }

        [Fact]
        public void RobustConvertCompiler_Compile_Decimal_ShouldWork()
        {
            // Arrange
            var targetType = typeof(decimal);
            var value = "123.45";

            // Act
            var converter = RobustConvertCompiler.Compile(targetType);
            var result = converter(value, targetType);

            // Assert
            Assert.Equal(123.45m, (decimal)result);
        }

        [Fact]
        public void RobustConvertCompiler_Compile_DateTime_ShouldWork()
        {
            // Arrange
            var targetType = typeof(DateTime);
            var value = "2025-01-25";

            // Act
            var converter = RobustConvertCompiler.Compile(targetType);
            var result = converter(value, targetType);

            // Assert
            Assert.IsType<DateTime>(result);
        }

        [Fact]
        public void RobustConvertCompiler_Compile_Bool_ShouldWork()
        {
            // Arrange
            var targetType = typeof(bool);
            var value = "true";

            // Act
            var converter = RobustConvertCompiler.Compile(targetType);
            var result = converter(value, targetType);

            // Assert
            Assert.True((bool)result);
        }

        [Fact]
        public void RobustConvertCompiler_CachesConverters()
        {
            // Arrange
            var targetType = typeof(long);

            // Act
            var converter1 = RobustConvertCompiler.Compile(targetType);
            var converter2 = RobustConvertCompiler.Compile(targetType);

            // Assert - 应该返回相同的缓存实例
            Assert.Same(converter1, converter2);
        }

        [Fact]
        public void RobustConvertCompiler_Performance_BaseLine()
        {
            // 这是一个基准测试框架
            // 真实性能测试应该在 NexusContract.Benchmarks 中执行

            const int iterations = 10000;
            var targetType = typeof(long);
            var compiler = RobustConvertCompiler.Compile(targetType);
            var values = new[] { "123", "456", "789" };

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var result = compiler(values[i % 3], targetType);
                _ = (long)result;
            }
            stopwatch.Stop();

            // 验证不出错即可，真实性能由 BenchmarkDotNet 衡量
            Assert.True(stopwatch.ElapsedMilliseconds >= 0);
        }

        // 默认命名策略（用于测试）
        public class DefaultNamingPolicy : INamingPolicy
        {
            public string ConvertName(string name)
            {
                // 简单实现：直接返回小写
                return name?.ToLowerInvariant() ?? name;
            }
        }
    }
}
