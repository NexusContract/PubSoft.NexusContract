# 支付宝提供商（Provider核心）

## 定位

本项目仅提供**支付宝Provider核心实现**，不包含具体的Contract和Endpoint示例。

- ✅ **包含**：`AlipayProvider.cs` + `AlipayProxyEndpoint.cs` + 配置
- ✅ **包含**：RSA签名、HTTP通信、响应验证
- ❌ **不包含**：TradePayRequest等具体契约
- ❌ **不包含**：TradePayEndpoint等具体端点实现

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

### AlipayProxyEndpoint

```csharp
public abstract class AlipayProxyEndpoint<TRequest> : NexusProxyEndpoint<TRequest>
    where TRequest : class, IApiRequest
{
    // 支付宝端点基类
    // 自动反射提取TResponse类型
}
```

## 使用流程

```
你的Contract类
  ↓
你的Endpoint类（继承AlipayProxyEndpoint<TRequest>）
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
2. 定义Endpoint（继承 `AlipayProxyEndpoint<TContract>`）
3. 注入AlipayProvider，调用ExecuteAsync

参考完整示例：[examples/Demo.Alipay.HttpApi](../../examples/Demo.Alipay.HttpApi)

