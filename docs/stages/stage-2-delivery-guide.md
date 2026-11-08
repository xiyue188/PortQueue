# 阶段 2 交付说明：真实查询与港口总览

更新：2026-11-08。本文件描述当前可运行成果；历史测试与交付记录保留在 `docs/reviews/`。本轮验收计划见 `stage-2-overview-and-scenarios.md`，业务解释见 `../business-flow.md`。

## 1. 本轮的目标与变化

预约查询已经贯通；本轮解决总览与预约页定位重叠、冗余文案、低清主图和样例过少的问题。首页从预约查询的另一种布局，调整为读取跨业务概况的总览。保留冰蓝商务风格，允许摘要波浪边等少量航运细节。

| 层次 | 本轮变化 | 输出与边界 |
|---|---|---|
| 数据库 | 新增总览 SELECT；新增旧样例名称整理和 48 条场景导入；旧种子识别整理后的编号 | 53 条持久化预约、49 个申请到港时刻；没有新增表、视图、过程、触发器或索引 |
| 后端 | 新增总览契约、Dapper 查询实现和控制器 | GET /api/overview；原预约接口保持契约 |
| 前端 | 总览独立指标与近期事件；预约页筛选/分页/详情；新主图、波浪摘要、简洁文案 | API 失败明确提示；未登录状态真实；后续页面尚未开放 |
| 验收 | 增加真实数据库总览/场景测试、总览 503 契约，浏览器用例更新为七项 | 构建、SQL 静态检查、独立数据库测试和真实 Edge 交互 |

数据是真实存入 SQL Server 的合成场景，不是实时港口或 AIS 信息，也不是前端硬编码。旧五条的申请时间和历史事件保留；名称整理追加审计。新 48 条涵盖六种状态，各状态 8 条；合计状态数量为草稿 8、待审核 9、已批准 10、已驳回 8、已撤销 8、已完成 10。

## 2. 后端与 SQL 的输入、处理和输出

| 入口 | 输入 | 处理 | 输出 |
|---|---|---|---|
| GET /api/appointments | page、pageSize、status、keyword | 校验参数；关联预约、到访、船舶和审核人；参数化筛选；稳定排序和分页 | items、page、pageSize、totalCount |
| GET /api/overview | 无筛选参数 | 单条 SELECT 读取待审核预约、实际锚地在场、当前已确认计划及最近五个事件 | pendingAppointments、anchorageVessels、confirmedPlans、recentEvents |

`database/queries/` 是 API SQL 的唯一源，构建时嵌入 Infrastructure 程序集。Dapper 只绑定参数和映射结果，不创建表。预约查询按申请到港时间、预约主键倒序；总览事件按实际事件时间、事件主键倒序。两种时间不能混用。

总览使用已有 RCSI 的单语句一致读取；锚地在场按有入锚且无离锚事件判断，不能只数有效队列。确认计划指当前待靠泊的 CONFIRMED 计划，不含 EXECUTING 或 COMPLETED。三个指标不能相加解释为全港船舶总量。

主键序列化为字符串，避免 JavaScript 精度损失；API 时间带 UTC 偏移，页面统一转 Asia/Shanghai。200 空数组是查询成功无记录，400 是参数错误，503 是数据库暂不可用，500 是其他内部故障；错误不泄露 SQL 和堆栈。

## 3. 前端操作与状态

| 用户操作 | 处理与输出 |
|---|---|
| 打开总览 | 同时读取总览和预约列表；显示三个全局指标、最近五个事件和五条预约摘要 |
| 提交顶部关键词 | 进入进港预约并查询；输入草稿未提交前不影响当前结果 |
| 状态筛选、页容量、翻页 | 按已提交条件重新查询；不同状态/页码显示真实记录 |
| 选择船舶 | 摘要和详情使用同一选中记录；支持鼠标和键盘 |
| 从预约返回总览 | 清除预约筛选、返回第一页；全局指标始终独立 |
| 刷新 | 重读预约和总览，不显示虚构更新时间或进度 |
| 空结果 | 显示明确空状态和清除筛选入口 |
| API 故障 | 显示错误和重试；不回退 Mock，也不保留旧指标假装成功 |

预约请求和总览请求分别管理取消、超时和请求序号，避免旧响应覆盖新结果。`AppointmentListView.vue` 组织页面；`useAppointments.ts` 和 `useOverview.ts` 分别管理查询；`AppointmentOverview.vue`、`AppointmentTable.vue`、`AppointmentDetail.vue` 负责焦点、列表与详情。完整职责索引见 `../file-guide.md`。

“实际执行”菜单改为“进出港记录”；当前事件仍是入锚、离锚、进港、靠泊、离泊五类，没有独立出港事件，离泊不冒充港界离港。账号区显示“未登录”；四类业务角色的真正鉴权在后续批次实施，不提供前端假切换。

## 4. 数据装载与可重复性

`10_seed_demo.sql` 建立早期五条流程样例；`12_refresh_sample_labels.sql` 保留 ID 和历史事实整理名称、编号及旧文案；`13_seed_port_scenarios.sql` 增加 48 条。按 `10 → 12 → 13` 装载，使用 sqlcmd 的 `-b -f 65001`，失败立即停止。本机已经完成，不需要每次启动执行。

新场景在单个事务内装载，重复编号跳过；历史审核和确认时间作为归档快照导入，实际事件通过既有过程登记。随机测试库验证重复执行后预约和审计数量不增加，不会清空开发库。IMO 格式及校验位正确只代表测试格式，不等于真实注册资料。

## 5. 联调例子

