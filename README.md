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

1. 安装 SQL Server Developer 或 Express，并确认可以从 VS Code 的 SQL Server 扩展或 `sqlcmd` 连接。
2. 设置项目级 `DOTNET_CLI_HOME` 和 `NUGET_PACKAGES`，再执行 `dotnet restore PortQueue.sln`。
3. 按 `database/README.md` 的顺序执行数据库脚本。
4. 复制示例配置到本地配置文件，填入本机连接字符串；连接密码不提交到 Git。
5. 先阅读 `docs/physical-design-and-operations.md` 和 `docs/acceptance-plan.md`，再实现功能。

如果使用本机 LocalDB，首次准备或遇到实例连接错误时，先执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\database\bootstrap-localdb.ps1
```

它会区分 LocalDB 实例和 `PortQueue` 数据库，已有数据库不会重复恢复。

## 当前运行方式

当前仓库已经具备可验证的 SQL Server 数据库基线、后端健康接口和前端调度总览原型。前端总览中的队列和指标仍是演示数据，并标注为“接口待联调”；预约业务接口将在下一阶段按验收计划逐步接入。

数据库脚本通过 SQL Server 扩展或 `sqlcmd` 执行一次即可，不需要长期占用一个终端。使用 LocalDB 时，先确认 `(localdb)\PortQueueDev` 实例处于 Running；运行时建议保留两个长期终端，若正在恢复数据库则临时使用第三个终端：

```powershell
# 长期终端一：启动 ASP.NET Core API
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --urls http://localhost:5180

# 长期终端二：启动 Vue 开发服务器
pnpm --dir web/port-queue-web run dev
```

启动后访问 `http://localhost:5173` 查看前端原型，访问 `http://localhost:5180/api/health` 查看后端健康接口。首次运行后端前，应先按 `database/README.md` 恢复 SQL Server 数据库；当前健康接口本身不需要数据库连接，真实业务接口会在下一阶段加入。

完整的文件职责、端口约定和常用验证命令见 [`docs/file-guide.md`](docs/file-guide.md) 与 [`docs/runbook.md`](docs/runbook.md)。

分阶段的开发范围、验收标准和阶段门见 [`docs/stage-plans.md`](docs/stage-plans.md)。

## 交付要求

最终交付包括：可恢复的 SQL 脚本、后端源代码、前端源代码、样例数据、测试结果、课程设计报告和答辩演示材料。完整的环境恢复步骤以 `docs/acceptance-plan.md` 为准。
