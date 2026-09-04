# 公开证据、现场调研与业务验证计划

## 1. 目的

本计划防止候选 MES 把“可以编码的流程”误写成“真实工厂一定这样做”。公开标准用于稳定系统边界和追溯语义，厂商资料用于证明产品身份、固件和设备事实，现场调研用于决定具体站序、阈值、权限和责任。

## 2. 当前公开证据基线

| 来源 | 可支持的结论 | 不能支持的结论 |
|---|---|---|
| IEC 62264-1 / GB/T 20720.1 | 企业计划层与制造运行管理层职责分层 | 某家用友/金蝶的具体接口字段 |
| IEC 62264-2:2026 | Level 3/4 交换信息对象和模型边界 | 工业路由器固定工位顺序 |
| IEC 62264-5:2016 | 业务与制造应用之间的交换事务 | 当前客户采用实时还是批量接口 |
| GS1 EPCIS 2.0 | 事件的 what/when/where/why、业务步骤、处置和纠错语义 | 某工厂必须采用 EPCIS 存储格式 |
| IPC-1782A 发布说明 | 电子产品制造和供应链追溯应按风险确定最低要求 | 所有物料必须逐件 SN 追溯 |
| Moxa 工业路由器产品与保修页面 | 工业路由器存在型号、固件、硬件、MAC/SN 等产品事实 | Moxa 的真实工厂 SOP、测试阈值和站序 |
| Microsoft Dynamics 365 WMS 文档 | 仓储接收、库位、波次、包装等属于仓储职责 | MES 完工可直接等同仓储或 ERP 入账 |

## 3. 主要来源

- IEC 62264-1: https://webstore.iec.ch/en/publication/6675
- IEC 62264-2:2026: https://webstore.iec.ch/en/publication/75127
- IEC 62264-5:2016: https://webstore.iec.ch/en/publication/25465
- GS1 EPCIS 2.0 标准: https://ref.gs1.org/standards/epcis/
- GS1 EPCIS Ontology: https://ref.gs1.org/standards/epcis/epcis-ontology.ttl
- IPC 标准发布信息: https://emails.ipc.org/emails/tech/ipc-standards.html
- Moxa EDR-G9010 产品页: https://www.moxa.com/en/products/industrial-network-infrastructure/secure-routers/secure-routers/edr-g9010-series
- Moxa Warranty Check: https://www.moxa.com/en/support/repair-and-warranty/warranty-check
- Dynamics 365 Warehouse Management: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview

## 4. 现场调研工作坊

| 工作坊 | 参与人 | 输入 | 输出 |
|---|---|---|---|
| 价值流与范围 | 发起人、计划、产线、工艺 | 订单、布局、SOP、班次 | 试点边界、主路径、例外和节拍 |
| 主数据与订单 | 计划、ERP、工艺 | 产品、物料、BOM、订单样例 | 权威矩阵、编码映射、下达和变更规则 |
| 身份与物料追溯 | 工艺、质量、物料 | 标签、关键件、Lot、召回要求 | 身份来源、追溯粒度和耗用时点 |
| 测试与质量 | 工艺、质量、测试工程 | 测试规范、报告、缺陷和返工记录 | 测试模型、处置权限、返工/复测规则 |
| ERP/WMS 集成 | ERP/IT、仓储、实施 | 接口文档、账号、单据和错误样例 | 接口清单、幂等键、回执、对账和补偿 |
| 工位与可用性 | 产线主管、操作工、IT | 工位设备、扫码枪、网络 | 交互步骤、提示、防错和离线边界 |
| 运维与安全 | IT、安全、项目经理 | 服务器、网络、备份和账号策略 | 部署拓扑、RPO/RTO、日志、监控和恢复 |
| UAT 与上线 | 发起人、关键用户、项目组 | 需求、风险和场景 | UAT 数据、验收人、试运行和上线门禁 |

## 5. 观察与取证原则

- 观察实际工位操作，不仅访谈管理人员；记录纸单、扫码、返工和交接的真实顺序。
- 对每条关键规则收集样例单据、报文、标签、测试报告或系统截图，并登记来源和脱敏要求。
- 区分“规定流程”和“现场实际做法”，差异进入 Fit-Gap 和风险台账。
- 对无法现场验证的内容保留 C/D 级，不通过多数意见或模型推断强行升级证据等级。
- 现场人员确认的是业务事实；架构和实现方案仍由项目组评估可行性、风险和成本。

## 6. 评审门禁

1. 工艺路线和追溯粒度必须由工艺/质量评审后才能从候选配置升级为已确认。
2. 测试阈值和设备参数必须来自批准规范或设备接口，不从互联网或开发代码推断。
3. ERP 字段和单据必须由目标版本接口文档和样例报文验证。
4. 权限和职责分离必须由客户组织责任确认，不能照搬演示角色。
5. 性能、保留年限和 RPO/RTO 必须经过容量与连续性评审。
6. 只有真实试运行数据和签字结果才能将可信度升级为“现场验证通过”。

