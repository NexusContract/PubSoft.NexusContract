# NexusContract.Providers.Alipay

**Production-Ready Alipay Provider** - Complete Alipay OpenAPI v3 (RESTful) integration with RSA2 signing, automatic signature verification, and zero-code endpoint support.

[**中文文档 / Chinese Documentation**](./README.zh-CN.md)

## 📦 Installation

```bash
dotnet add package NexusContract.Providers.Alipay
dotnet add package NexusContract.Abstractions
```

## 🚀 Quick Start

### Basic Usage (Direct Integration)

```csharp
using NexusContract.Providers.Alipay;

var provider = new AlipayProvider(
    appId: "2021001234567890",
    merchantPrivateKey: "MIIEvQ...your-private-key...",
    alipayPublicKey: "MIIBIj...alipay-public-key..."
);

// Execute request - method auto-extracted from [NexusContract] attribute
var response = await provider.ExecuteAsync(
    new TradeQueryRequest 
    { 
        TradeNo = "2025011234567890" 
    });

Console.WriteLine($"Status: {response.TradeStatus}");
Console.WriteLine($"Amount: {response.TotalAmount}");
```

### ASP.NET Core Integration (FastEndpoints)

```csharp
// Program.cs - Register provider
builder.Services.AddSingleton<AlipayProvider>(sp =>
    new AlipayProvider(
        appId: builder.Configuration["Alipay:AppId"]!,
        merchantPrivateKey: builder.Configuration["Alipay:MerchantPrivateKey"]!,
        alipayPublicKey: builder.Configuration["Alipay:AlipayPublicKey"]!
    ));

// Zero-code endpoint - route auto-inferred as POST /trade/query
public sealed class TradeQueryEndpoint(AlipayProvider provider) 
    : AlipayEndpointBase<TradeQueryRequest>(provider) { }
```

## ✨ Key Features

- **OpenAPI v3 Support**: Full support for Alipay's RESTful API
- **RSA2 Signing**: Automatic request signing with RSA2 algorithm
- **Signature Verification**: Automatic response signature verification
- **Zero-Code Endpoints**: Inherit `AlipayEndpointBase<TRequest>` for FastEndpoints
- **Type-Safe**: Strong typing with `IApiRequest<TResponse>`
- **Error Handling**: Structured error responses with diagnostic codes

## 🔐 Security Features

- ✅ **RSA2 Signature**: All requests signed with merchant private key
- ✅ **Response Verification**: Alipay responses verified with Alipay public key
- ✅ **HTTPS Only**: Production gateway uses HTTPS
- ✅ **No Plain Text Secrets**: Keys loaded from secure configuration

## 🏗️ Architecture

```
┌─────────────────────────────────────┐
│   Your Application / HttpApi         │
└─────────────────────────────────────┘
         ↓ uses
┌─────────────────────────────────────┐
│   AlipayProvider                     │  ← THIS PACKAGE
├─────────────────────────────────────┤
│  - Request Signing (RSA2)            │
│  - Response Verification             │
│  - HTTP Communication                │
│  - Error Translation                 │
└─────────────────────────────────────┘
         ↓ HTTP/HTTPS
┌─────────────────────────────────────┐
│   Alipay OpenAPI v3                  │
│   (https://openapi.alipay.com)       │
└─────────────────────────────────────┘
```

## 📋 Supported APIs

| API Method | Contract | Description |
|------------|----------|-------------|
| `alipay.trade.pay` | `TradePayRequest` | Barcode/QR code payment |
| `alipay.trade.create` | `TradeCreateRequest` | Create trade order |
| `alipay.trade.query` | `TradeQueryRequest` | Query trade status |
| `alipay.trade.refund` | `TradeRefundRequest` | Refund transaction |
| `alipay.trade.close` | `TradeCloseRequest` | Close unpaid order |
| `alipay.trade.precreate` | `TradePrecreateRequest` | Generate QR code for scanning |

## 🔧 Configuration

### appsettings.json

```json
{
  "Alipay": {
    "AppId": "2021001234567890",
    "MerchantPrivateKey": "MIIEvQ...",
    "AlipayPublicKey": "MIIBIj...",
    "GatewayUrl": "https://openapi.alipay.com/gateway.do",
    "SignType": "RSA2"
  }
}
```

### Environment-Specific Configuration

```csharp
// Development - Use sandbox
var provider = new AlipayProvider(
    appId: "2021...",
    merchantPrivateKey: "MII...",
    alipayPublicKey: "MII...",
    gatewayUrl: "https://openapi.alipaydev.com/gateway.do"  // Sandbox
);

// Production - Use production gateway
var provider = new AlipayProvider(
    appId: "2021...",
    merchantPrivateKey: "MII...",
    alipayPublicKey: "MII...",
    gatewayUrl: "https://openapi.alipay.com/gateway.do"  // Production (default)
);
```

## 📖 Contract Examples

See complete contract definitions in:
- [examples/Demo.Alipay.Contract/Transactions/](https://github.com/NexusContract/PubSoft.NexusContract/tree/main/examples/Demo.Alipay.Contract/Transactions)

### Sample Contract

```csharp
[NexusContract(Method = "alipay.trade.pay")]
[ApiOperation(Operation = "trade/pay", Verb = HttpVerb.POST)]
public sealed class TradePayRequest : IApiRequest<TradePayResponse>
{
    [ContractProperty(Name = "out_trade_no", Order = 1, Required = true)]
    public string OutTradeNo { get; set; } = string.Empty;

    [ContractProperty(Name = "scene", Order = 2, Required = true)]
    public string Scene { get; set; } = "bar_code";

    [ContractProperty(Name = "auth_code", Order = 3, Required = true)]
    public string AuthCode { get; set; } = string.Empty;

    [ContractProperty(Name = "subject", Order = 4, Required = true)]
    public string Subject { get; set; } = string.Empty;

    [ContractProperty(Name = "total_amount", Order = 5, Required = true)]
    public decimal TotalAmount { get; set; }
}
```

## 🎯 Target Framework

- **Requires**: .NET 10 (Preview)
- **Compatibility**: .NET 10+ applications only

## 📚 Related Packages

- **[NexusContract.Abstractions](https://www.nuget.org/packages/NexusContract.Abstractions)** - Core contracts and attributes
- **[NexusContract.Core](https://www.nuget.org/packages/NexusContract.Core)** - Gateway engine
- **[NexusContract.Client](https://www.nuget.org/packages/NexusContract.Client)** - HTTP client SDK (for BFF layer)

## 📖 Documentation

- [GitHub Repository](https://github.com/NexusContract/PubSoft.NexusContract)
- [Implementation Guide](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/docs/IMPLEMENTATION.md)
- [Alipay API Docs](https://github.com/NexusContract/PubSoft.NexusContract/tree/main/examples/Api-docs)

## 📄 License

MIT License - See [LICENSE](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/LICENSE)


