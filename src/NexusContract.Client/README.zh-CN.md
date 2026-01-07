# NexusContract.Client

**Elite Integration Channel** - Client SDK for BFF (Backend for Frontend) and business layers to consume NexusContract-powered HttpApi via HTTP.

> ℹ️ **Architecture Note**: This package is designed for **consuming remote HttpApi endpoints**, not for direct provider integration. If you need direct provider access within your HttpApi, use `NexusContract.Providers.*` instead.

## 📦 Installation

```bash
dotnet add package NexusContract.Client
```

## 🚀 Quick Start

### ASP.NET Core Integration with DI

```csharp
using NexusContract.Client;

var builder = WebApplication.CreateBuilder(args);

// Register HttpClient with base address
builder.Services.AddHttpClient<NexusGatewayClient>(client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register naming policy
builder.Services.AddSingleton<INamingPolicy>(new SnakeCaseNamingPolicy());

var app = builder.Build();

// Use in controllers/endpoints - URL and response type auto-inferred
app.MapPost("/process-payment", async (
    NexusGatewayClient client,
    PaymentRequest request) =>
{
    // ✅ SendAsync 自动从 [ApiOperation] 提取 URL，自动推断返回类型
    var result = await client.SendAsync(request);
    return Results.Ok(result);
});

app.Run();
```

### Standalone Client Usage

```csharp
using NexusContract.Client;
using NexusContract.Abstractions.Policies;

// Create HTTP client with base address
var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.example.com")
};

// Create gateway client
var client = new NexusGatewayClient(
    httpClient, 
    new SnakeCaseNamingPolicy());

// Execute request - URL from [ApiOperation], response type auto-inferred
var response = await client.SendAsync(
    new TradeQueryRequest { TradeNo = "202501..." });

Console.WriteLine($"Trade Status: {response.TradeStatus}");
```

## ✨ Key Features

- **BFF/Business Layer Focused**: Designed for consuming remote HttpApi endpoints via HTTP
- **Self-Describing**: URL automatically extracted from `[ApiOperation]` attribute in contracts
- **Type Inference**: Response type inferred from `IApiRequest<TResponse>` interface
- **DI Integration**: Works seamlessly with ASP.NET Core dependency injection
- **Zero Manual Routing**: No need to specify URLs or HTTP methods manually
- **Type-Safe Execution**: Compile-time safety with generic `SendAsync<TResponse>` API
- **Minimal Dependencies**: Only depends on `NexusContract.Abstractions` (for contracts)

## 🎯 Use Cases

✅ **When to use Client:**
- Building a BFF (Backend for Frontend) that aggregates multiple payment services
- Business layer services that need to call HttpApi endpoints over HTTP
- Microservices communicating with centralized payment gateway API

❌ **When NOT to use Client:**
- Within the HttpApi itself (use Provider directly with FastEndpoints)
- For direct provider integration without HttpApi layer (use Provider package)
- Server-to-server communication in the same process (use Provider directly)

## 🏗️ Architecture

```
┌─────────────────────────────────────┐
│   BFF / Business Layer                  │
│   (Your ASP.NET Core API)              │
└─────────────────────────────────────┘
         ↓ uses (HTTP)
┌─────────────────────────────────────┐
│   NexusGatewayClient (SDK)            │  ← THIS PACKAGE
├─────────────────────────────────────┤
│  - HTTP Communication Layer           │
│  - Request/Response Serialization     │
│  - Automatic URL Extraction           │
│  - Type-Safe API (SendAsync)          │
└─────────────────────────────────────┘
         ↓ HTTP calls to
┌─────────────────────────────────────┐
│   HttpApi (e.g. Demo.Alipay.HttpApi)  │
│   (FastEndpoints + Provider)          │
└─────────────────────────────────────┘
         ↓ calls
┌─────────────────────────────────────┐
│   Provider (AlipayProvider)           │
│   (Direct OpenAPI Integration)        │
└─────────────────────────────────────┘
```

## 📚 Related Packages

- **[NexusContract.Abstractions](https://www.nuget.org/packages/NexusContract.Abstractions)** - Core contracts and attributes
- **[NexusContract.Core](https://www.nuget.org/packages/NexusContract.Core)** - Gateway engine and pipeline
- **[NexusContract.Providers.Alipay](https://www.nuget.org/packages/NexusContract.Providers.Alipay)** - Alipay provider implementation

## 📖 Documentation

- [GitHub Repository](https://github.com/NexusContract/PubSoft.NexusContract)
- [Implementation Guide](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/docs/IMPLEMENTATION.md)
- [Client SDK Guide](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/src/NexusContract.Client/CLIENT_SDK_GUIDE.md)

## 🎯 Target Framework

- **Requires**: .NET 10 (Preview)
- **Compatibility**: Works with any .NET 10+ application

## 📄 License

MIT License - See [LICENSE](https://github.com/NexusContract/PubSoft.NexusContract/blob/main/LICENSE)
