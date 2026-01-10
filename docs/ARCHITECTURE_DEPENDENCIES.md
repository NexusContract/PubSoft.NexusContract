# NexusContract v1.1 架构依赖关系

**设计原则：依赖倒置原则 (DIP) + 接口隔离原则 (ISP)**

---

## 📊 依赖关系图

```
┌─────────────────────────────────────────────────────────────────┐
│                    Abstractions (netstandard2.0)                │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ INexusTransport, ITenantIdentity, IProviderConfiguration,   │ │
│  │ IConfigurationResolver, IProvider, INexusEngine, etc.      │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              ▲    ▲    ▲
                              │    │    │
            ┌─────────────────┘    │    └─────────────────┐
            │                      │                      │
            │                      │                      │
┌───────────┴────────────┐ ┌──────┴──────────┐ ┌─────────┴─────────────┐
│    Core (net10.0)      │ │ Providers       │ │  Hosting (net10.0)    │
│  ┌──────────────────┐  │ │   (net10.0)     │ │  ┌─────────────────┐  │
│  │ NexusEngine      │  │ │ ┌─────────────┐ │ │  │ TenantContext   │  │
│  │ NexusGateway     │  │ │ │AlipayProvider│ │  │ Factory         │  │
│  │ ProviderSettings │  │ │ │AlipayProxy   │ │  │ NexusEndpoint   │  │
│  │ InMemoryConfig   │  │ │ │Provider      │ │  │ HybridConfig    │  │
│  │ Resolver         │  │ │ └─────────────┘ │  │ Resolver        │  │
│  └──────────────────┘  │ └─────────────────┘ │  │ AesSecurity     │  │
└────────────────────────┘                     │  │ Provider        │  │
                                               │  └─────────────────┘  │
                                               └───────────────────────┘
                                                          ▲
                                                          │
                                               ┌──────────┴─────────────┐
                                               │ Hosting.Yarp (net10.0) │
                                               │  ┌──────────────────┐  │
                                               │  │ YarpTransport    │  │
                                               │  │ (INexusTransport)│  │
                                               │  │ YarpService      │  │
                                               │  │ Extensions       │  │
                                               │  └──────────────────┘  │
                                               └────────────────────────┘
```

---

## 🎯 核心原则

### 1. **接口在 Abstractions，实现在具体层**

| 接口 | 位置 | 实现 | 位置 |
|------|------|------|------|
| `INexusTransport` | Abstractions/Transport | `YarpTransport` | Hosting.Yarp |
| `IConfigurationResolver` | Abstractions/Configuration | `InMemoryConfigResolver` | Core |
|  |  | `HybridConfigResolver` | Hosting |
| `ISecurityProvider` | Abstractions/Security | `AesSecurityProvider` | Hosting |
| `ITenantIdentity` | Abstractions/Contracts | `ConfigurationContext` | Core |
| `IProviderConfiguration` | Abstractions/Configuration | `ProviderSettings` | Core |

### 2. **Provider 层只依赖 Abstractions + Core**

```xml
<!-- NexusContract.Providers.Alipay.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\NexusContract.Abstractions\NexusContract.Abstractions.csproj" />
  <ProjectReference Include="..\..\NexusContract.Core\NexusContract.Core.csproj" />
  <!-- ❌ 不引用 Hosting.Yarp -->
</ItemGroup>
```

**为什么？**
- ✅ **避免依赖膨胀**：Hosting.Yarp 引入 YARP、Polly、ASP.NET Core 等重度依赖
- ✅ **环境兼容性**：Provider 可在控制台、移动端、测试环境等轻量级场景使用
- ✅ **可测试性**：Mock `INexusTransport` 接口即可，无需启动真实 HTTP 服务器

### 3. **依赖注入在 Hosting 层完成绑定**

```csharp
// Program.cs (Hosting 层)
builder.Services.AddNexusYarpTransport(options =>
{
    options.RetryCount = 3;
    options.CircuitBreakerFailureThreshold = 5;
});

// YarpServiceExtensions 内部注册
services.AddHttpClient<INexusTransport, YarpTransport>(...);
```

---

## 📦 项目引用矩阵

