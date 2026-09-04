# 工业路由器整机装配与单件追溯：公开证据基线

> 研究日期：2026-07-29  
> 研究对象：中国中小型工业电子制造企业的一条工业路由器整机装配线  
> 使用边界：本文件是试点候选系统的公开证据基线，不是现场调研结论，也不是某一厂商的实际 SOP。

## 结论摘要

1. 公开标准能够直接支持：MES 位于制造运营层，承接业务计划层与现场控制层之间的信息；制造事件应保留对象、时间、地点/工位、业务步骤、状态/处置、业务单据及输入输出关系；测试应关联被测对象、测试规范、结果、实际资源与作业定义。[IEC 62264-1](https://webstore.iec.ch/en/publication/6675)（访问：2026-07-29）、[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）、[GS1 EPCIS 2.0.1 规范工件](https://ref.gs1.org/standards/epcis/artefacts)（访问：2026-07-29）。
2. 工业路由器厂商公开资料能够直接支持：产品存在可识别的 SN、MAC、型号、固件版本、硬件版本，以及带版本号和校验和的固件包；这些字段适合作为整机身份、固件写入/核验和终检的候选采集项。[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29）、[Moxa EDR-G9010 产品页](https://www.moxa.com/en/products/industrial-network-infrastructure/secure-routers/secure-routers/edr-g9010-series)（访问：2026-07-29）。
3. 本次检索到的厂商第一方页面公开了产品规格、固件和产品标识，但没有公开完整工厂 SOP，因而不能把 `ONLINE → FLASH → ASSEMBLY → FQC → PACK` 宣称为行业共识。[Moxa EDR-G9010 产品页](https://www.moxa.com/en/products/industrial-network-infrastructure/secure-routers/secure-routers/edr-g9010-series)（访问：2026-07-29）、[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29）。最多只能把“整机装配/部件关联、固件或配置版本建立、功能/终检、标识核验、完工移交”视为证据支持的候选能力；站点拆分、先后顺序、返工点和责任角色仍须现场验证。
4. 当前五站路线最需要纠正的不是多加站，而是去除误导性名称：`ONLINE` 应表示“SN 建档/工单投产事件”，不能同时冒充装配；`FLASH` 应明确记录固件包、版本、校验和与执行结果；`FQC` 应改成可验证的测试/终检定义；`PACK` 是否属于 MES 过站应由仓储交接方式决定。这是基于事件、测试与 WMS 边界作出的设计建议，不是标准规定。[GS1 EPCIS JSON Schema](https://ref.gs1.org/standards/epcis/epcis-json-schema.json)（访问：2026-07-29）、[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）、[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29）。

## 证据等级

| 等级 | 含义 | 本项目允许的用法 |
|---|---|---|
| A：来源直接支持 | 官方标准页面、规范工件或厂商第一方资料明确陈述 | 可作为系统边界、数据模型或候选字段的公开依据 |
| B：由来源合理推导 | 来源给出对象/能力，但没有规定本项目的具体实现 | 可作为试点候选设计，必须写明推导链，不得称行业强制规则 |
| C：业务假设 | 公开来源不足，依赖具体工厂、产品、设备、组织或合同 | 可以进入待验证清单；不得固化成“真实行业流程” |

标准本身有版权限制。本研究只使用标准组织公开的标题、摘要、公开规范工件与厂商公开页面，不把付费标准正文当作已审阅材料。

## 一、MES、ERP 与 WMS 边界

| 事实或设计结论 | 等级 | 证据与解释 |
|---|---:|---|
| 制造运营管理是 Level 3；它与 Level 4 业务系统交换信息，并与更低层控制活动衔接。 | A | IEC 官方摘要明确描述 Level 3 制造运营管理域及 Level 3/4 接口。[IEC 62264-1](https://webstore.iec.ch/en/publication/6675)（访问：2026-07-29）。中国现行国标采用相同主题与术语。[GB/T 20720.1-2019](https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=17B23AC4C780F6E2B50C09361631816F)（访问：2026-07-29）。 |
| MES/ERP 集成应通过明确的信息对象与事务，而不是双方直接共享内部表。 | A | IEC 62264-2 将 Level 3 与 Level 4 交换内容定义为相互关联的概念信息模型；IEC 62264-5定义业务与制造应用之间的信息交换事务。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）、[IEC 62264-5:2016](https://webstore.iec.ch/en/publication/25465)（访问：2026-07-29）。 |
| 对本试点，ERP 侧候选职责是物料/产品编码、业务计划和生产订单来源、库存总账与财务结果；MES 侧候选职责是工单执行、SN 在制、工序事件、测试、质量处置、耗料与完工实绩。 | B | 这是由 IEC 的 Level 4/Level 3 分层与接口模型推导出的实施边界；公开摘要没有规定“用友/金蝶必须交换哪些字段”。[IEC 62264-1](https://webstore.iec.ch/en/publication/6675)（访问：2026-07-29）、[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）。 |
| 完整 WMS 通常包含收货/入库、库位、上架、移动、波次、拣选、包装、盘点、容器/托盘与条码等仓内执行能力。 | A | Microsoft 的 WMS 第一方产品文档逐项列出入库/出库工作流、库位指令、批次/序列、收货、拣选、移动、盘点、包装和容器化等能力。[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29）。 |
| 目标工厂暂时没有独立 WMS 时，MES 可以保留“线边领料、关键件绑定耗用、合格完工、仓储交接状态”的轻量台账，但不得把它称为完整 WMS。 | B | 这是由上述 WMS 能力边界与 IEC Level 3 制造执行边界共同推导出的试点裁剪。[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29）、[IEC 62264-1](https://webstore.iec.ch/en/publication/6675)（访问：2026-07-29）。 |
| 成品包装是否由 MES 过站、由 WMS 执行，还是仅由 ERP 记账，取决于组织责任与系统现状。 | C | WMS 产品资料证明“包装”可属于仓储出库能力，但没有来源证明本目标工厂的包装职责。[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29）。 |

### 推荐接口所有权（试点候选）

| 信息/动作 | 建议权威方 | 交换方向 | 证据状态 |
|---|---|---|---|
| 产品、物料编码及基础单位 | ERP；MES 保留执行扩展属性 | ERP → MES | B：由 Level 4/3 分层推导；具体客户若无稳定 ERP 主数据，可阶段性由 MES 维护。[IEC 62264-1](https://webstore.iec.ch/en/publication/6675)（访问：2026-07-29） |
| BOM、工艺/作业定义及版本 | 需实施约定；MES 必须冻结工单执行快照 | ERP/PLM/MES → MES 执行 | B：IEC 支持作业定义与交换对象，未指定本客户系统所有权。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29） |
| 生产订单/计划数量/计划日期 | ERP 或计划系统 | ERP → MES | B：由 Level 4 业务计划与 Level 3 执行分层推导。[IEC 62264-1](https://webstore.iec.ch/en/publication/6675)（访问：2026-07-29） |
| SN 在制状态、工序事件、部件绑定、测试和质量处置 | MES | MES 内部；必要时摘要外发 | A/B：Level 3 事件、测试与对象关系由 IEC/GS1 支持；具体字段是本试点裁剪。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）、[GS1 EPCIS JSON Schema](https://ref.gs1.org/standards/epcis/epcis-json-schema.json)（访问：2026-07-29） |
| 原料仓/成品仓的库位、上架、拣选、盘点、容器和波次 | WMS；无 WMS 时不在一期伪造 | WMS ↔ ERP/MES | A/B：WMS 能力由第一方产品文档直接支持；本项目不实现是范围裁剪。[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29） |
| 耗料、报废、合格完工/入库交接实绩 | MES 产生事实，ERP/WMS 完成账务或仓储确认 | MES → ERP/WMS | B：由 IEC Level 3/4 信息交换与 WMS 生产集成能力推导。[IEC 62264-5:2016](https://webstore.iec.ch/en/publication/25465)（访问：2026-07-29）、[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29） |

## 二、公开证据能支持到什么程度的工艺阶段

公开资料支持“能力和事实”，不支持某家工厂的精确站序。下表给出可用于下一版设计的候选阶段，而不是已确认 SOP。

| 候选阶段 | 可被公开证据支持的部分 | 不能由公开证据证明的部分 | 等级 |
|---|---|---|---:|
| 工单投产与 SN 建档 | 制造事件可以关联对象标识、时间、地点、业务步骤和业务单据；Moxa 产品使用 SN 作为唯一产品识别信息之一。[GS1 EPCIS JSON Schema](https://ref.gs1.org/standards/epcis/epcis-json-schema.json)（访问：2026-07-29）、[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29） | SN 是在工单下达时预生成、首站扫码时创建，还是标签打印后导入；是否需要独立物理工位 | A（字段）/C（时机） |
| 整机装配与关键件绑定 | GS1 `TransformationEvent` 公开规范工件定义输入对象/数量、输出对象/数量及其转换关系，足以支持“关键部件 → 成品 SN”的谱系模型。[GS1 EPCIS Ontology](https://ref.gs1.org/standards/epcis/epcis-ontology.ttl)（访问：2026-07-29）、[GS1 EPCIS JSON Schema](https://ref.gs1.org/standards/epcis/epcis-json-schema.json)（访问：2026-07-29） | 工业路由器究竟绑定主板 SN、电源模块 SN、蜂窝模组 IMEI、无线模组、外壳还是其他件；装配顺序和扭矩/防错要求 | A（关系模型）/C（部件与作业） |
| 固件/配置写入与版本核验 | Moxa 产品页公开了面向具体系列的固件包、版本、发布日期和 SHA-512 校验和；保修查询页说明可从产品读取固件版本。[Moxa EDR-G9010 产品页](https://www.moxa.com/en/products/industrial-network-infrastructure/secure-routers/secure-routers/edr-g9010-series)（访问：2026-07-29）、[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29） | 固件在 PCBA 阶段、装壳前还是装壳后写入；是否还写 MAC、证书、IMEI、默认配置；失败返工点 | A（产品有版本化固件）/C（制造步骤与顺序） |
| 功能测试与结果采集 | IEC 62264-2:2026 的官方摘要明确新增测试模型，关联测试规范、测试结果、被测对象、实际资源和作业定义；Moxa 产品页公开了电源、以太网、串口、吞吐量、LED、温湿度等可检验产品属性。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）、[Moxa EDR-G9010 产品页](https://www.moxa.com/en/products/industrial-network-infrastructure/secure-routers/secure-routers/edr-g9010-series)（访问：2026-07-29） | 本产品的 ICT/FCT/老化/安规/射频测试项目、限值、抽检或全检策略、测试设备和校准要求 | A（测试模型与可测属性）/C（测试方案） |
| 外观、铭牌与身份一致性终检 | Moxa 明确说明产品标签/系统可提供 SN、MAC、型号、固件版本和硬件版本，支持把身份一致性作为终检候选项。[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29） | 标签打印发生在哪一站、标签格式、法规标识、附件清单及外观缺陷标准 | A（身份字段）/C（检验规范） |
| 质量隔离、返工、报废和放行 | GS1 EPCIS 事件含 `disposition`，并以错误声明保留原事件而非覆盖历史；IPC-1782A 官方发布说明称其按风险建立电子产品制造与供应链追溯最低要求。[GS1 EPCIS Ontology](https://ref.gs1.org/standards/epcis/epcis-ontology.ttl)（访问：2026-07-29）、[IPC-1782A 官方发布说明](https://emails.ipc.org/emails/tech/ipc-standards.html)（访问：2026-07-29） | 具体缺陷码、MRB 权限、返工路线、让步接收、报废审批和物理隔离区 | A（历史/处置语义）/C（处置流程） |
| 完工、包装与仓储交接 | IEC 支持 Level 3/4 事务交换；WMS 第一方资料将包装、容器化、收货、库位、移动等列为仓储能力。[IEC 62264-5:2016](https://webstore.iec.ch/en/publication/25465)（访问：2026-07-29）、[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29） | 包装是否是 MES 必经站，还是完工后由仓储执行；是否追踪箱号/托盘号及 SN 装箱关系 | A（系统能力边界）/C（本厂责任） |

本次检索**不能支持**把 SMT、回流焊、AOI、ICT、老化、射频校准或安规耐压自动加入路线。IPC-1782A 适用于电子产品制造与供应链追溯，但其公开发布说明只表达“按风险设定最低追溯要求”，不等于它为工业路由器规定上述工序。[IPC-1782A 官方发布说明](https://emails.ipc.org/emails/tech/ipc-standards.html)（访问：2026-07-29）。当前项目把 `PCB-MAIN` 当作外购/已完成关键件时，PCBA 制造过程尤其不应被擅自并入整机装配路线。

## 三、现有五站路线审查

当前路线来自 `src/Mes.Api/MasterData/MasterDataSeed.cs` 的演示种子：`ONLINE → FLASH → ASSEMBLY → FQC → PACK`。其存在能证明系统实现了一条线性演示路线，不能证明路线真实。

| 现站 | 审查结论 | 建议命名/处理 | 证据等级 |
|---|---|---|---:|
| `ONLINE` / 上线装配 | “上线”可以是 MES 的投产事件，但“上线装配”同时混合 SN 建档、工单关联与物理装配；没有来源证明它必须是独立第一站。 | 改为 `START_WIP` / “SN 建档与工单投产”，只做身份、工单和路线版本建立；若现场是预打印标签，则改为扫入既有 SN。是否独立成站保持可配置。 | B（事件）/C（时机与站点） |
| `FLASH` / 烧录测试 | 产品存在版本化固件可证实；工厂必须设置独立烧录站、以及先烧录后装壳不可证实。当前名称还把“写入”和“测试”混为一体。 | 改为 `FW_CONFIG` / “固件与配置写入核验”；至少记录固件包 ID、版本、校验和、配置版本、结果、工具/设备与时间。与 `ASSEMBLY_BIND` 的先后由路线版本决定。 | A（固件事实）/C（站序） |
| `ASSEMBLY` / 外壳组装 | 工业路由器有金属外壳、电源及网络接口等物理组成可证实；具体装配内容、关键件清单和顺序不可证实。[Moxa EDR-G9010 产品页](https://www.moxa.com/en/products/industrial-network-infrastructure/secure-routers/secure-routers/edr-g9010-series)（访问：2026-07-29） | 改为 `ASSEMBLY_BIND` / “整机装配与关键件绑定”；把绑定/解绑做成带时间、人员、工位和原因的谱系事件。 | B（需要输入输出关联）/C（作业内容） |
| `FQC` / 终检 | 需要测试对象、测试规范与测试结果有强标准依据，但“FQC”过于宽泛，无法回答到底检了什么；也不能证明一站覆盖功能、外观、标签、安规和射频。 | 优先改为 `FUNCTION_TEST` / “功能测试”，记录项目级结果；如真实流程需要，再新增 `FINAL_INSPECTION` / “外观与标签终检”。不得用单一 Pass/Fail 冒充测试数据。 | A（测试数据模型）/C（测试项与拆站） |
| `PACK` / 包装 | 产品最终会形成交付包装是常识性推断，但没有证据证明包装是本 MES 的必经工艺站。WMS 产品能力中也包含包装。 | 暂改为 `PACK_HANDOFF` / “包装与仓储交接（待确认）”，或从核心制造路线移出，在合格完工后记录仓储交接；只有需要箱号/附件/标签防错时才保留 MES 过站。 | C |

### 可用于验证的候选路线，而非行业定论

```text
工单投产事件
  -> [整机装配与关键件绑定]
  -> [固件/配置写入与版本核验]
  -> [功能测试]
  -> [外观/标签终检（可选）]
  -> 合格完工
  -> [包装/仓储交接（责任待确认）]
```

`整机装配` 与 `固件写入` 的先后不应在研究阶段定死；应允许两个已批准的路线版本，待产品结构和夹具可达性确认后选定。返工也不能默认“烧录失败回上线、终检失败回装配”，必须由缺陷类别映射到经批准的返工作业。

## 四、按成品 SN 追溯必须采集的事实

这里的“必须”指：为了让本试点可靠回答“这台成品是谁、按哪张工单和哪个版本生产、用了什么、经历了什么、测试与质量结果如何、最终去了哪里”，系统不可缺少；它不代表所有字段都是行业法规强制项。

| 事实组 | 最小字段 | 证据/推导 | 等级 |
|---|---|---|---:|
| 成品身份 | 成品 SN、产品/物料编码、型号、硬件版本；必要时 MAC/IMEI 等产品标识 | Moxa 明确将 SN、MAC、型号、固件版本、硬件版本作为产品可查询信息。[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29） | A（字段存在）/B（纳入本项目） |
| 制造上下文 | 工单号、工厂/产线、冻结的 BOM 版本、路线/作业定义版本、计划数量 | IEC 62264-2 支持制造运营对象、作业定义、位置和记录的交换；把这些字段绑定到 SN 是实现“按版本追溯”的必要推导。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29） | B |
| 事件公共头 | 不可变事件 ID、事件发生时间、时区、记录时间、事件类型/动作、工序/业务步骤、工位/地点、结果/处置 | GS1 EPCIS JSON Schema 直接定义 `eventTime`、`recordTime`、`eventTimeZoneOffset`、`eventID`、`action`、`bizStep`、`disposition`、`readPoint`、`bizLocation` 等事件属性。[GS1 EPCIS JSON Schema](https://ref.gs1.org/standards/epcis/epcis-json-schema.json)（访问：2026-07-29） | A |
| 执行主体 | 操作人/账号、班组；自动采集时记录设备/应用身份 | IEC 的测试模型关联实际资源；IPC-1782A 以风险为基础提出电子产品追溯最低要求。具体追到人还是设备必须由风险和隐私制度确定。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）、[IPC-1782A 官方发布说明](https://emails.ipc.org/emails/tech/ipc-standards.html)（访问：2026-07-29） | B/C |
| 关键件谱系 | 输入物料编码、关键件 SN 或批次、数量、输出成品 SN、绑定/解绑动作、时间、工位、原因 | GS1 `TransformationEvent` 直接定义输入 EPC/数量、输出 EPC/数量和 transformation ID，`AssociationEvent` 定义父子对象的关联/解除。[GS1 EPCIS Ontology](https://ref.gs1.org/standards/epcis/epcis-ontology.ttl)（访问：2026-07-29）、[GS1 EPCIS JSON Schema](https://ref.gs1.org/standards/epcis/epcis-json-schema.json)（访问：2026-07-29） | A（模型）/B（本项目映射） |
| 非序列化物料 | 物料编码、供应/生产 Lot（风险需要时）、实际数量、单位、耗用/退回事件 | GS1 EPCIS 同时支持实例标识列表与数量列表；IPC-1782A 强调按风险选择追溯深度。[GS1 EPCIS JSON Schema](https://ref.gs1.org/standards/epcis/epcis-json-schema.json)（访问：2026-07-29）、[IPC-1782A 官方发布说明](https://emails.ipc.org/emails/tech/ipc-standards.html)（访问：2026-07-29） | A（模型）/C（哪些物料采 Lot） |
| 固件与配置 | 固件产品/包 ID、版本、校验和、配置模板版本、烧录/核验结果、执行工具/设备、时间；如写入证书/MAC/IMEI，记录其受控引用而非明文秘密 | Moxa 产品页直接公开具体固件包、版本和 SHA-512 校验和，保修页说明设备固件版本可读取。[Moxa EDR-G9010 产品页](https://www.moxa.com/en/products/industrial-network-infrastructure/secure-routers/secure-routers/edr-g9010-series)（访问：2026-07-29）、[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29） | A（版本/校验事实）/B（逐 SN 绑定） |
| 测试 | 测试规范 ID/版本、被测 SN、测试项目、测量值、单位、上下限、项目结果、总结果、测试设备/夹具、执行时间；原始日志或报告 URI/哈希 | IEC 62264-2:2026 官方摘要明确说明测试模型关联测试规范、测试结果、被测对象、实际资源与作业定义。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29） | A（关联模型）/B（最小字段展开） |
| 质量异常与处置 | 不合格事件、缺陷码/现象、发现工序、隔离状态、处置结论、原因、授权人、返工路线/作业版本、复测结果、报废/放行时间 | EPCIS 直接支持 disposition，并规定错误声明保留原事件而非改写历史；具体 MRB/返工审批由工厂制度决定。[GS1 EPCIS Ontology](https://ref.gs1.org/standards/epcis/epcis-ontology.ttl)（访问：2026-07-29） | A（历史与处置语义）/C（审批规则） |
| 标签一致性 | 实际打印/读取的 SN、MAC、型号、硬件版本，标签模板版本，核验结果 | Moxa 说明这些产品信息可在标签或设备界面获得。[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29） | A（字段）/B（标签核验事件） |
| 完工与去向 | 合格完工事件、完工时间、仓储交接单/容器（若使用）、目标地点、接收确认、ERP/WMS 消息 ID、发送状态、重试次数和对方回执 | EPCIS 支持业务单据、来源/目的地与地点；IEC 62264-5 支持 Level 3/4 事务交换；WMS 文档支持生产集成、包装与收货。[GS1 EPCIS JSON Schema](https://ref.gs1.org/standards/epcis/epcis-json-schema.json)（访问：2026-07-29）、[IEC 62264-5:2016](https://webstore.iec.ch/en/publication/25465)（访问：2026-07-29）、[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29） | A（对象类别）/B（接口审计字段） |
| 更正与审计 | 原记录不可覆盖；冲正/更正引用原事件并说明原因、操作者和时间；关键主数据与路线版本变更留痕 | EPCIS 错误声明明确保留原事件并以新声明指明原断言错误。[GS1 EPCIS Ontology](https://ref.gs1.org/standards/epcis/epcis-ontology.ttl)（访问：2026-07-29） | A（事件更正语义）/B（扩展到业务审计） |

### 当前系统不可只保留的“伪追溯”

- 只保存“SN 在五站均 Pass”，但没有固件版本/包、测试规范、测试项目和测量结果，不能证明产品按什么标准检验合格。该判断由 IEC 测试对象模型合理推导。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）。
- 只保存当前绑定关系、失败时删除旧绑定，无法回答历史上装过什么、为什么更换。GS1 事件与错误声明模型要求历史以事件保留，而非覆盖。[GS1 EPCIS Ontology](https://ref.gs1.org/standards/epcis/epcis-ontology.ttl)（访问：2026-07-29）。
- 把所有物料强制采 SN 并非公开共识。IPC-1782A 的公开说明强调风险分级，故关键件、批次件、纯数量件应按召回/质量风险确定粒度。[IPC-1782A 官方发布说明](https://emails.ipc.org/emails/tech/ipc-standards.html)（访问：2026-07-29）。
- 将“包装完成”直接等同于“ERP 已入库”会混淆制造事实、仓储接收和财务记账；这三者应通过可追踪事务衔接。该结论由 IEC Level 3/4 事务与 WMS 仓储职责合理推导。[IEC 62264-5:2016](https://webstore.iec.ch/en/publication/25465)（访问：2026-07-29）、[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29）。

## 五、后续访谈与验证清单

### P0：不回答就不能声称路线可信

1. 实际产品边界是什么：主板是外购已测 PCBA，还是本厂生产？电源模块、蜂窝模组、无线模组、SIM/eSIM、天线和外壳分别由谁装配？
2. 成品 SN、MAC、IMEI、证书和硬件版本分别由谁生成、何时写入、标签何时打印？重复或错绑如何处置？
3. 固件在装壳前还是装壳后写入？写入的是通用固件、客户配置还是设备唯一凭据？生产夹具能否在装壳后访问接口？
4. 哪些部件必须逐件追 SN，哪些只追 Lot，依据是召回风险、客户合同、法规还是内部质量策略？
5. 每一类产品实际执行哪些测试：上电、以太网、串口、蜂窝、Wi-Fi、GNSS、DI/DO、吞吐、老化、安规、射频或其他？哪些全检、哪些抽检？
6. 每个测试的规范版本、上下限、设备/夹具、校准状态和原始报告在哪里？复测是追加记录还是覆盖原结果？
7. 不合格品由谁判定隔离、返工、让步放行或报废？缺陷到返工工序的映射是什么？
8. “包装”由生产还是仓库负责？是否需要附件防错、箱号/托盘号、SN 装箱关系？哪个动作代表 MES 完工，哪个动作代表仓储接收，哪个动作触发 ERP 入库？

### P1：决定系统能否试运行

1. 工位账号是人、班组还是共用终端？需要追到操作人、设备还是两者都要？换班和代岗如何处理？
2. 条码规则、扫描枪接口、标签模板、打印机协议和补打权限是什么？
3. 断网是否允许生产？本地缓存、补传、重复事件和时钟偏差如何处理？
4. ERP/WMS 接口由谁提供主数据、工单、耗料和完工确认？幂等键、失败重试、对账和人工补偿规则是什么？
5. 路线/BOM/测试规范变更时，已投产 SN 继续旧版本还是切换？谁授权例外？
6. 追溯查询要服务哪些场景：客诉、召回、维修、保修、质量分析、客户审计或监管？每种场景的最小字段与保存年限是什么？

### P2：规模化前验证

1. 每班产量、并发工位数、单 SN 事件量、测试原始文件大小和追溯查询响应目标是多少？
2. 多型号共线、替代料、拆机返修、序列件复用和工单尾数如何处理？
3. 是否需要与 PLM、QMS、设备平台、条码平台、证书/密钥系统对接？
4. 谁拥有追溯数据，保存多久，哪些字段涉及个人信息或安全凭据，如何脱敏和授权？

## 六、研究后可以确认与仍不可确认的边界

**可以确认：**

- 以成品 SN 为主线组织事件、输入部件、固件、测试、质量和完工事实，符合 IEC 制造运营信息模型与 GS1 事件可视性模型的方向。[IEC 62264-2:2026](https://webstore.iec.ch/en/publication/75127)（访问：2026-07-29）、[GS1 EPCIS 2.0.1 规范工件](https://ref.gs1.org/standards/epcis/artefacts)（访问：2026-07-29）。
- MES 不应吞并 ERP 业务计划/总账，也不应把轻量线边台账包装成完整 WMS。[IEC 62264-1](https://webstore.iec.ch/en/publication/6675)（访问：2026-07-29）、[Dynamics 365 Warehouse management overview](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview)（访问：2026-07-29）。
- 固件版本/包、产品 SN、MAC、型号和硬件版本是真实工业网络设备可获得的产品事实，不是为演示凭空制造的字段。[Moxa EDR-G9010 产品页](https://www.moxa.com/en/products/industrial-network-infrastructure/secure-routers/secure-routers/edr-g9010-series)（访问：2026-07-29）、[Moxa Warranty Check](https://www.moxa.com/en/support/repair-and-warranty/warranty-check)（访问：2026-07-29）。

**仍不可确认：**

- `FLASH` 与 `ASSEMBLY` 的先后、是否需要预烧录/二次配置、烧录失败返到哪里。
- `FQC` 应是一站还是功能测试、老化、外观/标签、安规/射频等多站；哪些是全检。
- `PACK` 是否属于 MES 路线，是否需要箱/托盘聚合关系。
- `PCB-MAIN`、`PSU-MOD` 是否真是需要 SN 追踪的关键件，以及实际 BOM 中还有哪些高风险件。
- 首站才创建 SN、非关键件在领料时即记耗、无独立质检角色、五站线性路线等当前规则，均仍是试点假设而不是公开行业共识。

因此，下一轮实现应先补“事件与事实的可追溯深度”，再改站点数量；任何路线改名或拆站都应以可配置的路线版本完成，并在获得从业者/现场证据前继续标记为业务假设。
