# 支付宝提供商（Provider核心）

## 定位

本项目提供**支付宝Provider核心实现**，负责平台特定的业务逻辑。

- ✅ **包含**：`AlipayProvider.cs` + RSA签名 + HTTP通信 + 响应验证
- ✅ **框架无关**：可用于 FastEndpoints、Minimal API、gRPC 等任何场景
- ❌ **不包含**：TradePayRequest等具体契约（由业务层定义）
- ❌ **不包含**：Endpoint实现（由 Web 框架层实现）

## 完整示例见

- **Contract定义**：[examples/Demo.Alipay.Contract](../../examples/Demo.Alipay.Contract)
- **Endpoint实现**：[examples/Demo.Alipay.HttpApi](../../examples/Demo.Alipay.HttpApi)

## 核心类

### AlipayProvider

```csharp
public class AlipayProvider : IAsyncDisposable
{
    public async Task<TResponse> ExecuteAsync<TResponse>(
        IApiRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : class, new()
    {
        // 1. 定义HTTP执行器（RSA签名、网络调用、验证）
        // 2. 委托给NexusGateway执行四阶段管道
        // 3. 返回强类型响应
    }
}
```

**特点**：
- 不依赖具体的Contract定义
- 不依赖HTTP框架（FastEndpoints等）
- 纯业务层实现，可独立测试

## 使用流程

```
你的Contract类 (IApiRequest<TResponse>)
  ↓
你的Endpoint类 (继承框架特定基类)
  ↓
AlipayProvider.ExecuteAsync(request)
  ↓
NexusGateway四阶段管道
  ↓
HTTP执行器（本项目提供）
```

## 依赖注入

```csharp
services.AddAlipayProvider(config);
```

参考：[ServiceConfiguration/AlipayServiceExtensions.cs](./ServiceConfiguration/AlipayServiceExtensions.cs)

## 快速开始

1. 在自己的项目中定义Contract（继承 `IApiRequest<TResponse>`）
2. 定义Endpoint（继承框架特定基类，如 `AlipayEndpointBase<TRequest>`）
3. 注入AlipayProvider，调用ExecuteAsync

参考完整示例：[examples/Demo.Alipay.HttpApi](../../examples/Demo.Alipay.HttpApi)


