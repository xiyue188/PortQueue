# PortQueue 文件职责索引

本文档用于帮助课程设计成员在 VS Code 中快速定位文件。文件职责按当前阶段划分；新增文件必须归入已有层次，并在这里补充说明。

## 根目录

| 路径 | 职责 |
|---|---|
| `AGENTS.md` | 项目协作规则、技术基线、数据库约束和开发流程；它是后续实现的工作约束。 |
| `README.md` | 项目概览、目录导航、当前状态和最短启动路径。 |
| `PortQueue.sln` | .NET 解决方案入口，统一组织 API、应用层、基础设施和测试项目。 |
| `Directory.Build.props` | 统一 .NET 编译设置，包括 nullable、隐式 using 和警告处理策略。 |
| `global.json` | 锁定项目使用的 .NET SDK 主版本，避免不同机器产生不可比的构建结果。 |
| `.gitignore` | 排除构建产物、依赖缓存、本地配置、数据库文件和日志。 |
| `.gitattributes` | 统一文本文件换行和 SQL、Markdown 等文件的 Git 属性。 |
| `.editorconfig` | 统一缩进、编码、换行和尾随空格规则。 |
| `package.json` | pnpm 工作区级前端命令入口。 |
| `pnpm-workspace.yaml` | 声明前端工作区和构建依赖白名单。 |

## 数据库

`database/` 是 SQL Server 物理设计和部署脚本的唯一来源。脚本按编号执行，编号表示依赖顺序，不表示业务重要性。

| 路径 | 职责 |
|---|---|
| `00_create_database.sql` | 创建或准备课程设计使用的数据库。 |
| `01_schema.sql` | 创建 21 个基本关系、主外键、候选键、值域约束和索引。 |
| `02_views.sql` | 创建 3 个面向队列、计划和事件查询的业务视图。 |
| `03_triggers.sql` | 承担字典类型与标识、事件分类、重复事件和审计只追加等必须靠数据库保护的约束。 |
| `04_procedures.sql` | 承担预约审核、队列调序、计划版本、计划确认、锚地事件、实际进港和靠离泊等事务写操作。 |
| `05_seed_reference.sql` | 写入可重复使用的基础字典、港口、锚地、泊位等参考数据；保留停用状态。 |
| `06_smoke_tests.sql` | 检查 21 表、3 视图、7 过程、8 表级触发器、约束可信状态和业务表索引。 |
| `07_business_smoke_test.sql` | 在事务中贯通一条核心业务链路，结束时回滚测试数据。 |
| `08_constraint_smoke_tests.sql` | 验证错误字典类型等关键非法输入会被拒绝。 |
| `09_permissions.sql` | 创建应用最小权限角色和无登录验证用户；不创建密码或本机登录。 |
| `10_seed_demo.sql` | 按需装载 PQ-DEMO 演示业务事实，重复执行不重置状态。 |
| `11_statistics.sql` | 提供等待、偏差、准点率和泊位观测利用率的口径查询。 |
| `12_refresh_sample_labels.sql` | 整理旧五条样例的展示名称、业务编号和旧审核文案，保留主外键及业务事实，追加审计快照。 |
| `13_seed_port_scenarios.sql` | 增加 48 条可重跑的合成场景；覆盖六类状态、排队、确认计划与已完成作业。 |
| `queries/appointments.sql` | API 执行的参数化预约 SELECT；构建时嵌入后端程序集，不部署为新表、视图或过程。 |
| `queries/overview.sql` | 单条 SELECT 读取独立总览指标及最近五个实际事件，不跟随预约筛选，不新增持久化对象。 |
| `tests/database/run_stage1.py` | 创建随机独立测试库，执行业务、权限、回滚、并发和演示数据验收，默认结束后清理。 |
| `tests/database/check-sql.ps1` | 调用 SQL Server ScriptDom 做静态语法检查，不连接数据库。 |
| `bootstrap-localdb.ps1` | 本机环境引导：检查/创建/启动实例，数据库不存在时恢复，已有数据库补齐参考数据并验证；不自动更新过程或加载演示业务。 |
| `README.md` | 说明 SQL Server 环境、执行顺序、验证方式和常见故障。 |

## 后端

