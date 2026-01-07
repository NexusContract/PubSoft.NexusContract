# NexusContract.Abstractions

**Constitutional Layer** - Core abstractions, contracts, and attributes for the NexusContract framework. Zero runtime dependencies, targets netstandard2.0 for maximum compatibility.

[**中文文档 / Chinese Documentation**](./README.zh-CN.md)

## 📦 Installation

```bash
dotnet add package NexusContract.Abstractions
```

## 🚀 Quick Start

### Define a Contract

```csharp
using NexusContract.Abstractions;

[NexusContract(Method = "alipay.trade.query")]
[ApiOperation(Operation = "trade/query", Verb = HttpVerb.POST)]
public sealed class TradeQueryRequest : IApiRequest<TradeQueryResponse>
{
    [ContractProperty(Name = "out_trade_no", Order = 1)]
    public string? OutTradeNo { get; set; }

    [ContractProperty(Name = "trade_no", Order = 2, Required = true)]
    public string TradeNo { get; set; } = string.Empty;
}

public sealed class TradeQueryResponse
{
    [ContractProperty(Name = "trade_status")]
    public string? TradeStatus { get; set; }

    [ContractProperty(Name = "total_amount")]
    public decimal? TotalAmount { get; set; }
}
```

## ✨ Key Features

- **Zero Dependencies**: No external package dependencies
- **netstandard2.0 Compatible**: Works with .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+
- **Attribute-Driven**: Declarative contract definitions
- **Type-Safe**: Strong typing with `IApiRequest<TResponse>`
- **Metadata-Rich**: Full support for naming policies, encryption, validation

## 📚 Core Abstractions

### Attributes

- `[NexusContract]` - Marks a class as a contract (defines method/operation)
- `[ContractProperty]` - Defines property mapping rules (name, order, required)
- `[ApiOperation]` - Defines HTTP operation metadata (route, verb)
- `[ProviderMetadata]` - Provider-specific configuration
- `[Encrypted]` - Marks field for encryption/decryption

### Contracts

- `IApiRequest<TResponse>` - Request contract interface
- `IContractSerializer` - Custom serialization logic
- `ISignaturePolicy` - Signature algorithm abstraction
- `INamingPolicy` - Property name conversion (snake_case, camelCase, etc.)

### Exceptions

- `ContractIncompleteException` - Contract validation failure
- `NexusCommunicationException` - HTTP/communication errors
- All exceptions include structured diagnostic data

## 🎯 Design Philosophy

1. **Explicit Boundaries**: No magic, no implicit behavior
2. **Compile-Time Safety**: Strong typing, interface contracts
3. **Zero Runtime Dependencies**: Can be referenced by any .NET project
4. **Metadata-Driven**: All behavior defined declaratively

## 🏗️ Architecture Position

```
┌──────────────────────────────────┐
│  Your Application                │
└──────────────────────────────────┘
         ↓ depends on
┌──────────────────────────────────┐
│  NexusContract.Core              │  ← Engine
└──────────────────────────────────┘
         ↓ depends on
┌──────────────────────────────────┐
│  NexusContract.Abstractions      │  ← THIS PACKAGE
│  (netstandard2.0, zero deps)     │
└──────────────────────────────────┘
```

## 📚 Related Packages

- **[NexusContract.Core](https://www.nuget.org/packages/NexusContract.Core)** - Gateway engine and pipeline
- **[NexusContract.Client](https://www.nuget.org/packages/NexusContract.Client)** - HTTP client SDK
- **[NexusContract.Providers.Alipay](https://www.nuget.org/packages/NexusContract.Providers.Alipay)** - Alipay provider

## 📖 Documentation

- [GitHub Repository](https://github.com/NexusContract/PubSoft.NexusContract)
- [Implementation Guide](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/docs/IMPLEMENTATION.md)
- [Constitutional Document](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/src/NexusContract.Abstractions/CONSTITUTION.md)

## 📄 License

MIT License - See [LICENSE](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/LICENSE)
