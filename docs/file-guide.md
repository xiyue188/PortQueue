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
| `03_triggers.sql` | 承担字典类型、事件分类、重复事件等必须靠数据库保护的约束。 |
| `04_procedures.sql` | 承担预约审核、锚地事件、实际进港、计划确认和靠离泊等事务写操作。 |
| `05_seed_reference.sql` | 写入可重复使用的基础字典、港口、锚地、泊位等参考数据。 |
| `06_smoke_tests.sql` | 检查结构对象是否存在和基本对象数量是否符合设计。 |
| `07_business_smoke_test.sql` | 在事务中贯通一条核心业务链路，结束时回滚测试数据。 |
| `08_constraint_smoke_tests.sql` | 验证错误字典类型等关键非法输入会被拒绝。 |
| `bootstrap-localdb.ps1` | Windows 本机引导脚本：检查/创建/启动 LocalDB 实例，数据库不存在时恢复脚本，已有数据库只读验证。 |
| `README.md` | 说明 SQL Server 环境、执行顺序、验证方式和常见故障。 |

## 后端

后端按 API、应用契约、基础设施三层组织。当前只实现健康接口和连接工厂，业务接口遵循“小阶段、可验收”节奏逐步增加。

| 路径 | 职责 |
|---|---|
| `src/PortQueue.Api/Program.cs` | Web 应用启动组合根：注册控制器、异常处理、健康检查和基础设施。这里不写业务 SQL。 |
| `src/PortQueue.Api/Controllers/HealthController.cs` | 提供不依赖业务数据的服务存活检查接口。 |
| `src/PortQueue.Api/appsettings*.json` | 非敏感配置模板；本地连接信息只能放在未提交的本地配置或用户机密中。 |
| `src/PortQueue.Api/Properties/launchSettings.json` | VS Code/`dotnet run` 的本地启动配置。 |
| `src/PortQueue.Application/Abstractions/` | 应用服务和查询契约，保持业务语义与 ASP.NET、Dapper 解耦。 |
| `src/PortQueue.Infrastructure/DependencyInjection.cs` | 注册 Dapper、SQL Server 和其他基础设施实现。 |
| `src/PortQueue.Infrastructure/Database/DatabaseOptions.cs` | 定义数据库连接配置节。 |
| `src/PortQueue.Infrastructure/Database/ISqlConnectionFactory.cs` | 为应用层提供可测试的连接创建抽象。 |
| `src/PortQueue.Infrastructure/Database/SqlConnectionFactory.cs` | 按配置创建 `Microsoft.Data.SqlClient.SqlConnection`；不负责建表和事务业务。 |
| `tests/PortQueue.Api.Tests/` | 接口和应用级自动化测试；每个新业务阶段至少补成功和失败场景。 |

## 前端

| 路径 | 职责 |
|---|---|
| `web/port-queue-web/src/main.ts` | Vue 应用入口，挂载根组件和全局样式。 |
| `web/port-queue-web/src/App.vue` | 当前调度总览原型。演示数据集中在组件内，并明确标注接口尚未联调。真实 API 接入后应迁移到 `api/` 和 `composables/`，避免继续扩大单文件。 |
| `web/port-queue-web/src/styles.css` | 当前原型的视觉样式和响应式布局。 |
| `web/port-queue-web/vite.config.ts` | Vite 插件、路径别名和开发服务器端口。 |
| `web/port-queue-web/package.json` | 前端依赖和类型检查、构建、开发命令。 |
| `pnpm-lock.yaml` | 在工作区根目录统一锁定前端依赖版本；不在 `web/port-queue-web` 再维护第二份 lockfile。 |

## 文档与配置

| 路径 | 职责 |
|---|---|
| `docs/implementation-roadmap.md` | 阶段目标、业务路径和实现顺序。 |
| `docs/acceptance-plan.md` | 中期与最终验收案例、证据和完成判定。 |
| `docs/physical-design-and-operations.md` | 物理类型、约束分工和业务操作契约。 |
| `docs/decision-log.md` | 记录影响模型和技术路线的关键决策。 |
| `docs/reviews/` | 每个里程碑的构建、测试、SQL 检查和深度 review 结论。 |
| `config/` | 可提交的本地配置示例，不放密码、令牌和真实连接凭据。 |
| `.vscode/` | VS Code 的任务、推荐扩展和项目级编辑设置。 |

## 修改顺序

涉及数据库业务的改动按以下顺序进行：先更新 `database/` 和设计文档，再更新应用层契约与基础设施，最后接入控制器和前端。每一阶段只实现一个可演示用例，并把验收证据写入 `docs/reviews/`。

## 注释约定

- C# 文件用 XML 注释说明公开类型和接口的业务职责；复杂事务或状态判断旁边说明原因。
- Vue、TypeScript 和 CSS 文件在模块顶部说明模块边界；模板只保留对业务有帮助的注释。
- SQL 文件在文件头说明模块、执行顺序和约束分工；重要过程按“输入—前置条件—事务动作—结果”分段。
- JSON、项目文件和 lockfile 不添加会破坏格式的伪注释，配置含义统一写在 README、runbook 或文件名附近的文档中。
- 注释解释“为什么”和“边界”，不重复描述显而易见的语法；注释必须随代码变更同步更新。