后端按 API、应用契约、基础设施三层组织。当前完成健康接口、预约只读查询和独立总览；后续写操作仍按“小阶段、可验收”节奏增加。

| 路径 | 职责 |
|---|---|
| `src/PortQueue.Api/Program.cs` | Web 应用启动组合根：注册控制器、异常处理、健康检查和基础设施。这里不写业务 SQL。 |
| `src/PortQueue.Api/Controllers/HealthController.cs` | 提供不依赖业务数据的服务存活检查接口。 |
| `src/PortQueue.Api/Controllers/AppointmentsController.cs` | 提供 `GET /api/appointments`，连接 HTTP 请求与查询接口。 |
| `src/PortQueue.Api/Appointments/AppointmentQueryRequest.cs` | 校验页码、页容量、状态和关键词，归一化输入。 |
| `src/PortQueue.Api/Errors/ApiExceptionHandler.cs` | 输出稳定的 503/500 错误，不回传 SQL 或堆栈。 |
| `src/PortQueue.Application/Appointments/` | 预约查询条件、结果类型与查询接口。 |
| `src/PortQueue.Infrastructure/Appointments/SqlAppointmentQueries.cs` | 绑定参数、执行唯一 SQL 源并映射预约分页结果。 |
| `src/PortQueue.Api/Controllers/OverviewController.cs` | 提供 `GET /api/overview`，返回总览摘要或统一错误响应。 |
| `src/PortQueue.Application/Overview/PortOverview.cs` | 总览指标、近期事件的返回契约及查询接口。 |
| `src/PortQueue.Infrastructure/Overview/SqlOverviewQueries.cs` | 执行嵌入的总览 SQL，映射计数与事件；管理超时与取消。 |
| `src/PortQueue.Api/Appointments.http` | 预约成功、筛选、分页和失败请求样例。 |
| `src/PortQueue.Api/appsettings*.json` | 非敏感配置模板；本地连接信息只能放在未提交的本地配置或用户机密中。 |
| `src/PortQueue.Api/Properties/launchSettings.json` | VS Code/`dotnet run` 的本地启动配置。 |
| `src/PortQueue.Application/Abstractions/` | 应用服务和查询契约，保持业务语义与 ASP.NET、Dapper 解耦。 |
| `src/PortQueue.Infrastructure/DependencyInjection.cs` | 注册 Dapper、SQL Server 和其他基础设施实现。 |
| `src/PortQueue.Infrastructure/Database/DatabaseOptions.cs` | 定义数据库连接配置节。 |
| `src/PortQueue.Infrastructure/Database/ISqlConnectionFactory.cs` | 为应用层提供可测试的连接创建抽象。 |
| `src/PortQueue.Infrastructure/Database/SqlConnectionFactory.cs` | 按配置创建 `Microsoft.Data.SqlClient.SqlConnection`；不负责建表和事务业务。 |
| `tests/PortQueue.Api.Tests/` | 接口和应用级自动化测试；每个新业务阶段至少补成功和失败场景。 |
| `tests/PortQueue.Api.Tests/Appointments/OverviewScenarioTests.cs` | 在独立数据库验证场景装载、重复执行无额外写入、审核完整性和最低读取权限下的总览结果。 |

## 前端

