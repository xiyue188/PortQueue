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
scripts/                   数据生成、数据库校验和交付辅助脚本
config/                    示例配置和本地开发说明
```

## 开始前

1. 安装 SQL Server Developer 或 Express，并确认可以从 VS Code 的 SQL Server 扩展或 `sqlcmd` 连接。
2. 设置项目级 `DOTNET_CLI_HOME` 和 `NUGET_PACKAGES`，再执行 `dotnet restore PortQueue.sln`。
3. 按 `database/README.md` 的顺序执行数据库脚本。
4. 复制示例配置到本地配置文件，填入本机连接字符串；连接密码不提交到 Git。
5. 先阅读 `docs/physical-design-and-operations.md` 和 `docs/acceptance-plan.md`，再实现功能。

## 当前运行方式

当前仓库已经具备可验证的 SQL Server 数据库基线、后端健康接口和前端调度总览原型。前端总览中的队列和指标仍是演示数据，并标注为“接口待联调”；预约业务接口将在下一阶段按验收计划逐步接入。

在 VS Code 的终端中分别启动后端和前端：

```powershell
# 终端一：启动 ASP.NET Core API
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --urls http://localhost:5180

# 终端二：启动 Vue 开发服务器
npm --prefix web/port-queue-web run dev
```

启动后访问 `http://localhost:5173` 查看前端原型，访问 `http://localhost:5180/api/health` 查看后端健康接口。首次运行后端前，应先按 `database/README.md` 恢复 SQL Server 数据库；当前健康接口本身不需要数据库连接，真实业务接口会在下一阶段加入。

完整的文件职责、端口约定和常用验证命令见 [`docs/file-guide.md`](docs/file-guide.md) 与 [`docs/runbook.md`](docs/runbook.md)。

## 交付要求

最终交付包括：可恢复的 SQL 脚本、后端源代码、前端源代码、样例数据、测试结果、课程设计报告和答辩演示材料。完整的环境恢复步骤以 `docs/acceptance-plan.md` 为准。
