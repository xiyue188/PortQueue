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
pnpm install
```

项目以根目录 pnpm 工作区和根目录 lockfile 为准。若本机暂时没有 pnpm，可用 `npm --prefix web/port-queue-web install` 和对应的 npm 命令作为临时替代；不要提交 `node_modules/`、`.dotnet-cli-home/` 或 `.nuget-packages/`。

## 恢复数据库

`.sql` 文件不是 PowerShell 可直接执行的程序。把 `database/00_create_database.sql` 直接输入终端不会运行其中的 T-SQL；必须通过 SQL Server 客户端执行。SQL Server 扩展不是必需品，但在 VS Code 中最直观，`sqlcmd` 更适合重复执行和保存命令结果。

### 先区分 LocalDB 实例和数据库

本项目涉及两个名称：

- `(localdb)\PortQueueDev` 是 SQL Server LocalDB **实例**，相当于运行数据库引擎的本机容器。
- `PortQueue` 是该实例中的 **数据库**，其中包含 21 张表、3 个视图和其他数据库对象。

`database/00_create_database.sql` 只负责在已经连接到 SQL Server 的情况下创建 `PortQueue` 数据库，不能创建 LocalDB 实例。LocalDB 实例由 Windows 的 `SqlLocalDB` 工具管理。实例停止时，VS Code 会提示“指定的 LocalDB 实例不存在/不可访问”，即使数据库文件仍然存在。

检查和启动实例：

```powershell
SqlLocalDB info
SqlLocalDB info PortQueueDev
SqlLocalDB start PortQueueDev
```

如果 `SqlLocalDB info PortQueueDev` 显示 `State: Running`，再进行 VS Code 连接。只有在确实显示实例不存在时，才需要创建实例；当前本机的 `PortQueueDev` 已经存在，不要重复创建：

```powershell
SqlLocalDB create PortQueueDev 15.0
SqlLocalDB start PortQueueDev
```

实例启动后，验证数据库是否存在：

```powershell
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d master -Q "SELECT name FROM sys.databases WHERE name = N'PortQueue';" -b
```

如果能返回 `PortQueue`，说明之前的数据库仍然可用；此时只需在 VS Code 中连接，不要重新运行建表脚本。

### 方式一：使用 VS Code SQL Server 扩展

项目已经在 `.vscode/extensions.json` 推荐 `ms-mssql.mssql`。安装后：

1. 在扩展面板安装 **SQL Server (mssql)**。
2. 按 `Ctrl+Shift+P`，执行 `MS SQL: Connect`。
3. 连接服务器 `(localdb)\PortQueueDev`，选择 Windows 身份验证。
4. 打开一个 SQL 文件，确认数据库下拉框为 `PortQueue`（初始化脚本除外）。
5. 点击编辑器右上角 **Run Query**，查看下方结果和错误消息。

### 方式二：使用 sqlcmd（推荐用于可重复验收）

先确认 `sqlcmd` 已在 PATH；如果没有，使用 `database/README.md` 中的完整路径。每条命令执行完都应看到命令输出或错误，不应只把文件路径当作命令输入：

```powershell
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d master -b -i 'database\00_create_database.sql'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d PortQueue -b -i 'database\01_schema.sql'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d PortQueue -b -i 'database\02_views.sql'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d PortQueue -b -i 'database\03_triggers.sql'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d PortQueue -b -i 'database\04_procedures.sql'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d PortQueue -b -i 'database\05_seed_reference.sql'
```

验证脚本：

```powershell
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d PortQueue -b -i 'database\06_smoke_tests.sql'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d PortQueue -b -i 'database\07_business_smoke_test.sql'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -E -d PortQueue -b -i 'database\08_constraint_smoke_tests.sql'
```

`-E` 使用 Windows 身份验证，`-C` 信任本地开发证书，`-b` 让 SQL 错误返回失败状态。没有 `sqlcmd` 时可使用 SQL Server 扩展或 SSMS，执行顺序不变。

### 数据库终端的作用

数据库命令只需要在恢复或验证时执行一次，不需要保持一个“数据库终端”长期运行。数据库服务由 SQL Server/LocalDB 后台运行；命令执行结束后终端回到提示符是正常现象。

## 三个 VS Code 终端分别做什么

建议打开三个终端，职责不同：

- **终端一：数据库操作**。按上面的 `sqlcmd` 命令恢复或测试数据库；脚本执行结束后可以关闭或继续使用。
- **终端二：后端服务**。持续运行 ASP.NET Core，保持窗口不要关闭。
- **终端三：前端开发服务器**。持续运行 Vite，保持窗口不要关闭。

终端二执行：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --urls http://localhost:5180
```

看到 `Now listening on: http://localhost:5180` 和 `Application started` 就表示后端启动成功。截图中的这两行已经说明启动成功；`Failed to determine the https port for redirect` 只是 HTTP 启动配置与 HTTPS 重定向不一致的警告，本项目已经通过 Development 配置关闭本地重定向。

终端三执行：

```powershell
pnpm --dir web/port-queue-web run dev
```

访问 `http://localhost:5173` 查看调度总览原型。当前页面展示演示队列和指标，页面底部明确写有“接口待联调”。这一步用于检查布局和演示交互，不应作为真实业务联调验收证据。

## 质量检查

```powershell
dotnet build PortQueue.sln --configuration Release
dotnet test PortQueue.sln --configuration Release --no-restore
pnpm --dir web/port-queue-web run build
```

每次新阶段完成后，把上述命令结果、数据库脚本验证和未完成项记录在 `docs/reviews/`，再进行下一阶段。
