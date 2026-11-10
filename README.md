# PortQueue

港口船舶进港预约与锚地排队协同管理系统。

本项目是数据库原理及航运应用课程设计的实现仓库。它以已经完成的需求分析、数据字典、概念结构设计和逻辑模型为依据，使用 SQL Server 保存业务事实，以 ASP.NET Core Web API 提供事务接口，以 Vue 3 页面展示调度过程和统计结果。

## 当前基线

- 数据库：SQL Server，21 个基本关系、3 个业务视图。
- 后端：ASP.NET Core Web API，`.NET 8`。
- 数据访问：Dapper 和显式 SQL；数据库脚本优先于代码生成。
- 前端：Vue 3、TypeScript、Vite、Element Plus、ECharts。
- 开发工具：VS Code。

## 目录

```text
database/                  SQL Server 建库、表、视图、过程、触发器、种子数据
docs/                      实施规划、物理设计、业务操作、验收计划、评审记录
src/PortQueue.Api/         Web API 入口、控制器、中间件和配置
src/PortQueue.Application/ 应用服务、命令/查询契约和业务规则接口
src/PortQueue.Infrastructure/ Dapper 仓储、SQL 连接和数据库适配
tests/                     后端单元测试和接口测试
web/port-queue-web/        Vue 前端
config/                    示例配置和本地开发说明
```

这是一个数据库优先的单仓库组织：`database/` 是独立的 SQL Server 部署与验收产物，放在根目录便于教师、数据库工具和脚本直接使用；`src/` 只放后端，`web/` 只放前端，`tests/` 与两者并列，`docs/` 保存设计和验收证据。没有把所有内容强行放到 `src/`，是为了让数据库脚本保持独立、可恢复、可审查。

## 开始前

前端需要 Node.js 22.13+ 和 pnpm 11.25.0。缺少包管理器时执行一次 `npm.cmd install --global pnpm@11.25.0`；本机已于 2026-11-07 配好。项目依赖在仓库根目录执行 `pnpm.cmd install --frozen-lockfile`，不要对已有 pnpm 依赖目录再执行 `npm install`。日常 `run dev` 不需要先重新安装依赖。

1. 安装 SQL Server Developer 或 Express，并确认可以从 VS Code 的 SQL Server 扩展或 `sqlcmd` 连接。
2. 设置项目级 `DOTNET_CLI_HOME` 和 `NUGET_PACKAGES`，再执行 `dotnet restore PortQueue.sln`。
3. 按 `database/README.md` 的顺序执行数据库脚本。
4. 本机 Development 配置已指向 `(localdb)\PortQueueDev / PortQueue`；其他实例通过 `ConnectionStrings__PortQueue` 环境变量或用户机密配置，连接密码不提交到 Git。单独复制 `appsettings.Local.json` 不会被当前程序自动加载。
5. 先阅读 `docs/physical-design-and-operations.md` 和 `docs/acceptance-plan.md`，再实现功能。

如果使用本机 LocalDB，首次准备或遇到实例连接错误时，先执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\database\bootstrap-localdb.ps1
```

它会区分 LocalDB 实例和 `PortQueue` 数据库，已有数据库不会重复恢复。

## 当前运行方式

当前为 **2026-11-10 中期冻结版**，服务上海洋山深水港（项目内部代码 SH-YS）。正式开放“总览”和“进港预约”：真实账号登录、查询、登记、草稿编辑、提交与批准/驳回已形成 SQL Server → API → Vue 的完整链路。调度员依据船方资料代为登记，审核员审批。

锚地排队、泊位计划、进出港记录和查询统计均为**待接入**。总览保留四个主图入口，未开放模块只提示尚未接入，不用通用列表代替专用业务设计。已有数据库对象、种子事实及总览概要可展示，但不等于这些模块已经开发完成。开发库保留原场景及用户后续登记，数量以查询为准。

数据库脚本通过 SQL Server 扩展或 `sqlcmd` 执行一次即可，不需要长期占用一个终端。使用 LocalDB 时，先确认 `(localdb)\PortQueueDev` 实例处于 Running；运行时建议保留两个长期终端，若正在恢复数据库则临时使用第三个终端：

```powershell
# 长期终端一：启动 ASP.NET Core API
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet run --project src/PortQueue.Api --configuration Release --launch-profile http

# 长期终端二：启动 Vue 开发服务器
pnpm.cmd --dir web/port-queue-web run dev
```

启动后访问 `http://127.0.0.1:5173` 查看总览，点击“进港预约”查看列表和详情；访问 `http://localhost:5180/api/health` 查看后端健康接口。所有业务结果从本机 SQL Server 读取；数据库不可用时显示可恢复的错误状态，不会回退到 Mock 数据。首次运行后端前，应先按 `database/README.md` 恢复数据库并按需装载场景。

只复制命令，不复制终端的 `PS ...>` 或 `>>` 提示符。也可以在 VS Code 中执行 `Tasks: Run Task` → `frontend: dev`。如提示 5173 已占用，先访问已有页面或停止原前端终端，不要重复启动。

完整的文件职责、端口约定和常用验证命令见 [`docs/file-guide.md`](docs/file-guide.md) 与 [`docs/runbook.md`](docs/runbook.md)。

分阶段的开发范围、验收标准和阶段门见 [`docs/stage-plans.md`](docs/stage-plans.md)。

阶段 2 的前后端改动、输入输出、联调过程以及教师要求的数据库展示对应关系见 [`docs/stages/stage-2-delivery-guide.md`](docs/stages/stage-2-delivery-guide.md)。

业务主线、申请/计划/实际时间、六类预约状态、总览指标口径与四类角色设计见 [`docs/business-flow.md`](docs/business-flow.md)。早期查询交互设计见 [`docs/stages/stage-2-interaction-polish.md`](docs/stages/stage-2-interaction-polish.md)：真实候选搜索、页面往返保留筛选与选择、局部加载及统一桌面布局。

第三阶段已推进到 **3C 预约登记与提交**。当前可在登录窗口选择管理员、调度员、审核员或记录员，初始密码统一为 `123456`，选择时自动填入；四个专用账号已在本机开发库建立，仍通过真实身份认证。仅在 Development 且显式启用时开放。操作步骤、权限和本轮输入输出见 [3B 交付说明](docs/stages/stage-3b-delivery-guide.md)，设计见 [3B 计划](docs/stages/stage-3b-review-and-usability.md)，结果见 [3B review](docs/reviews/2026-11-09-stage3b-review.md)。本轮登记、提交和审核已形成真实闭环，操作与验收见 [3C 交付说明](docs/stages/stage-3c-delivery-guide.md)。中期范围已收口，最新边界和演示顺序以 [中期交付说明](docs/stages/midterm-delivery-guide.md) 为准；之后先演练，答辩后再单独设计锚地业务。

## 交付要求

最终交付包括：可恢复的 SQL 脚本、后端源代码、前端源代码、样例数据、测试结果、课程设计报告和答辩演示材料。完整的环境恢复步骤以 `docs/acceptance-plan.md` 为准。
