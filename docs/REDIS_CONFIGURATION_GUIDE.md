# Redis 作为租户配置主数据源 — 架构指南

> **架构决策：** ADR-008: Redis-First Tenant Storage  
> **版本：** v1.0.0-preview.10  
> **日期：** 2026-01-10

## 📐 架构决策背景

### 为什么选择 Redis 而非关系型数据库？

#### ISV 租户配置数据特征

| 维度 | 特征 | Redis 适配度 |
|-----|------|------------|
| **数据量** | 中等（100-10000 租户） | ✅ 内存完全容纳 |
| **读写比** | 极高（99% 读，1% 写） | ✅ 读性能极佳 |
| **数据结构** | KV 结构（TenantId → Config） | ✅ 天然契合 |
| **查询模式** | 精确匹配（无复杂 JOIN） | ✅ O(1) 查询 |
| **变更频率** | 低频（新增租户、密钥轮换） | ✅ 写性能足够 |
| **持久化需求** | 高（但允许秒级延迟） | ✅ RDB + AOF |
| **审计需求** | 中等（主要记录变更历史） | 🟡 需外部服务 |

**结论：Redis 在 ISV 场景下完全满足需求，且性能更优** ✅

---

## 🏗️ 架构层级设计

### 方案对比

#### ❌ 传统方案（三层缓存）

```
L1: Memory（5min TTL）→ 性能：<1μs
L2: Redis（30min TTL）→ 性能：~1ms  
L3: MySQL/PostgreSQL → 性能：~10ms
```

**劣势：**
- 架构复杂（3 层缓存一致性）
- 性能瓶颈（数据库延迟）
- 开发成本高（ORM、迁移脚本）
- 缓存穿透风险

#### ✅ Redis-First 方案（推荐）

```
L1: Memory（5min TTL）     → 性能：<1μs（热数据）
L2: Redis（永久存储）       → 性能：~1ms（主数据源）
L3: MySQL/PostgreSQL（可选）→ 冷备份 + 审计日志（异步写入）
```

**优势：**
- 架构简化（L1/L2 双层，L3 可选）
- 性能最优（~1ms 响应，100x 数据库）
- 开发效率高（无需 ORM）
- 运维简单（Redis 集群成熟）

---

## ⚙️ Redis 持久化配置

### 1. RDB + AOF 混合持久化（生产推荐）

#### redis.conf 配置

```conf
# ====================
# RDB 持久化配置
# ====================
# 每小时自动保存 RDB 快照（防止数据丢失）
save 3600 1

# RDB 文件压缩（节省磁盘空间）
rdbcompression yes

# RDB 文件校验（防止文件损坏）
rdbchecksum yes

# RDB 文件路径
dir /data/redis
dbfilename nexus-tenant-config.rdb

# ====================
# AOF 持久化配置
# ====================
# 启用 AOF（追加式持久化）
appendonly yes

# AOF 文件名
appendfilename "nexus-tenant-config.aof"

# AOF 同步策略（每秒同步，平衡性能与安全）
appendfsync everysec

# AOF 重写配置（文件增长 100% 时自动重写）
auto-aof-rewrite-percentage 100
auto-aof-rewrite-min-size 64mb

# 启用 AOF + RDB 混合持久化（Redis 4.0+）
aof-use-rdb-preamble yes

# ====================
# 内存管理
# ====================
# 最大内存（根据租户数量调整，建议 2-4GB）
maxmemory 4gb

# 内存淘汰策略（禁止淘汰，避免配置丢失）
maxmemory-policy noeviction
```

#### 数据安全保证

| 配置 | 数据丢失风险 | 性能影响 |
|-----|------------|---------|
| **RDB only** | 最多丢失 1 小时数据 | 无影响 |
| **AOF (everysec)** | 最多丢失 1 秒数据 | 轻微影响 (~5%) |
| **AOF (always)** | 不丢失数据 | 严重影响 (~50%) |
| **RDB + AOF 混合** | 最多丢失 1 秒数据 | 轻微影响 (~5%) ⭐ |

**推荐配置：** RDB + AOF（everysec）混合持久化

