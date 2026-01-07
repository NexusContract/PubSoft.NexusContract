# NexusContract Implementation Guide

[**中文文档 / Chinese Documentation**](./IMPLEMENTATION.zh-CN.md)

## 📖 Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Four-Phase Pipeline](#four-phase-pipeline)
3. [Startup Health Check](#startup-health-check)
4. [Layer 1: HttpApi (FastEndpoints + Provider)](#layer-1-httpapi)
5. [Layer 2: BFF/Business Layer (Client)](#layer-2-bff-business-layer)
6. [Layer 3: Direct Integration (Provider Only)](#layer-3-direct-integration)
7. [Complete Alipay Integration Example](#complete-example)
8. [Performance Optimization](#performance-optimization)
9. [Error Handling & Diagnostics](#error-handling)

## 🏗️ Architecture Overview

NexusContract implements a **three-layer architecture** pattern:

```
┌─────────────────────────────────────────────┐
│ Layer 2: BFF / Business Layer               │
│ • Uses NexusGatewayClient                   │
│ • HTTP communication to HttpApi             │
└─────────────────────────────────────────────┘
          ↓ HTTP
┌─────────────────────────────────────────────┐
│ Layer 1: HttpApi                            │
│ • FastEndpoints (zero-code endpoints)       │
│ • Direct Provider calls                     │
└─────────────────────────────────────────────┘
          ↓ Direct Call
┌─────────────────────────────────────────────┐
│ Layer 0: Provider                           │
│ • AlipayProvider (OpenAPI v3)               │
│ • Signing, encryption, protocol conversion  │
└─────────────────────────────────────────────┘
          ↓ HTTP/HTTPS
┌─────────────────────────────────────────────┐
│ External API (e.g., Alipay OpenAPI)         │
└─────────────────────────────────────────────┘
```

**OR Direct Integration (Skip HttpApi):**

```
┌─────────────────────────────────────────────┐
│ Your Application                            │
│ • Direct AlipayProvider usage               │
└─────────────────────────────────────────────┘
          ↓ Direct Call
┌─────────────────────────────────────────────┐
│ Provider Layer                              │
└─────────────────────────────────────────────┘
          ↓ HTTP/HTTPS
┌─────────────────────────────────────────────┐
│ External API                                │
└─────────────────────────────────────────────┘
```

## 🔄 Four-Phase Pipeline

Every request goes through four standardized phases:

1. **Validate**: Contract validation (attributes, nesting depth, required fields)
2. **Project**: Transform C# object → Dictionary (key naming, encryption)
3. **Execute**: Call external API (signing, HTTP communication)
4. **Hydrate**: Transform Dictionary → C# object (decryption, deserialization)

```csharp
// Simplified pipeline illustration
var metadata = Registry.GetMetadata(typeof(TRequest));  // Phase 1: Validate
var dict = Projector.Project(request);                  // Phase 2: Project
var response = await Provider.Execute(dict);            // Phase 3: Execute
var result = Hydrator.Hydrate<TResponse>(response);     // Phase 4: Hydrate
```

## 🏥 Startup Health Check

**Why?** Detect contract errors at startup, not in production.

```csharp
// In Program.cs or Startup.cs
var types = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => a.GetTypes())
    .Where(t => t.IsClass && !t.IsAbstract && 
                t.GetCustomAttribute<ApiOperationAttribute>() != null)
    .ToArray();

var report = NexusContractMetadataRegistry.Instance.Preload(types, warmup: true);

report.PrintToConsole(includeDetails: true);

if (report.HasCriticalErrors)
{
    Console.WriteLine("❌ Service cannot start due to contract errors.");
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

## 🎯 Layer 1: HttpApi (FastEndpoints + Provider)

### Define Contract

```csharp
using NexusContract.Abstractions;

[NexusContract(Method = "alipay.trade.query")]
[ApiOperation(Operation = "trade/query", Verb = HttpVerb.POST)]
public sealed class TradeQueryRequest : IApiRequest<TradeQueryResponse>
{
    [ContractProperty(Name = "out_trade_no", Order = 1)]
    public string? OutTradeNo { get; set; }

    [ContractProperty(Name = "trade_no", Order = 2)]
    public string? TradeNo { get; set; }
}

public sealed class TradeQueryResponse
{
    [ContractProperty(Name = "trade_status")]
    public string? TradeStatus { get; set; }

    [ContractProperty(Name = "total_amount")]
    public decimal? TotalAmount { get; set; }
}
```

### Zero-Code Endpoint

```csharp
using FastEndpoints;
using NexusContract.Providers.Alipay;

// HttpApi endpoint - NO business logic, just protocol conversion
public sealed class TradeQueryEndpoint(AlipayProvider provider) 
    : AlipayEndpointBase<TradeQueryRequest>(provider) 
{
    // ZERO lines of code needed!
    // Route: POST /trade/query (auto-inferred)
    // Provider call: Automatic
    // Response: Automatic serialization
}
```

### Register Provider in DI

```csharp
// Program.cs
builder.Services.AddSingleton<AlipayProvider>(sp =>
    new AlipayProvider(
        appId: builder.Configuration["Alipay:AppId"]!,
        merchantPrivateKey: builder.Configuration["Alipay:MerchantPrivateKey"]!,
        alipayPublicKey: builder.Configuration["Alipay:AlipayPublicKey"]!
    ));
```

## 🌐 Layer 2: BFF/Business Layer (Client)

### Install Package

```bash
dotnet add package NexusContract.Client
```

### Configure Client

```csharp
// In your BFF or business service
using NexusContract.Client;
using NexusContract.Abstractions.Policies;

var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://payment-api.yourcompany.com")
};

var client = new NexusGatewayClient(
    httpClient, 
    new SnakeCaseNamingPolicy());
```

### Make Request

```csharp
// ✅ URL auto-extracted from [ApiOperation] attribute
// ✅ Response type auto-inferred from IApiRequest<TResponse>
var response = await client.SendAsync(
    new TradeQueryRequest 
    { 
        TradeNo = "2025011234567890" 
    });

Console.WriteLine($"Status: {response.TradeStatus}");
Console.WriteLine($"Amount: {response.TotalAmount}");
```

## 🚀 Layer 3: Direct Integration (Provider Only)

Skip HttpApi layer, call provider directly:

```csharp
using NexusContract.Providers.Alipay;

var provider = new AlipayProvider(
    appId: "2021...",
    merchantPrivateKey: "MII...",
    alipayPublicKey: "MII..."
);

// ✅ Direct OpenAPI call
// ✅ Method auto-extracted from [NexusContract] attribute
var response = await provider.ExecuteAsync(
    new TradeQueryRequest { TradeNo = "202501..." }
);
```

## 📝 Complete Alipay Integration Example

See the complete working example in:
- **Contract Definitions**: `examples/Demo.Alipay.Contract/Transactions/`
- **HttpApi Endpoints**: `examples/Demo.Alipay.HttpApi/Endpoints/`
- **Startup Configuration**: `examples/Demo.Alipay.HttpApi/Program.cs`

## ⚡ Performance Optimization

- **FrozenDictionary** for metadata lookups (zero allocation)
- **Precompiled IL** for property access (zero reflection at runtime)
- **Zero-copy** projections where possible
- **Startup warmup** for JIT optimization

**Benchmarks (.NET 10 Preview):**

| Operation | Mean | Allocated |
|-----------|------|-----------|
| Projection (Cold) | 52.3 ns | 0 B |
| Projection (Warm) | 31.7 ns | 0 B |
| Hydration | 48.1 ns | 0 B |
| Full Pipeline | ~120 ns | ~200 B |

## 🔍 Error Handling & Diagnostics

Every error has a unique `NXC` diagnostic code:

```csharp
try
{
    var response = await client.SendAsync(request);
}
catch (NexusCommunicationException ex)
{
    // ex.DiagnosticCode: "NXC-001", "NXC-HTTP-404", etc.
    // ex.DiagnosticData: Structured error details
    Console.WriteLine($"Error: {ex.DiagnosticCode} - {ex.Message}");
}
```

**Common Diagnostic Codes:**

| Code | Meaning |
|------|---------|
| `NXC-001` | Contract validation failed |
| `NXC-002` | Missing required attribute |
| `NXC-HTTP-xxx` | HTTP error (xxx = status code) |
| `NXC-SIGN-001` | Signature verification failed |

---

For detailed Chinese documentation, see [IMPLEMENTATION.zh-CN.md](./IMPLEMENTATION.zh-CN.md).

**Maintainer:** NexusContract  
**Project Homepage:** https://github.com/NexusContract/PubSoft.NexusContract

