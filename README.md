# NexusContract (Elite Edition)

> **[中文文档 (Chinese)](./README.zh-CN.md)** | **English (Current)**

**Kernelized Contract Integration (KCI) Framework** - A high-performance, self-describing, metadata-driven payment integration engine built on .NET Standard 2.0 + .NET 10.

> **"Explicit boundaries over implicit magic."** — The supreme constitution of this framework. All designs revolve around **determinism**, **observability**, and **architectural constraints**.

---

## 🏛️ Core Architecture: From REPR to REPR-P

This framework extends [**FastEndpoints**](https://fast-endpoints.com/)' **REPR (Request-Endpoint-Response)** pattern through **Proxying** mechanism, achieving complete decoupling between business logic and physical protocols, forming the **REPR-P** pattern.

- **R**equest: Strongly-typed business contract (`IApiRequest<TResponse>`).
- **E**ndpoint: **Zero-code proxy**, responsible only for protocol conversion, containing no business logic.
- **R**esponse: Strongly-typed business response.
- **P**roxy: `NexusGateway` acts as the command center, orchestrating the four-phase pipeline and proxying Endpoint calls to specific third-party `Provider`s.

---

## 🚀 Core Features

- **Metadata-Driven**: One-time scan and "freeze" of all contract metadata at startup, zero reflection at runtime, achieving extreme performance and smooth P99 latency.
- **Startup Health Check**: Performs "lossless panoramic scan" of all contracts at application startup, generating structured diagnostic reports to detect architectural violations early.
- **Four-Phase Pipeline**: All requests go through the standardized flow of "Validate → Project → Execute → Hydrate", ensuring behavioral consistency.
- **Structured Diagnostics**: Every error (static, outbound, inbound) has a unique `NXC` diagnostic code for rapid localization and resolution.
- **Explicit Boundaries**: The framework enforces strict "constitutional" constraints (e.g., max nesting depth, encrypted field locking) to eliminate "magic" and uncertainty.

---

## 🏁 Quick Start: Startup Health Check

In `Demo.Alipay.HttpApi`, we demonstrate how to perform contract health checks at application startup:

```csharp
// examples/Demo.Alipay.HttpApi/Program.cs

// 1. Scan all types with [ApiOperation] attribute
var types = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => a.GetTypes())
    .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<ApiOperationAttribute>() != null)
    .ToArray();

// 2. Execute preload and panoramic scan
var report = NexusContractMetadataRegistry.Instance.Preload(types, warmup: true);

// 3. Print beautiful ASCII health report
report.PrintToConsole(includeDetails: true);

// 4. If critical errors exist, abort startup
if (report.HasCriticalErrors)
{
    Console.WriteLine("❌ Critical errors detected. Service cannot start.");
    Environment.Exit(1);
}

Console.WriteLine("✅ All contracts validated. Service is ready.");
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

---

## 📦 NuGet Packages

| Package | Version | Framework | Description |
|---------|---------|-----------|-------------|
| [NexusContract.Abstractions](https://www.nuget.org/packages/NexusContract.Abstractions) | ![NuGet](https://img.shields.io/nuget/v/NexusContract.Abstractions?style=flat-square) | netstandard2.0 | Core abstraction layer (zero dependencies) |
| [NexusContract.Core](https://www.nuget.org/packages/NexusContract.Core) | ![NuGet](https://img.shields.io/nuget/v/NexusContract.Core?style=flat-square) | .NET 10 | Gateway engine and four-phase pipeline |
| [NexusContract.Client](https://www.nuget.org/packages/NexusContract.Client) | ![NuGet](https://img.shields.io/nuget/v/NexusContract.Client?style=flat-square) | .NET 10 | Client SDK for BFF/business layer (HTTP communication) |
| [NexusContract.Providers.Alipay](https://www.nuget.org/packages/NexusContract.Providers.Alipay) | ![NuGet](https://img.shields.io/nuget/v/NexusContract.Providers.Alipay?style=flat-square) | .NET 10 | Alipay provider (OpenAPI v3) |

---

## 🏗️ Three-Layer Architecture

```
┌──────────────────────────────────────────────┐
│   BFF / Business Layer (Layer 2)             │
│   └─ Uses: NexusGatewayClient (HTTP calls)    │
└──────────────────────────────────────────────┘
         ↓ HTTP (Client Package)
┌──────────────────────────────────────────────┐
│   HttpApi Layer (Layer 1)                    │
│   └─ FastEndpoints + Provider               │
└──────────────────────────────────────────────┘
         ↓ Direct Call (Provider Package)
┌──────────────────────────────────────────────┐
│   Provider Layer (Layer 0)                   │
│   └─ AlipayProvider (OpenAPI v3)            │
└──────────────────────────────────────────────┘
         ↓ calls
┌──────────────────────────────────────────────┐
│   Alipay OpenAPI                             │
└──────────────────────────────────────────────┘

OR (Direct Integration - Skip HttpApi)

┌──────────────────────────────────────────────┐
│   Your Application                           │
│   └─ AlipayProvider (Direct)                │
└──────────────────────────────────────────────┘
         ↓ calls
┌──────────────────────────────────────────────┐
│   Alipay OpenAPI                             │
└──────────────────────────────────────────────┘
```

**Architecture Selection Guide:**

| Scenario | Recommended Solution | Components |
|----------|---------------------|------------|
| Microservices architecture, unified payment gateway API | Layer 1 + Layer 2 | HttpApi (FastEndpoints) + Client (BFF) |
| Monolithic application, direct payment integration | Layer 3 (Direct) | Provider only |
| Multi-tenant SaaS, centralized payment service | Layer 1 + Layer 2 | HttpApi + Client |

---

## 📖 Usage Examples

### Layer 1: HttpApi (FastEndpoints + Provider)

```csharp
// 🎯 Inside HttpApi: Zero-code endpoint, direct Provider call
public sealed class TradeQueryEndpoint(AlipayProvider provider) 
    : AlipayEndpointBase<TradeQueryRequest>(provider) { }
// ✅ Route auto-inferred as POST /trade/query
// ✅ Direct Provider call, no HTTP overhead
```

### Layer 2: BFF/Business Layer (Client via HTTP)

```csharp
// 🎯 BFF or business service: HTTP call to HttpApi endpoint
using NexusContract.Client;

var httpClient = new HttpClient 
{ 
    BaseAddress = new Uri("https://payment-api.example.com") 
};
var client = new NexusGatewayClient(httpClient, new SnakeCaseNamingPolicy());

// ✅ Sends HTTP request to HttpApi's /trade/query endpoint
// ✅ URL auto-extracted from [ApiOperation]
var response = await client.SendAsync(
    new TradeQueryRequest { TradeNo = "202501..." }
);
```

### Layer 3: Direct Integration (Provider Only)

```csharp
// 🎯 Direct integration: Skip HttpApi, call Alipay OpenAPI directly
using NexusContract.Providers.Alipay;

var provider = new AlipayProvider(appId, privateKey, publicKey);

// ✅ Direct Alipay OpenAPI call, no HTTP intermediary
// ✅ Method auto-extracted from [NexusContract]
var response = await provider.ExecuteAsync(
    new TradeQueryRequest { TradeNo = "202501..." }
);
```

---

## 🎯 Performance Benchmarks

Running on **.NET 10 (Preview)**, results from `BenchmarkDotNet`:

| Method | Mean | Allocated |
|--------|------|-----------|
| Projection (Cold) | **52.3 ns** | **0 B** |
| Projection (Warm) | **31.7 ns** | **0 B** |
| Hydration | **48.1 ns** | **0 B** |
| Full Pipeline | **~120 ns** | **~200 B** |

**Key Optimizations:**
- ✅ **FrozenDictionary** for metadata (zero allocation lookups)
- ✅ **Precompiled IL** for property getters/setters
- ✅ **Zero-reflection** at runtime
- ✅ **Frozen collections** for endpoint routing

---

## 📚 Documentation

- **[IMPLEMENTATION.md](./docs/IMPLEMENTATION.md)** - Detailed implementation guide
- **[CONSTITUTION.md](./src/NexusContract.Abstractions/CONSTITUTION.md)** - Architectural constitution
- **[PACKAGES.md](./PACKAGES.md)** - NuGet package overview
- **[NUGET_PUBLISHING.md](./docs/NUGET_PUBLISHING.md)** - Publishing guide
- **[CLIENT_SDK_GUIDE.md](./src/NexusContract.Client/CLIENT_SDK_GUIDE.md)** - Client SDK documentation

---

## 🤝 Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](./CONTRIBUTING.md) (planned).

---

## 📄 License

MIT License - See [LICENSE](./LICENSE) for details.

---

**Maintainer:** NexusContract  
**Project Homepage:** https://github.com/NexusContract/PubSoft.NexusContract  
**NuGet Profile:** https://www.nuget.org/profiles/NexusContract
