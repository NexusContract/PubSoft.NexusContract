# NexusContract.Providers.Alipay

> **支付宝提供商** - 开箱即用的支付宝 OpenAPI v3 集成

[![NuGet](https://img.shields.io/nuget/v/NexusContract.Providers.Alipay.svg)](https://www.nuget.org/packages/NexusContract.Providers.Alipay/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## 📦 这个包包含什么？

完整的支付宝 Provider 实现：

- **AlipayProvider**: 支付宝网关集成（RSA2 签名、OpenAPI v3）
- **AlipayProviderConfig**: 配置模型（AppId, 私钥, 公钥等）
- **DI 扩展**: `AddAlipayProvider()` - 一行代码完成注册

## 🎯 适用场景

- ✅ **支付宝当面付** (扫码、刷卡、声波)
- ✅ **支付宝线上支付** (APP、Web、H5)
- ✅ **支付宝交易管理** (查询、退款、关闭)

## 🚀 快速开始

### 安装

```bash
dotnet add package NexusContract.Providers.Alipay
```

### ASP.NET Core 集成（FastEndpoints）

```csharp
using NexusContract.Providers.Alipay;
using NexusContract.Providers.Alipay.ServiceConfiguration;

var builder = WebApplication.CreateBuilder(args);

// 注册支付宝提供商
builder.Services.AddAlipayProvider(new AlipayProviderConfig
{
    AppId = "2021xxx",
    MerchantId = "2088xxx",
    PrivateKey = "MIIEvQIBA...", // 商户 RSA 私钥
    AlipayPublicKey = "MIIBIjANBgkqh...", // 支付宝 RSA 公钥
    ApiGateway = new Uri("https://openapi.alipay.com/"),
    UseSandbox = false
});

builder.Services.AddFastEndpoints();
var app = builder.Build();

app.UseFastEndpoints(c => c.Endpoints.RoutePrefix = "v3/alipay");
app.Run();
```

### 定义契约（放在独立的 Contract 项目中）

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
    
    [ApiField("subject", IsRequired = true)]
    public string Subject { get; set; }
    
    [ApiField("scene", IsRequired = true)]
    public string Scene { get; set; } // bar_code, qr_code
}
```

### 创建零代码端点

```csharp
using Demo.Alipay.Contract.Transactions;
using NexusContract.Providers.Alipay;

public class TradePayEndpoint(AlipayProvider provider) 
    : AlipayEndpointBase<TradePayRequest>(provider)
{
    // 零代码！路由、请求处理、响应序列化全部自动完成
}
```

## 🔐 安全特性

- **RSA2 签名**: 所有请求自动签名，所有响应自动验签
- **密钥隔离**: 私钥仅用于签名，公钥仅用于验签
- **HTTPS 强制**: 生产环境强制 HTTPS

## 📚 支持的接口

- ✅ `alipay.trade.pay` - 统一收单交易支付
- ✅ `alipay.trade.create` - 统一收单交易创建
- ✅ `alipay.trade.query` - 统一收单交易查询
- ✅ `alipay.trade.refund` - 统一收单交易退款
- ✅ `alipay.trade.close` - 统一收单交易关闭
- ✅ `alipay.trade.precreate` - 统一收单线下交易预创建

## 🔗 相关包

- **NexusContract.Core** - 核心引擎（必需依赖）
- **NexusContract.Abstractions** - 基础抽象（传递依赖）

## 📖 完整示例

- **Contract 定义**: [examples/Demo.Alipay.Contract](https://github.com/NexusContract/PubSoft.NexusContract/tree/main/examples/Demo.Alipay.Contract)
- **Endpoint 实现**: [examples/Demo.Alipay.HttpApi](https://github.com/NexusContract/PubSoft.NexusContract/tree/main/examples/Demo.Alipay.HttpApi)

## 📄 许可

MIT License - 查看 [LICENSE](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/LICENSE)


