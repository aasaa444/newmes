# 第一期演示脚本（可重复）

目标：陌生人按本文 + `README.md` 启动后，15 分钟内走完**黄金路径**与**异常支线**，并展示 ERP 出站与审计。

自动化等价证据：`dotnet test` 中的 `Phase1E2eSeamTests`（票 07）。

## 0. 启动

```powershell
# 终端 1
sqllocaldb start MSSQLLocalDB
dotnet run --project src/Mes.Api
# http://localhost:5101/health → Healthy

# 终端 2
cd web
npm install
npm run dev
# 浏览器打开 Vite 提示的地址（默认 http://localhost:5173）
```

| 账号 | 密码 | 用途 |
|------|------|------|
| planner | Planner@123 | 主数据、工单、领料、关单、集成页 |
| operator | Operator@123 | 过站台 |
| leader | Leader@123 | 看工单/隔离/谱系（只读为主） |

种子：成品 `FG-ROUTER`，路线 `ONLINE→FLASH→ASSEMBLY→FQC→PACK`，线边库存已预置。

---

## 1. 黄金路径（UI）

### 1.1 计划员 — 工单与领料

1. 登录 **planner** → 计划端 → **生产工单**  
2. 计划数量填 `1` → **创建并下达**  
3. 对该工单点 **齐套领料**（线边数量应下降）  
4. （可选）打开 **集成与审计**，确认已有 `MaterialIssue` 出站报文  

### 1.2 操作工 — 过站与关键件

1. 退出，登录 **operator** → 自动进 **过站台**  
2. 工位选 **上线工位 (ONLINE)**，工单选刚下达的那张  
3. SN 可留空（系统发号）或输入 `SN-DEMO-001` → **过站合格**  
4. 在「绑定关键件」输入 PCB 序列号如 `PCB-DEMO-001` → **绑定**  
5. 工位依次切换：烧录 → 组装 → 终检 → 包装，每次填入同一成品 SN → **过站合格**  
6. 末站后点 **完工入库（路线完成）**  

### 1.3 计划员 — 关单与展示

1. 登录 **planner** → 生产工单 → 状态应为 **Completed** → **关闭**  
2. **集成与审计**：  
   - 成品库存 `FG-ROUTER` ≥ 1  
   - 出站含 `ProductionReceipt`、`WorkOrderClose`  
   - 审计含 `StationPass`、`ComponentBound`、`FinishedGoodsReceived`、`WorkOrderClosed`  
3. 可用任意角色调 API：`GET /api/genealogy/{成品SN}` 查看 5 站 + 关键件  

---

## 2. 异常支线（UI）

1. 计划员再建工单数量 `1`，下达并领料  
2. 操作工：ONLINE 过站后，工位切到 **FLASH**，填 SN → **隔离**（填原因）  
3. 此时 **完工入库** 应失败提示 isolated  
4. 计划员/班组长：过站台下方「隔离中」→ **放行**  
5. 操作工从 FLASH 起继续过站至 PACK → 入库  
6. 另开一台：ONLINE 后 FLASH **报废** → 工单 `ScrappedQty` +1，不可再过站  

可选：**返工** — FLASH 点「返工(回上线)」后当前工序回到 ONLINE，谱系保留 Fail + Rework。

---

## 3. 防跳站与取消（面试常问）

| 场景 | 预期 |
|------|------|
| 新 SN 直接在 FLASH 过站 | 失败（须首站） |
| SN 当前在 FLASH，却在 FQC 过站 | 失败 `anti-skip` |
| 已下达、无在制 SN 取消 | 成功 Cancelled |
| 已有在制 SN 取消 | 失败 |

---

## 4. API 速查（无 UI 时）

```http
POST /api/auth/login  {"userName":"planner","password":"Planner@123"}
POST /api/work-orders
POST /api/work-orders/{id}/release
POST /api/work-orders/{id}/issue
POST /api/station/pass
POST /api/station/bind-component
POST /api/station/fail
POST /api/quality/release
POST /api/completion/receive
POST /api/work-orders/{id}/close
GET  /api/genealogy/{sn}
GET  /api/erp/outbox
GET  /api/audit
```

Bearer：登录返回的 `accessToken`。

---

## 5. 自动化一键验收

```powershell
dotnet test src/Mes.Api.Tests --filter "FullyQualifiedName~Phase1E2eSeamTests"
# 或全量
dotnet test src/Mes.Api.Tests
```

绿 = 第一期完成定义中的闭环、异常、RBAC 关键规则、ERP/审计可观测性已由测试锁定。
