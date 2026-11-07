# PortQueue VS Code 启停手册

## 环境前提

- VS Code、.NET 8 SDK、Node.js 22.13+ 和 Git 已安装；当前实测 Node.js 22.20.0，仓库指定 pnpm 11.25.0。
- SQL Server Developer/Express 或 LocalDB 已安装；数据库恢复步骤见 `database/README.md`。
- 在项目根目录 `D:\program\database design` 打开 VS Code。

## 第一次准备

先在普通 VS Code 终端确认 `node --version` 和 `pnpm.cmd --version`。缺少 pnpm 时只需安装一次；`npm` 在这里用于安装包管理器，不用于重新安装项目依赖：

```powershell
npm.cmd install --global pnpm@11.25.0
pnpm.cmd --version
```

2026-11-07 已在本机完成安装，命令位于 `C:\npm-global\pnpm.cmd`，该目录已在系统 PATH 中，无需再次安装。若旧 VS Code 终端仍找不到命令，关闭该终端后新建；如 VS Code 继承的是旧 PATH，再重启 VS Code。

恢复项目依赖只在首次获取项目或依赖变更后进行，在仓库根目录执行：

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet restore PortQueue.sln
pnpm.cmd install --frozen-lockfile
```

项目以根目录 pnpm 工作区和 `pnpm-lock.yaml` 为准。不要对现有 pnpm 依赖目录执行 `npm install` 或 `npm ci`，也不要生成第二份 `package-lock.json`。2026-11-06 的 npm 安装日志显示，它在 pnpm junction 目标中执行了 axios/ECharts 的 prepare 脚本，并因找不到 husky 失败；本项目没有新增 Husky 的需求。2026-11-07 使用正确的 pnpm 检查依赖和构建均通过，未删除依赖目录、未修改锁文件。

日常启动不需要先 install。依赖已存在时，`npm.cmd --prefix web/port-queue-web run dev` 也能运行同一个 Vite 脚本；这里允许的是 `run`，不是 `install`。不要提交依赖目录、缓存或日志。

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

### 一条命令完成本机准备

如果你不确定实例是否存在、是否停止或数据库是否已经创建，直接在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\database\bootstrap-localdb.ps1
```

脚本会自动完成以下判断：

1. 检查 `PortQueueDev` 是否存在；不存在才创建。
2. 检查实例是否运行；停止才启动。
3. 检查 `PortQueue` 是否存在；不存在才按 `00` 至 `05` 恢复。
4. 数据库登记不存在但旧 MDF/LDF 文件存在时，尝试安全附加旧数据库文件；附加失败立即停止，不删除文件。
5. 数据库已经存在时不执行建表脚本、不改写业务过程、不删除现有数据；参考种子只补齐缺失基础资料。升级按 `database/README.md` 显式执行 `03`、`04`、`05`、`09`，环境启动不兼任对象升级。
6. 每一个 SQL 命令失败都会立即停止，不会继续执行后续脚本。
7. 最后验证并输出表、视图、存储过程、触发器、索引和参考种子数据数量。

正常结果应包含：

```text
current_database    PortQueue
table_count         21
view_count          3
procedure_count     7
trigger_count        8
index_count         45
```

这个脚本解决的是本地环境准备问题，不属于应用业务功能；它不会修改 21 个关系的设计。旧文件无法附加时，脚本会明确报错并停止，不能继续往下执行。

### 方式一：使用 VS Code SQL Server 扩展

项目已经在 `.vscode/extensions.json` 推荐 `ms-mssql.mssql`。安装后：

1. 在扩展面板安装 **SQL Server (mssql)**。
2. 按 `Ctrl+Shift+P`，执行 `MS SQL: Connect`。
3. 连接服务器 `(localdb)\PortQueueDev`，选择 Windows 身份验证。
4. 打开一个 SQL 文件，确认数据库下拉框为 `PortQueue`（初始化脚本除外）。
5. 点击编辑器右上角 **Run Query**，查看下方结果和错误消息。

连接成功后，在左侧数据库树展开 `PortQueue`：

- `Tables`：应看到 21 张基本表。
- `Views`：应看到 `v_port_event`、`v_anchorage_queue`、`v_current_berth_plan`。
- `Programmability > Stored Procedures`：应看到 7 个业务过程。
- `Programmability > Database Triggers` 通常只展示数据库级 DDL 触发器，本项目没有这类对象，因此显示 `No items` 是正常的。
- 8 个业务触发器是表级 DML 触发器，应在所属表的 `Triggers` 节点查看；如果扩展不显示表级节点，用 `sys.triggers` 查询确认。
- 展开任意表的 `Indexes`：可查看主键、唯一索引和查询索引。
- 运行对象清单查询时，应看到 45 个业务表索引（包括主键、唯一索引和查询索引；只统计 21 张 dbo 业务表，不混入系统对象）。

