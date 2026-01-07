# NexusContract.Abstractions

> **宪法层 (Constitution Layer)** - 纯净的契约抽象与边界定义

[![NuGet](https://img.shields.io/nuget/v/NexusContract.Abstractions.svg)](https://www.nuget.org/packages/NexusContract.Abstractions/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## 📦 这个包包含什么？

这是 NexusContract 框架的**基础依赖层**，包含：

- **契约接口**: `IApiRequest<TResponse>` - 强类型请求/响应绑定
- **标注属性**: `[ApiOperation]`, `[ApiField]` - 声明式契约定义
- **命名策略**: `INamingPolicy` - 字段名转换抽象
- **加密抽象**: `IEncryptor`, `IDecryptor` - 敏感数据处理
- **诊断码**: `ContractDiagnosticRegistry` - 结构化错误索引 (NXC1xx-3xx)
- **边界配置**: `ContractBoundaries` - 物理红线（最大深度、循环检测等）

## 🎯 适用场景

- ✅ **定义业务契约** (Contract POCO)
- ✅ **多 Provider 共享** (Alipay, UnionPay, WeChat)
- ✅ **跨 .NET 版本兼容** (netstandard2.0)

## 🚀 快速开始

### 安装

```bash
dotnet add package NexusContract.Abstractions
```

### 定义你的第一个契约

```csharp
using NexusContract.Abstractions.Attributes;
using NexusContract.Abstractions.Contracts;

[ApiOperation("alipay.trade.pay", HttpVerb.POST)]
public class TradePayRequest : IApiRequest<TradePayResponse>
{
    [ApiField("out_trade_no", IsRequired = true)]
    public string MerchantOrderNo { get; set; }
    
    [ApiField("total_amount", IsRequired = true)]
    public decimal TotalAmount { get; set; }
}

public class TradePayResponse
{
    public string TradeNo { get; set; }
    public string TradeStatus { get; set; }
}
```

## 🏛️ 设计哲学

> **"显式边界优于隐式魔法"**

- **零运行时依赖**: 不依赖任何第三方包
- **纯净抽象**: 只有接口和 Attribute，无行为实现
- **架构约束**: 通过诊断码 (NXC1xx) 强制执行设计边界

## 📚 文档

- [架构宪法](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/src/NexusContract.Abstractions/CONSTITUTION.md)
- [NXC 诊断码详解](https://github.com/NexusContract/PubSoft.NexusContract#-结构化诊断码-nxc-codes)

## 🔗 相关包

- **NexusContract.Core** - 核心引擎实现 (.NET 10)
- **NexusContract.Providers.Alipay** - 支付宝提供商

## 📄 许可

MIT License - 查看 [LICENSE](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/LICENSE)
