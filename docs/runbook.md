# PortQueue VS Code 启停手册

## 环境前提

- VS Code、.NET 8 SDK、Node.js LTS 和 Git 已安装。
- SQL Server Developer/Express 或 LocalDB 已安装；数据库恢复步骤见 `database/README.md`。
- 在项目根目录 `D:\program\database design` 打开 VS Code。

## 第一次准备

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet restore PortQueue.sln
npm --prefix web/port-queue-web install
```

如果使用 pnpm，应按根目录 `package.json` 执行对应命令；当前构建验证也可使用 npm。不要提交 `node_modules/`、`.dotnet-cli-home/` 或 `.nuget-packages/`。

## 恢复数据库

在 SQL Server 扩展、`sqlcmd` 或 SSMS 中依次执行：

```text
database/00_create_database.sql
database/01_schema.sql
database/02_views.sql
database/03_triggers.sql
database/04_procedures.sql
database/05_seed_reference.sql
```

执行 `06_smoke_tests.sql` 检查对象数量，再执行 `07_business_smoke_test.sql` 和 `08_constraint_smoke_tests.sql`。后两个脚本会在测试事务中回滚业务数据。

## 启动后端

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --urls http://localhost:5180
```

验证：打开 `http://localhost:5180/api/health`，应看到 `status` 为 `ok`。当前健康接口用于确认 Web API 进程启动；它不代表预约业务已经完成。

## 启动前端

在另一个 VS Code 终端执行：

```powershell
npm --prefix web/port-queue-web run dev
```

访问 `http://localhost:5173` 查看调度总览原型。当前页面展示演示队列和指标，页面底部明确写有“接口待联调”。这一步用于检查布局和演示交互，不应作为真实业务联调验收证据。

## 质量检查

```powershell
dotnet build PortQueue.sln --configuration Release
dotnet test PortQueue.sln --configuration Release --no-restore
npm --prefix web/port-queue-web run build
```

每次新阶段完成后，把上述命令结果、数据库脚本验证和未完成项记录在 `docs/reviews/`，再进行下一阶段。