如果对象树没有及时刷新，在 `PortQueue` 数据库节点上右键选择 **Refresh**。也可以运行 `database/06_smoke_tests.sql`，结果面板会同时显示对象数量和参考种子数据行数。

确认表级触发器的查询：

```sql
SELECT
    t.name AS trigger_name,
    OBJECT_SCHEMA_NAME(t.parent_id) AS parent_schema,
    OBJECT_NAME(t.parent_id) AS parent_table,
    t.parent_class_desc,
    t.is_disabled
FROM sys.triggers AS t
WHERE t.parent_class = 1
  AND t.is_ms_shipped = 0
ORDER BY t.name;
```

预期返回 8 行，`parent_class_desc` 为 `OBJECT_OR_COLUMN`。不要为了填充数据库级触发器节点而新增无业务意义的 DDL 触发器。

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

后端端口和前端端口不是同一个地址：

- `http://localhost:5180/` 是 API 根路径，现在返回服务状态 JSON。
- `http://localhost:5180/api/health` 是后端健康接口。
- `http://127.0.0.1:5173/` 才是 Vue 前端页面，与 Vite 的监听地址一致。

如果在浏览器打开 `http://localhost:5180/`，看到 JSON 或 API 响应，说明后端已启动；不要把后端端口当成前端页面地址。

终端三执行：

```powershell
Set-Location 'D:\program\database design'
pnpm.cmd --dir web/port-queue-web run dev
```

只复制代码块中的命令，不复制 `PS D:\...>` 或 `>>` 提示符。如果终端已经停在 `>>` 续行状态，先按 Ctrl+C 退出未完成输入。看到 `VITE ... ready` 和 `Local: http://127.0.0.1:5173/` 表示启动成功；终端持续运行是正常现象，用 Ctrl+C 停止。Windows 下 `.cmd` 直接调用命令入口，避免选择 PowerShell 同名 `.ps1` 包装脚本。

也可按 Ctrl+Shift+P → `Tasks: Run Task` → `frontend: dev`，任务固定以仓库根目录执行。命令行和任务二选一，只启动一个前端服务。项目设置了 `strictPort: true`，5173 已占用时会明确报错，不自动改用 5174；先访问已有页面或在原启动终端按 Ctrl+C，不要直接关闭所有 Node 进程。

访问 `http://127.0.0.1:5173` 查看真实预约总览，点击“进港预约”打开列表与详情。两个布局共用 `/api/appointments` 的 SQL Server 查询结果，支持状态、关键词和分页；旧静态原型已删除。船舶图片标注“示意”，不代表记录对应的实际船舶照片。

阶段 2 的快速验证：当前数据库有 5 条 `PQ-DEMO` 记录，总览表格可在内部滚动。选择“每页 2 条”后点击“下一页”，记录应改变且总数保持 5；输入 `PQ-DEMO-005` 后点击查询，或直接选择“待审核”，应只显示匹配记录。点击船舶后摘要和详情同步更新。数据库不可用时应显示错误和“重新查询”，而不是空表或演示数据。

## 质量检查

```powershell
dotnet build PortQueue.sln --configuration Release
dotnet test PortQueue.sln --configuration Release --no-restore
pnpm --dir web/port-queue-web run build
```

每次新阶段完成后，把上述命令结果、数据库脚本验证和未完成项记录在 `docs/reviews/`，再进行下一阶段。

## 前端浏览器验收

已提供基于本机 Microsoft Edge 的 Playwright 验收。先启动 5180 后端，并确认开发库仍为阶段 2 的五条 PQ-DEMO 演示基线，再执行：

```powershell
pnpm.cmd --dir web/port-queue-web run test:e2e
```

该命令复用已有 5173 前端，或自动启动一个测试用 Vite。正向测试访问真实 API；故障与迟到响应测试只在浏览器内拦截业务请求，不停数据库、不写开发库。预期样例已经变化时，应核对测试前置条件，不要为跑测试清空数据库。截图位于 `.impeccable/review/`，JSON 结果和失败 trace 位于 `artifacts/`，均不提交。

在同一配置下重建后端前，先在其终端按 Ctrl+C 停止本项目 API，避免 Windows 锁定输出 DLL；构建完成后再启动，不需要重启计算机或终止所有 dotnet 进程。