| 项目 | 引用 Abstractions | 引用 Core | 引用 Hosting | 引用 Hosting.Yarp |
|------|-------------------|-----------|--------------|-------------------|
| **Abstractions** | - | ❌ | ❌ | ❌ |
| **Core** | ✅ | - | ❌ | ❌ |
| **Hosting** | ✅ | ✅ | - | ❌ |
| **Hosting.Yarp** | ✅ | ❌ | ❌ | - |
| **Providers.Alipay** | ✅ | ✅ | ❌ | ❌ |
| **Client** | ✅ | ❌ | ❌ | ❌ |
| **Demo.Alipay.HttpApi** | ✅ | ✅ | ✅ | ✅ |

---

## 🔍 为什么 IYarpTransport 不改名为 INexusTransport？

**当前命名：** `IYarpTransport`  
**建议命名：** `INexusTransport` 或 `IGatewayTransport`

### 保持 IYarpTransport 的理由：

1. **明确实现意图**：
   - YARP 是 Microsoft 官方的反向代理框架，具有独特的 HTTP/2 连接池和负载均衡特性
   - 接口名明确表达"这是基于 YARP 技术栈的传输层"
   - 如果将来引入 gRPC 传输层，可以定义 `IGrpcTransport` 接口

2. **避免过度抽象**：
   - `INexusTransport` 过于宽泛，无法明确表达传输机制
   - ISV 多租户场景需要 HTTP/2 连接池，YARP 是最优解
   - 接口命名应平衡"抽象性"和"表达性"

3. **扩展性**：
   ```csharp
   // 未来可能的传输层接口族
   public interface IYarpTransport { }   // HTTP/2 + Polly 弹性策略
   public interface IGrpcTransport { }   // gRPC 双向流
   public interface IMqttTransport { }   // MQTT 物联网场景
   ```

### 如果重命名，建议方案：

```csharp
// Abstractions/Transport/INexusTransport.cs
public interface INexusTransport
{
    Task<HttpResponseMessage> SendAsync(...);
}

// Hosting.Yarp/YarpTransport.cs
public class YarpTransport : INexusTransport { }

// Hosting.Grpc/GrpcTransport.cs (未来)
public class GrpcTransport : INexusTransport { }
```

**当前决策：保持 `IYarpTransport` 不变，除非引入第二种传输机制。**

---

## 🚀 最佳实践

### ✅ 正确姿势：通过接口注入

```csharp
// AlipayProvider.cs (Providers.Alipay)
using NexusContract.Abstractions.Transport;

public class AlipayProvider
{
    private readonly INexusTransport? _transport;

    // 推荐：注入接口（生产级）
    public AlipayProvider(
        AlipayProviderConfig config,
        NexusGateway gateway,
        INexusTransport transport)
    {
        _transport = transport;
    }

    // 向后兼容：HttpClient（测试/轻量级场景）
    public AlipayProvider(
        AlipayProviderConfig config,
        NexusGateway gateway,
        HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
}
```

### ❌ 错误姿势：直接引用实现

```csharp
// ❌ 不要这样做
using NexusContract.Hosting.Yarp;  // 导入实现层

public class AlipayProvider
{
    private readonly YarpTransport _transport;  // 依赖具体类
}
```

**问题：**
- Provider 被迫引用 Hosting.Yarp 项目及其所有传递依赖
- 无法在轻量级环境（控制台、移动端）使用
- 单元测试必须启动 YARP 服务器

---

## 📖 相关文档

- [Abstractions Layer CONSTITUTION](../src/NexusContract.Abstractions/CONSTITUTION.md) — 抽象层设计原则
- [Hosting Layer README](../src/NexusContract.Hosting/README.md) — Hosting 层职责
- [YARP Transport README](../src/NexusContract.Hosting.Yarp/README.md) — YARP 传输层使用指南
- [IMPLEMENTATION.md](IMPLEMENTATION.md) — 完整实现细节

---

## 🎓 设计模式应用

| 模式 | 应用 | 效果 |
|------|------|------|
| **依赖倒置原则 (DIP)** | Provider 依赖 `INexusTransport` 接口，不依赖 `YarpTransport` 实现 | 高层模块不依赖低层模块 |
| **接口隔离原则 (ISP)** | `INexusTransport` 只定义传输必要方法，不暴露 YARP 内部细节 | 接口最小化 |
| **策略模式** | 通过 DI 切换不同传输实现（YARP / HttpClient / Mock） | 运行时替换算法 |
| **适配器模式** | `YarpTransport` 适配 YARP 库到 `INexusTransport` 接口 | 封装第三方库 |

---

## 🔒 License

MIT License. See [LICENSE](../LICENSE) for details.