```mermaid
sequenceDiagram
    participant U as Vue 页面
    participant V as Vite 5173
    participant A as ASP.NET 5180
    participant D as SQL Server
    U->>V: GET /api/appointments?keyword=PQ202611-1044
    V->>A: 同路径代理
    A->>D: 参数化 SELECT
    D-->>A: 远川明珠 8 的预约与总数 1
    A-->>U: JSON，经 Vite 返回
    U->>V: GET /api/overview
    V->>A: 同路径代理
    A->>D: 总览 SELECT
    D-->>U: 经 API 返回 9、10、4 及近期事件
```

预约 `PQ202611-1044` 的申请到港为北京时间 2026/11/09 06:55，状态待审核。筛选仅影响预约结果，不改变总览 9/10/4。浏览器不直连数据库，连接字符串只由后端使用。今后同源部署还需配置静态站点与 API 转发。

## 6. 老师要求的表结构视图、数据视图对应什么

教师通知原文：“主要讲解已经完成的工作，可以把数据库表结构视图和数据视图、已经完成的系统界面等截图展示。”结合上下文，这里的“视图”主要指可供截图展示的结构界面和数据界面，并非限定为 SQL `CREATE VIEW` 对象。

| 展示类别 | 需要证明什么 | 本项目对应内容 |
|---|---|---|
| 表结构展示 | 设计已落实为物理表：字段、类型、主外键、是否允许空、唯一/检查约束 | SQL Server 中的 arrival_appointment、ship、berth_plan、berth_plan_revision 等；对象树、表设计界面、元数据查询结果 |
| 数据展示 | 表内有可查询、可解释的记录，关联数据能对上 | 预约编号、状态、船舶和到访数据；SELECT 结果网格 |
| 数据库业务 VIEW | 多表如何被组织为有业务意义的查询 | v_port_event、v_anchorage_queue、v_current_berth_plan；定义及查询结果 |
| 已完成系统界面 | 用户能通过应用读取数据库信息 | 真实进港预约页面、筛选结果、分页；静态总览不能替代 |

ER 图是概念设计证据，不能完全替代数据库已建成的结构证据；基本表的 SELECT 结果也可作为数据展示，不要求为了截图新增 VIEW。

### 在 VS Code 中准备截图

连接 `(localdb)\PortQueueDev`、选择 `PortQueue`。表结构可展开 Tables 下表的 Columns、Keys、Indexes，或使用扩展提供的设计/脚本查看入口；扩展版本不同菜单名称可能不同。通用方式是在 SQL 编辑器执行下列只读查询，截图结果网格，不需要重跑建表脚本。

```sql
USE [PortQueue];
-- 结构与键/约束：返回多个结果集，可选列定义和约束页截图。
EXEC sys.sp_help N'dbo.arrival_appointment';
EXEC sys.sp_helpconstraint N'dbo.arrival_appointment';

-- 数据：只展示与预约演示有关的列。
SELECT appointment_id, appointment_no, port_call_id,
       requested_arrival_at, requested_departure_at,
       appointment_status_code, reviewed_by, reviewed_at
FROM dbo.arrival_appointment
ORDER BY requested_arrival_at DESC, appointment_id DESC;

-- 结构对应的关联数据：同一预约能查到船名与审核人。
SELECT aa.appointment_no, s.ship_name, aa.appointment_status_code,
       reviewer.display_name AS reviewer_name
FROM dbo.arrival_appointment AS aa
JOIN dbo.port_call AS pc ON pc.port_call_id = aa.port_call_id
JOIN dbo.ship AS s ON s.ship_id = pc.ship_id
LEFT JOIN dbo.user_account AS reviewer ON reviewer.user_id = aa.reviewed_by
ORDER BY aa.requested_arrival_at DESC, aa.appointment_id DESC;

-- 一个已建成的业务 VIEW 查询，可作为额外的数据库成果。
SELECT appointment_no, ship_name, anchorage_name, queue_position,
       queue_status_code, entered_anchorage_at, left_anchorage_at
FROM dbo.v_anchorage_queue
ORDER BY appointment_no;
```

3 个现有业务视图的职责：`v_port_event` 将公共事件和三种分类关联统一为预约事件时间线；`v_anchorage_queue` 组合队列、船舶、锚地及真实进出锚事件时间；`v_current_berth_plan` 选择各计划当前版本并关联泊位、船舶和确认信息。当前版本视图不等于“只显示待执行计划”，已完成计划也可以有当前版本。

中期三分钟内建议准备：一张核心表结构及主外键截图、一张预约/队列数据网格截图、一张真实预约页面截图。完整 21 表清单、ER 图、3 VIEW 定义和规则测试放备用材料，不要求正文逐表朗读。

## 7. 验收证据与完成边界

本轮 .NET Release 构建零警告、零错误，38 项后端测试通过；16 个 SQL 文件静态检查通过；前端类型检查/构建通过；七项 Edge 浏览器测试通过。浏览器正向路径使用真实 SQL/API，故障和迟到响应只在浏览器拦截，不修改开发库。桌面、窄屏、空结果与故障截图位于仓库根 `.impeccable/review/`，不提交 Git。

数据库阶段 1 的 59 项规则验收属于此前结果，本轮没有改过程或约束，不将该历史结果重复算作新测试。当前对象基线保持 21 表、3 视图、7 过程、8 表级触发器、45 个业务表索引。

尚未实现：真实账号认证和业务角色授权，预约登记/提交/撤销/审核的写 API 与页面，队列、计划和事件完整页面，统计报表页。数据库对象已经具备基础能力，不能等同于整个应用业务已经完成。

## 8. 后续交付要求

每批说明目标、前后端/数据库各自变化、关键文件职责、输入→处理→输出、实测结果、未完成项和小步提交。下一批先完成认证与权限设计/验收，再实现受控登录和预约审核这一条写链路；不同时展开所有菜单。
