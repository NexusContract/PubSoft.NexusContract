# NexusContract Architecture Blueprint v1.1

> **Version:** 1.1 (ISV Multi-Tenant Execution Release)
> **Status:** ✅ Approved
> **Date:** January 10, 2026
> **Scenario:** High-concurrency ISV gateway for Alipay/WeChat Pay (hundreds of merchants dynamic access)
> **Technical Constraints:** Core contracts compatible with .NET Standard 2.0 (WinForm/Legacy support)

## 1. Architectural Overview

This architecture follows the **"Ingress -> Dispatcher -> JIT Resolver -> Executor"** pipeline model.

### Core Component Responsibilities

| Component | Layer | Metaphor Role | Responsibility | Key Features |
|-----------|-------|---------------|----------------|--------------|
| **FastEndpoints** | **Ingress** | **Receiver** | Dumb terminal. Handles metadata routing, exception normalization, tenant context extraction. | 🟢 **Metadata Zero-Code** |
| **NexusEngine** | **Core** | **Dispatcher** | Brain. Routes to corresponding Provider based on Request type. | 🟡 **Stateless Dispatch** |
| **ConfigResolver** | **Strategy** | **Butler** | **New Introduction**. Maps business identity (Realm/Profile) to physical configuration. | 🔵 **JIT Dynamic Loading** |
| **Provider** | **Business** | **Expert** | Stateless singleton. Handles signing and protocol conversion only, no static configuration. | 🟡 **Environment Isolation** |
| **YarpTransport** | **Egress** | **Fleet** | High-performance HTTP/2 connection pool tunnel. | 🔴 **Multiplexing** |

---

## 2. Physical Architecture and Data Flow

```mermaid
graph TD
    %% 1. External Request
    User[Client / BFF] -->|HTTP + Business Params| FE[FastEndpoints Ingress]

    %% 2. Gateway Internal Host
    subgraph GatewayHost [Nexus Gateway Host]
        direction TB

        %% A. Reception and Context Building (Zero-Code Base)
        FE -->|1. Strongly-typed Req + Context| Core[NexusEngine]

        %% B. Dispatch
        Core -->|2. Dispatch (Stateless)| Provider[Alipay / WeChat Provider]

        %% C. Configuration Resolution (JIT Core)
        subgraph ConfigLayer [Configuration Strategy Layer]
            direction TB
            style ConfigLayer fill:#e3f2fd,stroke:#1565c0,stroke-dasharray: 5 5

            Resolver[Configuration Resolver]
            Cache[(L1 Memory + L2 Redis)]

            Resolver <-->|3. Get Keys (JIT)| Cache
        end

        %% D. Execution and Transport
        subgraph ExecutionLayer [Execution Layer]
            direction TB
            style ExecutionLayer fill:none,stroke:none

            Url[Url Decision]
            Yarp[YarpTransport]
        end

        Provider -->|3a. Request Config (with ProviderName)| Resolver
        Provider -.->|4. Calculate Route (no keys)| UrlStrategy
        Provider -->|5. Sign and Send| Yarp
    end

    %% 3. Upstream
    Yarp -->|HTTP/2| Upstream[Alipay / WeChat Pay API]

    %% Style Definitions
    style FE fill:#c8e6c9,stroke:#2e7d32,stroke-width:2px
    style Core fill:#fff9c4,stroke:#fbc02d,stroke-width:2px
    style Provider fill:#fff3e0,stroke:#e65100,stroke-width:2px
    style Resolver fill:#bbdefb,stroke:#0d47a1,stroke-width:2px

```

---

## 3. Core Contracts (`NexusContract.Abstractions`)

**Technical Constraints:** Target framework **.NET Standard 2.0**. Strictly prohibit `record`, `required`, `init`.

### A. Configuration Context

```csharp
namespace NexusContract.Abstractions;

public class ConfigurationContext
{
    // Mandatory constructor validation
    public ConfigurationContext(string providerName, string realmId)
    {
        if (string.IsNullOrEmpty(providerName)) throw new ArgumentNullException(nameof(providerName));
        if (string.IsNullOrEmpty(realmId)) throw new ArgumentNullException(nameof(realmId));

        ProviderName = providerName;
        RealmId = realmId;
    }

    /// <summary>Channel identifier (e.g. "Alipay")</summary>
    public string ProviderName { get; private set; }

    /// <summary>Domain/Ownership (corresponds to SysId / SpMchId)</summary>
    public string RealmId { get; private set; }

    /// <summary>Profile/Execution Unit (corresponds to AppId / SubMchId)</summary>
    public string ProfileId { get; set; }

    public Dictionary<string, object> Metadata { get; set; }
}

```

### B. Routing Context - **Security Isolation**

```csharp
public class RoutingContext
{
    public RoutingContext(Uri baseUrl)
    {
        if (baseUrl == null) throw new ArgumentNullException(nameof(baseUrl));
        BaseUrl = baseUrl;
    }

    public Uri BaseUrl { get; private set; }
    public string Version { get; set; }
}

public interface IUpstreamUrlBuilder
{
    // ✅ Fixed: Only receives pure Context, not Settings containing private keys
    Uri Build(string operationId, RoutingContext context);
}

```

---

## 4. Key Implementation Strategies

### A. Ingress Layer: Zero-Code and Metadata-Driven

Using **Template Method Pattern**. Base class handles routing, tenant extraction, engine dispatch, and **NxcErrorEnvelope** encapsulation.

