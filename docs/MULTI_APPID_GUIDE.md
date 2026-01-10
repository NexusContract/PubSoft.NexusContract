# Multi-AppId Configuration Guide

> **多 AppId 配置指南** - 支持一个 SysId 下管理多个 AppId

**场景：** ISV 服务商模式，一个系统商户（SysId）可以拥有多个支付应用（AppId）

---

## 📚 使用场景说明

### 场景 1：精确匹配（指定 AppId）

**BFF 传入参数：** `sysid + appid + providername`

```csharp
// BFF 请求头
X-SysId: 2088123456789012
X-AppId: 2021001234567890
X-Provider: Alipay

// HttpApi 处理
var context = new ConfigurationContext("Alipay", "2088123456789012")
{
    ProfileId = "2021001234567890"  // 精确匹配此 AppId
};

var config = await resolver.ResolveAsync(context);
// → 返回 AppId 2021001234567890 的配置
```

### 场景 2：默认 AppId（不传 AppId）

**BFF 传入参数：** `sysid + providername`（不传 appid）

```csharp
// BFF 请求头
X-SysId: 2088123456789012
X-Provider: Alipay
// X-AppId 缺失

// HttpApi 处理
var context = new ConfigurationContext("Alipay", "2088123456789012")
{
    ProfileId = null  // 不指定 AppId
};

var config = await resolver.ResolveAsync(context);
// → 自动查找该 SysId 下的默认 AppId
```

**默认 AppId 解析策略：**
1. 优先：查找标记为 `default` 的 AppId
2. 回退：返回第一个（first）AppId
3. 失败：抛出 `NexusTenantException.NotFound`

---

## 🔧 配置管理 API

### 1. 添加配置（支持设置默认 AppId）

```csharp
var manager = new TenantConfigurationManager(redis, securityProvider);

// 添加第一个 AppId（标记为默认）
await manager.SetConfigurationAsync(
    providerName: "Alipay",
    realmId: "2088123456789012",
    profileId: "2021001234567890",
    settings: new ProviderSettings
    {
        AppId = "2021001234567890",
        PrivateKey = "MIIEvQ...",
        PublicKey = "MIIBIj...",
        GatewayUrl = new Uri("https://openapi.alipay.com/gateway.do")
    },
    isDefault: true  // ✅ 标记为默认 AppId
);

// 添加第二个 AppId（非默认）
await manager.SetConfigurationAsync(
    providerName: "Alipay",
    realmId: "2088123456789012",
    profileId: "2021009876543210",
    settings: new ProviderSettings { /* ... */ },
    isDefault: false  // 非默认 AppId
);
```

### 2. 查询 AppId 列表

```csharp
// 获取某个 SysId 下的所有 AppId
var appIds = await manager.GetProfileIdsAsync(
    providerName: "Alipay",
    realmId: "2088123456789012"
);
// 返回: ["2021001234567890", "2021009876543210"]
```

### 3. 查询默认 AppId

```csharp
// 获取默认 AppId
var defaultAppId = await manager.GetDefaultProfileIdAsync(
    providerName: "Alipay",
    realmId: "2088123456789012"
);
// 返回: "2021001234567890"（如果设置了默认）
// 返回: null（如果没有设置默认）
```

### 4. 修改默认 AppId

```csharp
// 切换默认 AppId
await manager.SetDefaultProfileIdAsync(
    providerName: "Alipay",
    realmId: "2088123456789012",
    profileId: "2021009876543210"  // 新的默认 AppId
);

// ✅ 自动发布 Pub/Sub 刷新通知，其他实例的 L1 缓存自动失效
```

### 5. 删除配置

```csharp
// 删除 AppId
await manager.DeleteConfigurationAsync(
    providerName: "Alipay",
    realmId: "2088123456789012",
    profileId: "2021001234567890"
);

// ⚠️ 如果删除的是默认 AppId，自动清除 default 标记
// ⚠️ 下次查询时会回退到 first AppId
```

---

## 🗄️ Redis 数据结构

### 配置存储（单个 AppId）

```
Key: nexus:config:Alipay:2088123456789012:2021001234567890
Type: String
Value: {
  "AppId": "2021001234567890",
  "PrivateKey": "v1:base64_encrypted_key",
  "PublicKey": "MIIBIj...",
  "GatewayUrl": "https://openapi.alipay.com/gateway.do"
}
TTL: 永久
```

### AppId 组索引（用于默认 AppId 查询）

```
Key: nexus:config:group:Alipay:2088123456789012
Type: Hash
Fields:
  - "2021001234567890" → "2026-01-10T10:30:00Z" (创建/更新时间)
  - "2021009876543210" → "2026-01-10T11:00:00Z"
  - "default" → "2021001234567890" (默认 AppId 标记)
TTL: 永久
```

