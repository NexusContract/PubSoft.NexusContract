// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;
using NexusContract.Core.Utilities;

namespace NexusContract.Core.Tests.Utilities;

/// <summary>
/// TypeUtilities 单元测试
/// 
/// 【宪法生死线】这些测试验证的是全系统递归处理的"逻辑分界线"。
/// 任何改动 IsComplexType、IsCollectionType、IsNullable 的逻辑，
/// 都必须通过这些测试，否则将导致投影/回填引擎行为混乱。
/// 
/// 测试范围：
/// - 基元类型：int, bool, long, double
/// - 框架类型：DateTime, Guid, TimeSpan, Decimal
/// - 字符串：特殊的"简单引用类型"
/// - 集合：List<T>, Dictionary<K,V>, IEnumerable
/// - 自定义POCO：演示"复杂类型"
/// </summary>
public class TypeUtilitiesTests
{
    #region IsComplexType 测试

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    public void IsComplexType_PrimitiveTypes_ShouldReturnFalse(Type type)
    {
        // Act
        bool result = TypeUtilities.IsComplexType(type);

        // Assert
        Assert.False(result, $"Primitive type {type.Name} should NOT be complex");
    }

    [Fact]
    public void IsComplexType_String_ShouldReturnFalse()
    {
        // Act
        bool result = TypeUtilities.IsComplexType(typeof(string));

        // Assert - String is treated as simple type despite being reference type
        Assert.False(result, "String should be treated as simple type");
    }

    [Theory]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(TimeSpan))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(DateTimeOffset))]
    public void IsComplexType_SystemFrameworkTypes_ShouldReturnFalse(Type type)
    {
        // Act
        bool result = TypeUtilities.IsComplexType(type);

        // Assert - All System.* types are excluded
        Assert.False(result, $"System type {type.Name} should NOT be complex (宪法 boundary)");
    }

    [Fact]
    public void IsComplexType_Dictionary_ShouldReturnFalse()
    {
        // Act
        bool result = TypeUtilities.IsComplexType(typeof(Dictionary<string, object>));

        // Assert - Dictionary is protocol layer, not contract object
        Assert.False(result, "Dictionary should not be treated as complex type (protocol layer)");
    }

    [Fact]
    public void IsComplexType_IDictionary_ShouldReturnFalse()
    {
        // Act
        bool result = TypeUtilities.IsComplexType(typeof(IDictionary));

        // Assert
        Assert.False(result, "IDictionary should not be treated as complex type");
    }

    [Fact]
    public void IsComplexType_UserDefinedCollectionSubclass_ShouldReturnTrue()
    {
        // Arrange - User-defined collection (not in System namespace)
        var userCollectionType = typeof(CustomContractCollection);

        // Act
        bool result = TypeUtilities.IsComplexType(userCollectionType);

        // Assert
        Assert.True(result, "User-defined collection subclass should be complex");
    }

    [Theory]
    [InlineData(typeof(List<>))]
    public void IsComplexType_SystemGenericCollectionTypes_ShouldReturnFalse(Type type)
    {
        // Note: System.Collections.Generic types are in System namespace, so filtered out
        // Act
        bool result = TypeUtilities.IsComplexType(type);

        // Assert
        Assert.False(result, $"System generic collection {type.Name} should be filtered (System namespace)");
    }

    [Fact]
    public void IsComplexType_CustomPocoClass_ShouldReturnTrue()
    {
        // Arrange - Simple POCO class (not System.*, not Dictionary, not primitive)
        var pocoType = typeof(SampleContract);

        // Act
        bool result = TypeUtilities.IsComplexType(pocoType);

        // Assert
        Assert.True(result, "User-defined POCO should be treated as complex type");
    }

    [Fact]
    public void IsComplexType_Null_ShouldReturnFalse()
    {
        // Act
        bool result = TypeUtilities.IsComplexType(null!);

        // Assert
        Assert.False(result, "Null type should return false");
    }

    #endregion

    #region IsCollectionType 测试

    [Theory]
    [InlineData(typeof(List<int>))]
    [InlineData(typeof(IEnumerable<string>))]
    [InlineData(typeof(IList))]
    [InlineData(typeof(int[]))]
    public void IsCollectionType_CollectionTypes_ShouldReturnTrue(Type type)
    {
        // Act
        bool result = TypeUtilities.IsCollectionType(type);

        // Assert
        Assert.True(result, $"Collection type {type.Name} should be recognized");
    }

    [Fact]
    public void IsCollectionType_String_ShouldReturnFalse()
    {
        // Act - String implements IEnumerable but is treated as simple type
        bool result = TypeUtilities.IsCollectionType(typeof(string));

        // Assert
        Assert.False(result, "String should NOT be treated as collection (special case)");
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(SampleContract))]
    public void IsCollectionType_NonCollectionTypes_ShouldReturnFalse(Type type)
    {
        // Act
        bool result = TypeUtilities.IsCollectionType(type);

        // Assert
        Assert.False(result, $"Non-collection type {type.Name} should return false");
    }

    #endregion

    #region IsNullable 测试

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(object))]
    [InlineData(typeof(List<int>))]
    [InlineData(typeof(DateTime?))]
    public void IsNullable_ReferenceOrNullableTypes_ShouldReturnTrue(Type type)
    {
        // Act
        bool result = TypeUtilities.IsNullable(type);

        // Assert
        Assert.True(result, $"Type {type.Name} should be nullable");
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(DateTime))]
    public void IsNullable_PrimitiveValueTypes_ShouldReturnFalse(Type type)
    {
        // Act
        bool result = TypeUtilities.IsNullable(type);

        // Assert
        Assert.False(result, $"Non-nullable value type {type.Name} should return false");
    }

    #endregion

    #region 集成测试：投影/回填边界确认

    [Fact]
    public void BoundaryTest_SystemVsUserNamespaces()
    {
        // Arrange
        var userType = typeof(SampleContract);
        var systemType = typeof(DateTime);
        var frameworkType = typeof(Uri);

        // Act & Assert
        Assert.True(TypeUtilities.IsComplexType(userType), "User namespace types should be complex");
        Assert.False(TypeUtilities.IsComplexType(systemType), "System.DateTime should be simple");
        Assert.False(TypeUtilities.IsComplexType(frameworkType), "System.Uri should be simple");

        // 【宪法 确认】这个边界是全系统递归处理的生死线
    }

    #endregion

    // 测试用的简单 POCO 类
    public class SampleContract
    {
        public string Name { get; set; } = string.Empty;
        public int Amount { get; set; }
        public NestedContract? Nested { get; set; }
    }

    public class NestedContract
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // 用户自定义集合类（不在 System 命名空间中）
    public class CustomContractCollection : List<SampleContract>
    {
    }
}
