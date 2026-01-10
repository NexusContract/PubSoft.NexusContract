# NexusContract.Core

> **引擎层 (Engine Layer)** - 元数据驱动的高性能执行引擎

[![NuGet](https://img.shields.io/nuget/v/NexusContract.Core.svg)](https://www.nuget.org/packages/NexusContract.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## 📦 这个包包含什么？

NexusContract 的**核心执行引擎**，实现四阶段管道：

- **NexusGateway**: 指挥中心，协调所有阶段
- **ContractValidator**: 启动期体检 + 运行期 Fail-Fast 双模验证
- **NexusContractMetadataRegistry**: 元数据冷冻与缓存（启动期反射，运行期 O(1)）
- **ProjectionEngine**: POCO → Dictionary 投影（支持嵌套、加密、命名策略）
- **ResponseHydrationEngine**: Dictionary → POCO 回填（强制类型纠偏）
- **DiagnosticReport**: 结构化启动期诊断报告

## 🎯 适用场景

- ✅ **构建 Provider** (如 AlipayProvider, UnionPayProvider)
- ✅ **实现 Gateway** (协调四阶段管道)
- ✅ **启动期契约体检** (Preload + DiagnosticReport)

## 🚀 快速开始

### 安装

```bash
dotnet add package NexusContract.Core
```

### 启动期契约体检

```csharp
using NexusContract.Core.Reflection;

// 扫描所有契约类型
var types = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => a.GetTypes())
    .Where(t => t.GetCustomAttribute<ApiOperationAttribute>() != null)
    .ToArray();

// 执行全景无损扫描
var report = NexusContractMetadataRegistry.Instance.Preload(types, warmup: true);

// 打印诊断报告
report.PrintToConsole(includeDetails: true);

if (report.HasCriticalErrors)
{
    Console.Error.WriteLine("❌ 检测到致命契约错误，中止启动。");
    Environment.Exit(1);
}
```

## ⚡ 性能特性

- **元数据冷冻**: 启动期一次性反射，运行期 O(1) 查询
- **预编译投影**: Expression Tree 预编译，避免运行时反射
- **确定性 P99**: GC 优化设计，平滑延迟曲线

## 🏛️ 四阶段管道

```
Contract (Input)
    ↓
1️⃣ Validate   → ContractValidator (NXC1xx)
    ↓
2️⃣ Project    → ProjectionEngine (POCO → Dict)
    ↓
3️⃣ Execute    → Provider.HttpExecutor (HTTP + Sign)
    ↓
4️⃣ Hydrate    → ResponseHydrationEngine (Dict → POCO)
    ↓
Response (Output)
```

## 📚 文档

- [实现章法](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/docs/IMPLEMENTATION.md)
- [启动期体检指南](https://github.com/NexusContract/PubSoft.NexusContract#-快速上手启动期体检)

## 🔗 相关包

- **NexusContract.Abstractions** - 基础抽象层（必需依赖）
- **NexusContract.Providers.Alipay** - 支付宝提供商实现

## 📄 许可

MIT License - 查看 [LICENSE](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/LICENSE)