---

## 🚀 FastEndpoints 集成示例

### Endpoint 自动提取 AppId

```csharp
public abstract class NexusEndpointBase<TReq> : Endpoint<TReq, TReq.TResponse>
    where TReq : class, IApiRequest<TReq.TResponse>, new()
{
    public override async Task HandleAsync(TReq req, CancellationToken ct)
    {
        // 1. 提取租户上下文
        string sysId = HttpContext.Request.Headers["X-SysId"].ToString();
        string appId = HttpContext.Request.Headers["X-AppId"].ToString(); // 可选
        string provider = HttpContext.Request.Headers["X-Provider"].ToString();

        // 2. 构建配置上下文
        var context = new ConfigurationContext(provider, sysId)
        {
            ProfileId = string.IsNullOrWhiteSpace(appId) ? null : appId
            // ✅ 如果 appId 为空，Resolver 自动查找默认 AppId
        };

        // 3. 执行请求
        var response = await _engine.ExecuteAsync(req, context, ct);
        await SendAsync(response);
    }
}
```

---

## 📊 性能特征

| 场景 | L1 缓存命中 | L2 缓存命中 | 缓存未命中（含默认查询） |
|------|------------|------------|----------------------|
| **精确匹配** | 极快 | ~1ms | ~2ms (Redis 2次查询) |
| **默认 AppId** | 极快 | ~1ms | ~3ms (Redis 3次查询) |

**缓存策略：**
- L1（内存）：5 分钟 TTL
- L2（Redis）：永久保存
- 默认 AppId 解析结果也会缓存在 L1

---

## ⚠️ 注意事项

### 1. 默认 AppId 标记的原子性

使用 Redis Transaction 确保：
- 配置写入
- AppId 组索引更新
- 默认标记设置

三个操作原子执行。

### 2. 删除默认 AppId 的行为

```csharp
// 当前默认 AppId: 2021001234567890
// AppId 列表: ["2021001234567890", "2021009876543210"]

// 删除默认 AppId
await manager.DeleteConfigurationAsync("Alipay", "2088123456", "2021001234567890");

// ✅ 自动清除 default 标记
// ⚠️ 下次查询时会回退到 first (2021009876543210)
```

### 3. 空 AppId 列表的异常

```csharp
// 如果 SysId 下没有任何 AppId
var context = new ConfigurationContext("Alipay", "2088123456")
{
    ProfileId = null
};

await resolver.ResolveAsync(context);
// ❌ 抛出: NexusTenantException.NotFound
//    "No AppId found for Alipay:2088123456"
```

---

## 🧪 测试示例

```csharp
[Fact]
public async Task ResolveAsync_NullProfileId_ShouldUseDefaultAppId()
{
    // Arrange: 设置默认 AppId
    await _manager.SetConfigurationAsync(
        "Alipay", "2088123456", "2021001234", _settings1, isDefault: true);
    await _manager.SetConfigurationAsync(
        "Alipay", "2088123456", "2021009876", _settings2, isDefault: false);

    // Act: 不传 ProfileId
    var context = new ConfigurationContext("Alipay", "2088123456")
    {
        ProfileId = null
    };
    var config = await _resolver.ResolveAsync(context);

    // Assert: 应该返回默认 AppId 的配置
    Assert.Equal("2021001234", config.AppId);
}

[Fact]
public async Task ResolveAsync_NoDefaultMarker_ShouldUseFirstAppId()
{
    // Arrange: 不设置默认 AppId
    await _manager.SetConfigurationAsync(
        "Alipay", "2088123456", "2021001234", _settings1, isDefault: false);
    await _manager.SetConfigurationAsync(
        "Alipay", "2088123456", "2021009876", _settings2, isDefault: false);

    // Act
    var context = new ConfigurationContext("Alipay", "2088123456")
    {
        ProfileId = null
    };
    var config = await _resolver.ResolveAsync(context);

    // Assert: 应该返回第一个 AppId
    Assert.NotNull(config.AppId);
}
```

---

## 🎯 总结

**核心设计原则：**
1. ✅ 支持精确匹配（显式指定 AppId）
2. ✅ 支持默认匹配（不传 AppId，自动查找）
3. ✅ 原子性保证（Redis Transaction）
4. ✅ 缓存一致性（Pub/Sub 自动刷新）
5. ✅ 性能优化（L1/L2 双层缓存）

**使用建议：**
- 生产环境：每个 SysId 设置一个 `default` AppId
- 新增 AppId：使用 `isDefault: false`
- 切换主 AppId：使用 `SetDefaultProfileIdAsync()`
- 删除 AppId：注意检查是否为 default