| 路径 | 职责 |
|---|---|
| `web/port-queue-web/src/main.ts` | Vue 应用入口，挂载根组件和全局样式。 |
| `web/port-queue-web/src/App.vue` | 工作区入口，挂载共用真实查询的预约工作台。 |
| `web/port-queue-web/src/views/AppointmentListView.vue` | 管理总览/预约布局切换、筛选草稿、选中记录、分页和加载/错误/空状态。 |
| `web/port-queue-web/src/api/appointments.ts` | 预约 API 客户端和响应结构校验；不提供 Mock 兜底。 |
| `web/port-queue-web/src/composables/useAppointments.ts` | 保存已提交查询条件，管理取消、超时、分页和迟到响应。 |
| `web/port-queue-web/src/composables/useOverview.ts` | 独立请求和校验总览数据；请求失败清除旧数值，避免把过期数据当成当前概况。 |
| `web/port-queue-web/src/types/appointment.ts` | 前端预约状态、查询和响应类型。 |
| `web/port-queue-web/src/components/AppointmentOverview.vue` | 当前预约的船舶焦点、申请时间窗与审核摘要，使用父组件传入的同一记录。 |
| `web/port-queue-web/src/components/AppointmentTable.vue` | 两种布局共用的预约表格，支持鼠标及键盘选择、内部滚动。 |
| `web/port-queue-web/src/components/AppointmentDetail.vue` | 选中预约的标识、申请时间和审核事实，不填造尚未接入的业务信息。 |
| `web/port-queue-web/src/components/PortIcon.vue` | 集中维护统一描边 SVG 图标。 |
| `web/port-queue-web/src/utils/appointmentPresentation.ts` | 统一北京时间格式、状态中文名称与状态颜色。 |
| `web/port-queue-web/src/assets/` | 船舶示意素材及来源记录；图片本身嵌入来源元数据。 |
| `web/port-queue-web/src/styles.css` | 冰蓝玻璃设计变量、侧栏、工具区、主次分栏、表格和响应式规则。 |
| `web/port-queue-web/playwright.config.ts`、`tests/appointments.spec.ts` | 本机 Edge 浏览器验收，验证真实查询和异常恢复并保存截图。 |
| `web/port-queue-web/DESIGN.md`、`.impeccable/design.json` | 最终视觉规范与对应组件预览数据；供后续页面保持统一，不参与生产页面加载。 |
| `web/port-queue-web/vite.config.ts` | Vite 插件、端口和 `/api` 到 `http://localhost:5180` 的本地代理。 |
| `web/port-queue-web/package.json` | 前端依赖和类型检查、构建、开发命令。 |
| `pnpm-lock.yaml` | 在工作区根目录统一锁定前端依赖版本；不在 `web/port-queue-web` 再维护第二份 lockfile。 |

## 文档与配置

| 路径 | 职责 |
|---|---|
| `docs/implementation-roadmap.md` | 阶段目标、业务路径和实现顺序。 |
| `docs/acceptance-plan.md` | 中期与最终验收案例、证据和完成判定。 |
| `docs/stages/stage-2-delivery-guide.md` | 阶段 2 前后端与数据库变化、输入输出、联调过程，以及教师要求的结构/数据展示说明。 |
| `docs/stages/stage-2-ui-reference-redesign.md` | 用户参考图改版的范围、视觉约束和 U01—U08 验收条件。 |
| `docs/stages/stage-2-overview-and-scenarios.md` | 总览定位、场景数据、主图、简洁文案与真实账号状态的 R01—R08 验收条件。 |
| `docs/business-flow.md` | 业务主线、页面职责、申请/计划/实际时间、状态、数据性质及下一批权限设计。 |
| `docs/design/reference/` | 用户提供的五张原始参考图和来源说明，作为后续界面的视觉依据。 |
| `docs/physical-design-and-operations.md` | 物理类型、约束分工和业务操作契约。 |
| `docs/decision-log.md` | 记录影响模型和技术路线的关键决策。 |
| `docs/reviews/` | 每个里程碑的构建、测试、SQL 检查和深度 review 结论。 |
| `config/` | 可提交的本地配置示例，不放密码、令牌和真实连接凭据。 |
| `.vscode/` | VS Code 的任务、推荐扩展和项目级编辑设置；MSSQL 的编辑器错误诊断已关闭，避免扩展对 SQL Server `THROW` 等合法语法产生误报，真实校验由 SQL Server 和 `tests/database/check-sql.ps1` 负责。 |

## 修改顺序

涉及数据库业务的改动按以下顺序进行：先更新 `database/` 和设计文档，再更新应用层契约与基础设施，最后接入控制器和前端。每一阶段只实现一个可演示用例，并把验收证据写入 `docs/reviews/`。

## 注释约定

- C# 文件用 XML 注释说明公开类型和接口的业务职责；复杂事务或状态判断旁边说明原因。
- Vue、TypeScript 和 CSS 文件在模块顶部说明模块边界；模板只保留对业务有帮助的注释。
- SQL 文件在文件头说明模块、执行顺序和约束分工；重要过程按“输入—前置条件—事务动作—结果”分段。
- JSON、项目文件和 lockfile 不添加会破坏格式的伪注释，配置含义统一写在 README、runbook 或文件名附近的文档中。
- 注释解释“为什么”和“边界”，不重复描述显而易见的语法；注释必须随代码变更同步更新。