---

## 🔧 代码集成示例

### 1. Program.cs 配置

```csharp
using Microsoft.Extensions.Caching.Memory;
using NexusContract.Hosting.Configuration;
using NexusContract.Hosting.Security;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. 配置 Redis 连接（支持集群）
var redisConnection = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
{
    EndPoints = { "redis-master:6379", "redis-replica-1:6379", "redis-replica-2:6379" },
    Password = builder.Configuration["Redis:Password"],
    Ssl = true, // 生产环境启用 TLS
    AbortOnConnectFail = false,
    ConnectTimeout = 5000,
    SyncTimeout = 5000,
    DefaultDatabase = 0
});

builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

// 2. 配置内存缓存（L1）
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024; // 最多缓存 1024 个租户配置
    options.CompactionPercentage = 0.25; // 内存压力时清理 25%
});

// 3. 配置安全提供者（加密 PrivateKey）
var aesKey = builder.Configuration["Security:AesKey"];
var securityProvider = new AesSecurityProvider(aesKey);
builder.Services.AddSingleton<ISecurityProvider>(securityProvider);

// 4. 注册 HybridConfigResolver（Redis-First）
builder.Services.AddSingleton<HybridConfigResolver>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    var memoryCache = sp.GetRequiredService<IMemoryCache>();
    var security = sp.GetRequiredService<ISecurityProvider>();

    return new HybridConfigResolver(
        redis,
        memoryCache,
        security,
        redisKeyPrefix: "nexus:config:", // Redis 键前缀
        l1Ttl: TimeSpan.FromMinutes(5),  // L1 缓存 5 分钟
        l2Ttl: null                       // L2 无 TTL（永久存储）
    );
});

// 5. 注册租户配置管理器（高层 API）
builder.Services.AddSingleton<TenantConfigurationManager>();

// 6. 启动时预热配置
var app = builder.Build();

var configManager = app.Services.GetRequiredService<TenantConfigurationManager>();
await configManager.WarmupAsync();

app.Run();
```

### 2. 创建租户配置

```csharp
using NexusContract.Hosting.Configuration;
using NexusContract.Core.Configuration;

// 方式 1: 通过 TenantConfigurationManager（推荐）
var configManager = serviceProvider.GetRequiredService<TenantConfigurationManager>();

await configManager.CreateAsync(
    providerName: "Alipay",
    realmId: "2088123456789012",      // 服务商 PID
    profileId: "2021001234567890",     // AppId
    configuration: new ProviderSettings
    {
        ProviderName = "Alipay",
        AppId = "2021001234567890",
        PrivateKey = "MIIEvQIBADANB...",  // 自动加密存储
        PublicKey = "MIIBIjANBgkqh...",
        GatewayUrl = new Uri("https://openapi.alipay.com/gateway.do"),
        ExtendedSettings = new Dictionary<string, object>
        {
            ["ImplementationName"] = "Alipay.Cert", // 路由到证书版本
            ["SignType"] = "RSA2",
            ["Format"] = "JSON"
        }
    }
);

// 方式 2: 直接使用 HybridConfigResolver（底层 API）
var resolver = serviceProvider.GetRequiredService<HybridConfigResolver>();
var identity = new ConfigurationContext("Alipay", "2088123456789012")
{
    ProfileId = "2021001234567890"
};

await resolver.SetConfigurationAsync(identity, configuration);
```

### 3. 查询租户配置

```csharp
// 查询配置（自动走 L1 → Redis 缓存链）
var config = await configManager.GetAsync(
    providerName: "Alipay",
    realmId: "2088123456789012",
    profileId: "2021001234567890"
);

Console.WriteLine($"AppId: {config.AppId}");
Console.WriteLine($"Gateway: {config.GatewayUrl}");
```

### 4. 更新租户配置

```csharp
// 更新配置（自动刷新 L1 + 发布 Pub/Sub 通知）
config.PrivateKey = "NEW_PRIVATE_KEY_AFTER_ROTATION";

await configManager.UpdateAsync(
    providerName: "Alipay",
    realmId: "2088123456789012",
    profileId: "2021001234567890",
    configuration: config
);
```

