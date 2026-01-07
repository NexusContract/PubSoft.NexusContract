# NexusContract.Core

**Gateway Engine** - The orchestration engine powering the four-phase pipeline (Validate → Project → Execute → Hydrate). High-performance metadata-driven architecture with startup health checks.

[**中文文档 / Chinese Documentation**](./README.zh-CN.md)

## 📦 Installation

```bash
dotnet add package NexusContract.Core
dotnet add package NexusContract.Abstractions
```

## 🚀 Quick Start

### Startup Health Check

```csharp
using NexusContract.Core.Reflection;

// Scan all contract types
var contractTypes = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => a.GetTypes())
    .Where(t => t.IsClass && !t.IsAbstract && 
                t.GetCustomAttribute<ApiOperationAttribute>() != null)
    .ToArray();

// Preload and validate
var report = NexusContractMetadataRegistry.Instance.Preload(contractTypes, warmup: true);

// Print diagnostic report
report.PrintToConsole(includeDetails: true);

// Abort if critical errors
if (report.HasCriticalErrors)
{
    Environment.Exit(1);
}
```

**Sample Output:**

```
╔══════════════════════════════════════════════════════════════════╗
║           NexusContract Diagnostic Report                       ║
║                    Startup Health Check                          ║
╠══════════════════════════════════════════════════════════════════╣
║ Status: ✅ HEALTHY                                               ║
║ Contracts Scanned: 6                                            ║
║ Total Issues: 0                                                  ║
║ Critical Errors: 0                                               ║
║ Warnings: 0                                                      ║
╚══════════════════════════════════════════════════════════════════╝
```

## ✨ Key Features

- **Four-Phase Pipeline**: Validate → Project → Execute → Hydrate
- **Dual-Mode Validator**: 
  - `Validate()` - Aggregates all errors (startup health check)
  - `ValidateFailFast()` - Throws on first error (runtime)
- **Frozen Metadata**: Zero reflection at runtime
- **FrozenDictionary**: Zero-allocation metadata lookups
- **Startup Diagnostics**: Detect contract errors before production

## 🔄 Four-Phase Pipeline

```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│  Validate   │ →  │   Project   │ →  │   Execute   │ →  │   Hydrate   │
│  (Static)   │    │  (Outbound) │    │  (Network)  │    │  (Inbound)  │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
     ↓                  ↓                  ↓                  ↓
Contract           C# → Dict          HTTP Call          Dict → C#
Validation         + Encryption       + Signing          + Decryption
```

### Usage Example

```csharp
using NexusContract.Core;
using NexusContract.Providers.Alipay;

// Create gateway
var namingPolicy = new SnakeCaseNamingPolicy();
var gateway = new NexusGateway(namingPolicy);

// Register provider
var provider = new AlipayProvider(appId, privateKey, publicKey);

// Execute request (all phases automatic)
var response = await gateway.ExecuteAsync(
    new TradeQueryRequest { TradeNo = "202501..." },
    async (context, dict) => await provider.CallAlipayAsync(context, dict)
);
```

## 📊 Performance Characteristics

**Benchmarks (.NET 10 Preview):**

| Operation | Mean | Allocated |
|-----------|------|-----------|
| Metadata Lookup (Frozen) | **8.2 ns** | **0 B** |
| Projection (Warm) | **31.7 ns** | **0 B** |
| Hydration | **48.1 ns** | **0 B** |
| Full Pipeline | **~120 ns** | **~200 B** |

**Optimizations:**
- ✅ FrozenDictionary for metadata (zero allocation)
- ✅ Precompiled IL for property access (zero reflection)
- ✅ Zero-copy projections where possible
- ✅ Frozen collections for routing

## 🏥 Diagnostic System

### Structured Error Reporting

```csharp
var report = NexusContractMetadataRegistry.Instance.Preload(types);

if (!report.IsHealthy)
{
    foreach (var diagnostic in report.Diagnostics)
    {
        Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}");
        Console.WriteLine($"  Contract: {diagnostic.ContractType}");
        Console.WriteLine($"  Message: {diagnostic.Message}");
    }
}
```

### Common Diagnostic Codes

| Code | Severity | Meaning |
|------|----------|---------|
| `NXC-001` | Error | Missing required attribute |
| `NXC-002` | Error | Invalid nesting depth |
| `NXC-003` | Warning | No properties marked with [ContractProperty] |
| `NXC-SIGN-001` | Error | Signature verification failed |

## 🎯 Target Framework

- **Requires**: .NET 10 (Preview)
- **Compatibility**: .NET 10+ applications only

## 📚 Related Packages

- **[NexusContract.Abstractions](https://www.nuget.org/packages/NexusContract.Abstractions)** - Core contracts and attributes
- **[NexusContract.Client](https://www.nuget.org/packages/NexusContract.Client)** - HTTP client SDK
- **[NexusContract.Providers.Alipay](https://www.nuget.org/packages/NexusContract.Providers.Alipay)** - Alipay provider

## 📖 Documentation

- [GitHub Repository](https://github.com/NexusContract/PubSoft.NexusContract)
- [Implementation Guide](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/docs/IMPLEMENTATION.md)

## 📄 License

MIT License - See [LICENSE](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/LICENSE)
