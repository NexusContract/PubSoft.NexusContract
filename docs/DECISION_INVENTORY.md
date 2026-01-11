# 决策清单（Decision Inventory）

**说明**：目标不是一步到位写完美文档，而是先把散落、重复、杂乱的决策“原形提取”出来，形成可审阅的档案馆索引。每条只保留核心结论、简短提取与来源引用（便于后续展开）。

**目录**
- 1. 核心架构与哲学决策 (Core & Philosophy)
- 2. 依赖与传输决策 (Dependency & Transport)
- 3. 多租户与配置决策 (Multi-Tenant & Config)
- 4. 运行稳定性与隔离决策 (Stability & Isolation)

---

### 1. 核心架构与哲学决策 (Core & Philosophy)

| 决策编号 | 决策名称 | 核心内容提取 | 来源出处 |
| --- | --- | --- | --- |
| **ADR-001** | **入口/出口物理分离** | 采用 FastEndpoints 处理 Ingress（入口），YARP 处理 Egress（出口），职责分离以简化边界与路由策略。 | `BLUEPRINT.md` |
| **ADR-002** | **客户端零依赖 (Purity)** | `NexusContract.Client` SDK 保持纯净，不依赖宿主框架，便于在不同宿主中复用。 | `BLUEPRINT.md` |
| **ADR-003** | **四阶段标准化管道** | 标准执行流：验证 (Validate) → 投影 (Project) → 执行 (Execute) → 回填 (Hydrate)，作为全栈流程约定。 | `IMPLEMENTATION.md` |
| **ADR-004** | **启动期体检机制** | 通过 `Preload` 冻结/预热元数据，尽量避免运行期反射，提升 P99 稳定性（建议在 CI/启动验证）。 | `IMPLEMENTATION.zh-CN.md` |
| **ADR-007** | **向下兼容性要求** | Abstractions 层保持对 .NET Standard 2.0 兼容，避免使用新语法（如 `record`）以兼容更广宿主。 | `BLUEPRINT.md` |

---

### 2. 依赖与传输决策 (Dependency & Transport)

| 决策编号 | 决策名称 | 核心内容提取 | 来源出处 |
| --- | --- | --- | --- |
| **ADR-005** | **依赖倒置 (DIP) 边界** | 把接口放在 `Abstractions`，实现放在 `Hosting`；Provider 不应直接引用 YARP 的具体实现，保持边界清晰。 | `DEPENDENCIES.md` |
| **ADR-006** | **传输层接口抽象** | 统一出口抽象为 `INexusTransport`，生产环境默认绑定 YARP，实现互换性与测试替换。 | `DEPENDENCIES.md` |
| **ADR-012** | **IProvider 适配器模式** | 使用适配器将无状态 Provider 与 `NexusEngine` 的调度逻辑连接，保持 Provider 轻量且可替换。 | `ROADMAP.md` |

---

### 3. 多租户与配置决策 (Multi-Tenant & Config)

| 决策编号 | 决策名称 | 核心内容提取 | 来源出处 |
| --- | --- | --- | --- |
| **ADR-008** | **Redis-First 存储策略** | 使用 Redis 作为主存储，建议 RDB + AOF 混合持久化；生产建议审慎配置内存淘汰策略（默认不使用 noeviction 作为绝对规则，需按 SLA 与容量调整）。 | `REDIS_GUIDE.md` |
| **ADR-009** | **三层数据模型 (Map/Inst/Pool)** | 数据分层：授权映射 (Map)、实例参数 (Inst)、物理资产/池 (Pool)，用于逻辑隔离与高效定位。 | `ADR-009.md` |
| **ADR-013** | **Realm 与 Profile 抽象** | 引入 `RealmId`（SysId）与 `ProfileId`（AppId）以去渠道化术语，统一命名与多商家隔离。 | `BLUEPRINT.md` |
| **ADR-014** | **默认解析与自愈策略** | 支持精确匹配 `AppId`，并提供默认/自动查找（Default/First）机制以提高容错与回退能力。 | `MULTI_APPID_GUIDE.md` |

---

### 4. 运行稳定性与隔离决策 (Stability & Isolation)

| 决策编号 | 决策名称 | 核心内容提取 | 来源出处 |
| --- | --- | --- | --- |
| **ADR-010** | **BFF/HttpApi 职责解耦** | BFF 负责 Map 层的鉴权与路由决策；HttpApi 负责对 Inst/Pool 层的受信任执行与状态管理，避免跨层职责混淆。 | `ADR-010.md` |
| **ADR-011** | **URL 资源寻址与复合 ID** | 在 URL 路径中编码 `spid-appid` 等复合 ID，支持 O(1) 内存寻址与物理隔离（实现需注意长度/编码边界）。 | `ADR-010.md` |
| **ADR-015** | **懒加载与永久缓存策略** | HttpApi 对执行态采用懒加载 + `NeverRemove`（永久缓存）策略以支持脱网自治，但需配合监控与重试策略。 | 会议达成共识 |
| **ADR-016** | **新商家上线隔离 (Fail-Fast)** | 冷启动路径建议设置极短超时（示例 500ms）以防新商家阻塞老商家流量；该值需基于实验与 SLA 调整。 | `ADR-009.md` |

---

## 后续建议（短）
- P0：对 `ADR-010`、`ADR-009` 的实现引用做链接校验，确保表格中的“来源出处”能直接定位到实现文件（可自动生成路径清单）。
- P1：把每条决策扩展为单段「背景 + 决策 + 影响范围 + 实现位置」的 ADR 草稿（小步快跑）。
- P2：在本文件中加入维护人和最后更新时间列，便于追踪责任与演进。

---

*生成时间：2026-01-11*  
*作者：架构组（初稿）*