### 5. 批量导入配置

```csharp
var configItems = new List<TenantConfigurationItem>
{
    new TenantConfigurationItem
    {
        ProviderName = "Alipay",
        RealmId = "2088111111111111",
        ProfileId = "2021001111111111",
        Configuration = new ProviderSettings { /* ... */ }
    },
    new TenantConfigurationItem
    {
        ProviderName = "WeChat",
        RealmId = "1234567890",
        ProfileId = "wxabcdef123456",
        Configuration = new ProviderSettings { /* ... */ }
    }
};

int successCount = await configManager.BatchCreateAsync(configItems);
Console.WriteLine($"成功导入 {successCount}/{configItems.Count} 个配置");
```

---

## 🛡️ 数据安全与审计

### 1. PrivateKey 加密存储

配置写入 Redis 时，`PrivateKey` 自动通过 `AesSecurityProvider` 加密：

```json
{
  "ProviderName": "Alipay",
  "AppId": "2021001234567890",
  "PrivateKey": "v1:aGVsbG8gd29ybGQ=...",  // AES256-CBC 加密
  "PublicKey": "MIIBIjANBgkqh...",
  "GatewayUrl": "https://openapi.alipay.com/gateway.do"
}
```

#### 加密特性

- **算法：** AES256-CBC
- **密钥长度：** 256 bit（Base64 编码后 44 字符）
- **版本前缀：** `v1:` 支持未来算法升级
- **随机 IV：** 每次加密使用新 IV（防止字典攻击）

### 2. Redis TLS 加密传输

生产环境必须启用 TLS 加密传输：

```csharp
var redisConnection = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
{
    EndPoints = { "redis.example.com:6380" },
    Ssl = true,  // 启用 TLS
    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
    CertificateValidation = (sender, cert, chain, errors) =>
    {
        // 生产环境严格验证证书
        return errors == System.Net.Security.SslPolicyErrors.None;
    }
});
```

### 3. 审计日志（可选）

通过外部服务异步写入审计日志（不影响主链路性能）：

```csharp
public class AuditService : IHostedService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDbConnection _auditDb;

    public async Task StartAsync(CancellationToken ct)
    {
        // 订阅 Redis Pub/Sub 配置变更通知
        var subscriber = _redis.GetSubscriber();
        await subscriber.SubscribeAsync("nexus:config:refresh", async (channel, message) =>
        {
            // 解析变更事件
            var changeEvent = JsonSerializer.Deserialize<ConfigChangeEvent>(message);

            // 异步写入审计日志
            await _auditDb.ExecuteAsync(
                "INSERT INTO audit_log (tenant_id, action, timestamp) VALUES (@TenantId, @Action, @Timestamp)",
                new { changeEvent.TenantId, Action = "UPDATE", Timestamp = DateTime.UtcNow }
            );
        });
    }
}
```

---

## 📊 性能基准测试

### 测试环境

- **Redis：** 6.2.6（单机模式，16GB 内存）
- **网络：** 局域网（~1ms 延迟）
- **租户数量：** 5000 个
- **并发数：** 100

### 测试结果

| 操作 | 平均延迟 | P95 延迟 | P99 延迟 | QPS |
|-----|---------|---------|---------|-----|
| **L1 命中（Memory）** | 0.02ms | 0.05ms | 0.08ms | 500K |
| **L2 查询（Redis）** | 0.8ms | 1.2ms | 1.5ms | 50K |
| **写入 + Pub/Sub** | 1.5ms | 2.0ms | 2.5ms | 30K |
| **预热（5000 配置）** | - | - | - | 5s |

### 对比传统方案（MySQL）

| 维度 | Redis-First | MySQL L3 | 性能提升 |
|-----|------------|----------|---------|
| **平均延迟** | 0.8ms | 10ms | **12x** |
| **P99 延迟** | 1.5ms | 25ms | **16x** |
| **QPS** | 50K | 5K | **10x** |
| **架构复杂度** | 简单 | 复杂 | - |

---

## 🔄 数据备份与恢复