```csharp
// Core Base Class: NexusEndpointBase
public abstract class NexusEndpointBase<TReq, TResp> : Endpoint<TReq, TResp>
    where TReq : class, IApiRequest<TResp>, new()
    where TResp : class, new()
{
    private readonly INexusEngine _engine; // Replace specific Provider with universal dispatch
    private readonly ILogger _logger;

    protected NexusEndpointBase(INexusEngine engine, ILogger logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public override void Configure()
    {
        // 1. [Zero-Code] Auto-generate routes based on [ApiOperation] metadata
        var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(typeof(TReq));

        if (metadata?.Operation == null)
            throw new InvalidOperationException($"Missing [ApiOperation] on {typeof(TReq).Name}");

        // e.g., "nexus.trade.create" -> "/api/trade/create"
        string route = RouteStrategy.Convert(metadata.Operation.OperationId);

        Post(route);
        AllowAnonymous();
    }

    public override async Task HandleAsync(TReq req, CancellationToken ct)
    {
        try
        {
            // 2. [ISV Feature] Auto-extract tenant context (SysId/AppId)
            var tenantCtx = TenantContextFactory.Create(req, HttpContext);

            // 3. [Dispatcher] Delegate to engine dispatch
            var response = await _engine.ExecuteAsync(req, tenantCtx, ct);

            await SendAsync(response);
        }
        // 4. [Error Normalization] Unified error contract (NxcErrorEnvelope)
        catch (ContractIncompleteException ex)
        {
            await SendEnvelopeAsync(400, "NXC200", ex.Message, ex.GetDiagnosticData(), ct);
        }
        catch (NexusTenantException ex) // Tenant resolution failure
        {
            await SendEnvelopeAsync(403, "TENANT_INVALID", ex.Message, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway Error");
            await SendEnvelopeAsync(500, "NXC999", "Internal Server Error", null, ct);
        }
    }
}

```

### B. Infrastructure: ISV Hybrid Resolver

Maps "business dialect" to "framework standard".

```csharp
public class HybridConfigResolver : IConfigurationResolver
{
    private readonly ITenantRepository _repo;

    public async Task<ProviderSettings> ResolveAsync(ConfigurationContext ctx, CancellationToken ct)
    {
        // 1. Terminology mapping: RealmId -> SysId, ProfileId -> InnerAppId
        // 2. L1/L2 cache lookup
        var config = await _repo.GetAsync(ctx.ProviderName, ctx.RealmId, ctx.ProfileId);

        if (config == null) throw new NexusTenantException("Invalid merchant configuration");
        return config;
    }
}

```

### C. Business Layer: Stateless Provider

Provider hardcodes `ProviderName` and dynamically loads configuration at runtime.

```csharp
public class AlipayProvider(IConfigurationResolver _resolver, ...) : IProvider
{
    private const string NAME = "Alipay";

    public async Task<TResponse> ExecuteAsync(IApiRequest request, NexusContext ctx)
    {
        // 1. Construct context
        var configCtx = new ConfigurationContext(NAME, ctx.Metadata["SysId"])
        {
            ProfileId = ctx.Metadata["AppId"]
        };

        // 2. JIT load configuration
        var settings = await _resolver.ResolveAsync(configCtx, CancellationToken.None);

        // 3. Sign (private key used only here)
        var targetUri = _urlBuilder.Build(request.GetOperationId(), new RoutingContext(settings.GatewayUrl));
        var httpRequest = _signer.SignRequest(request, targetUri, settings);

        return await _transport.SendAsync(httpRequest, ctx);
    }
}

```

---

## 5. Composition Root (Program.cs)

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Core and Ingress
builder.Services.AddFastEndpoints();
builder.Services.AddNexusContract();

// 2. ISV Resolver and Repository
builder.Services.AddSingleton<IConfigurationResolver, HybridConfigResolver>();
builder.Services.AddSingleton<ITenantRepository, RedisTenantRepository>();

// 3. Register Providers
builder.Services.AddSingleton<IProvider, AlipayProvider>();

// 4. Production Egress (YARP)
if (builder.Environment.IsProduction())
{
    builder.Services.AddNexusYarpHosting();
}

var app = builder.Build();
app.UseFastEndpoints();
app.Run();

```

---

## 6. Key Architectural Decision Records (ADR Summary)

### Base Architecture (Inherited from v1.0)

* **ADR-001: Ingress/Egress Separation**
* **FastEndpoints** handles ingress (API definition), **YARP** handles egress (HTTP/2 transport).

* **ADR-002: Client Purity**
* `NexusContract.Client` SDK must remain zero-dependency, no references to FastEndpoints or YARP.

* **ADR-003: Deterministic Signing**
* URL resolution must complete inside Provider, before signing.

### ISV Enhanced Architecture (v1.1 Additions)

* **ADR-004: JIT Configuration**
* **Change:** Deprecate static `IOptions` singleton injection.
* **Decision:** Adopt `IConfigurationResolver` with L1/L2 caching.
* **Reason:** Support hundreds of merchants dynamic access, configuration updates without service restart.

* **ADR-005: Realm & Profile**
* **Decision:** Framework layer abstracts to `RealmId` (domain) and `ProfileId` (profile).
* **Reason:** Compatible with both Alipay (AppId system) and WeChat Pay (service provider system), eliminate business terminology pollution like `SysId`.

* **ADR-006: Context Isolation**
* **Decision:** `ProviderSettings` (containing private keys) strictly prohibited from URL Builder.
* **Reason:** Principle of least privilege, prevent key leakage from URL strategy layer.

* **ADR-007: Compatibility Degradation**
* **Decision:** `NexusContract.Abstractions` must be compatible with **.NET Standard 2.0**.
* **Reason:** Support enterprise internal WinForm and legacy .NET Framework system access. Prohibit `record`, `required`.