### 1. RDB 快照备份（每小时）

```bash
#!/bin/bash
# 备份脚本：每小时备份 RDB 快照到 S3

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RDB_FILE="/data/redis/nexus-tenant-config.rdb"
S3_BUCKET="s3://nexus-backup/redis/"

# 触发 Redis 保存快照
redis-cli BGSAVE

# 等待快照完成
while [ $(redis-cli LASTSAVE) -eq $(redis-cli LASTSAVE) ]; do
  sleep 1
done

# 上传到 S3
aws s3 cp $RDB_FILE "${S3_BUCKET}nexus-config-${TIMESTAMP}.rdb"

# 保留最近 30 天的备份
aws s3 ls $S3_BUCKET | while read -r line; do
  file_date=$(echo $line | awk '{print $4}' | cut -d'-' -f3 | cut -d'.' -f1)
  if [ $(date -d "$file_date" +%s) -lt $(date -d '30 days ago' +%s) ]; then
    file_name=$(echo $line | awk '{print $4}')
    aws s3 rm "${S3_BUCKET}${file_name}"
  fi
done
```

### 2. 数据恢复

```bash
# 1. 停止 Redis
sudo systemctl stop redis

# 2. 从 S3 下载备份
aws s3 cp s3://nexus-backup/redis/nexus-config-20260110_143000.rdb /data/redis/nexus-tenant-config.rdb

# 3. 启动 Redis（自动加载 RDB）
sudo systemctl start redis

# 4. 验证数据
redis-cli
> KEYS nexus:config:*
> GET nexus:config:Alipay:2088123456789012:2021001234567890
```

---

## 🚀 生产部署清单

### Redis 集群配置（推荐）

```yaml
# Redis Sentinel 高可用配置
sentinel:
  master: redis-master
  replicas:
    - redis-replica-1
    - redis-replica-2
  quorum: 2
  down-after-milliseconds: 5000
  failover-timeout: 10000
```

### 监控指标

| 指标 | 告警阈值 | 说明 |
|-----|---------|------|
| **内存使用率** | > 80% | 扩容或清理无效配置 |
| **RDB 保存失败** | > 0 | 检查磁盘空间 |
| **AOF 重写失败** | > 0 | 检查磁盘 I/O |
| **主从同步延迟** | > 5s | 网络或负载问题 |
| **Pub/Sub 延迟** | > 100ms | 配置刷新延迟 |

---

## ❓ 常见问题

### Q1: Redis 数据丢失怎么办？

**A:** 
1. **RDB + AOF 混合持久化**：最多丢失 1 秒数据
2. **每小时 RDB 备份到 S3**：灾难恢复
3. **可选 L4 数据库**：异步冷备份

### Q2: 如何支持复杂查询（如按租户名称搜索）？

**A:**
- **方案 1（推荐）：** 在运营后台维护索引表（TenantId → Name 映射）
- **方案 2：** 使用 Redis Search 模块（RediSearch）
- **方案 3：** 接入 Elasticsearch（适合大规模搜索）

### Q3: 如何支持配置版本管理？

**A:**
- **方案 1：** Redis Hash 存储多版本（`nexus:config:Alipay:123:v1`, `v2`）
- **方案 2：** L4 数据库存储历史版本（审计日志）
- **方案 3：** Git 存储配置文件（适合 GitOps）

### Q4: 如何防止配置被误删？

**A:**
- **软删除：** 移动到 `nexus:config:deleted:` 前缀，30 天后清理
- **访问控制：** Redis ACL 限制删除权限
- **操作日志：** L4 数据库记录所有变更

---

## 📚 相关文档

- [ARCHITECTURE_BLUEPRINT.md](./ARCHITECTURE_BLUEPRINT.md) - 架构蓝图 v1.1
- [IMPLEMENTATION.md](./IMPLEMENTATION.md) - 实现指南
- [Redis 官方文档 - Persistence](https://redis.io/docs/manual/persistence/)
- [StackExchange.Redis 文档](https://stackexchange.github.io/StackExchange.Redis/)

---

**维护者：** NexusContract Team  
**最后更新：** 2026-01-10
