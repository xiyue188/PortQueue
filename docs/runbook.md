# PortQueue VS Code 启停与验证手册

更新：2026-11-10。当前开放范围见 [中期交付说明](stages/midterm-delivery-guide.md)。

## 日常启动：两个长期终端

在 VS Code 打开 `D:\program\database design`。数据库已部署时，不需要每天重跑建表、迁移或场景导入。LocalDB 在后台运行，不需要一个终端一直挂着 SQL 文件。

终端一启动后端：

```powershell
Set-Location 'D:\program\database design'
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet run --project src/PortQueue.Api --configuration Release --launch-profile http
```

出现 `Now listening on: http://localhost:5180` 后保持终端运行。`http` 配置会使用 Development 和本机数据库；个人连接密码不得提交。后端健康检查为 `http://localhost:5180/api/health`。

终端二启动前端：

```powershell
Set-Location 'D:\program\database design'
pnpm.cmd --dir web/port-queue-web run dev
```

出现 Vite ready 后访问 `http://127.0.0.1:5173/`。浏览器通过 Vite 将 `/api` 转发到 5180，后端再查询 SQL Server。登录、登记和审核都使用真实 API；失败不会回退 Mock。

只复制命令，不复制 `PS ...>` 或 `>>`。若终端卡在 `>>`，先按 Ctrl+C 取消未完成输入。前端任务与命令行二选一，不重复启动。日常启动无需再次 install。

## 停止与端口占用

正常停止：分别在前端和后端原终端按 **Ctrl+C**，等待回到 PowerShell 提示符。只关浏览器不会停止服务。5173 或 5180 已占用时，新服务无法再监听同一端口，这是重复运行造成的占用，不是 pnpm 或代码突然失效。

找不到原终端时，在一个临时终端查看监听进程：

```powershell
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -in 5173,5180 } |
    Select-Object LocalAddress,LocalPort,OwningProcess
```

将输出中的实际 PID 填入下列变量，先核对命令行包含本项目目录、Vite 或 PortQueue.Api，再停止。示例 12345 仅为占位，不能直接照抄使用：

```powershell
$portQueueProcessId = 12345
Get-CimInstance Win32_Process -Filter "ProcessId=$portQueueProcessId" |
    Select-Object ProcessId,ParentProcessId,CommandLine
# 核对属于本项目后执行：
Stop-Process -Id $portQueueProcessId
```

重新执行监听查询，确认端口已释放，再启动。不要批量结束所有 node、dotnet 或 SQL Server 进程，也无需重启计算机。在同一配置下重新 build 前先停止 API，避免 Windows 锁定输出 DLL。本次自动验收的服务由验收结束时清理；用户日常服务由自己的两个终端管理。

## 首次环境与依赖

需要 .NET 8 SDK、Node.js 22.13+、仓库指定 pnpm 11.25.0、SQL Server/LocalDB。先在普通 VS Code 终端检查：

```powershell
dotnet --version
node --version
pnpm.cmd --version
```

缺少 pnpm 时只安装包管理器一次：`npm.cmd install --global pnpm@11.25.0`。首次获取仓库或依赖变更后，在根目录执行：

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet restore PortQueue.sln
pnpm.cmd install --frozen-lockfile
```

不要在 pnpm 管理的依赖目录执行 `npm install`；不生成另一份 package-lock.json。旧 VS Code 若继承旧 PATH，可新建终端或重开 VS Code。

## 数据库恢复与展示

`(localdb)\PortQueueDev` 是 SQL Server 实例，`PortQueue` 是其中的数据库。实例停止时可执行：

```powershell
& 'C:\Program Files\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe' start PortQueueDev
```

首次准备或需要检查本机环境时，才使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\database\bootstrap-localdb.ps1
```

该脚本检查/启动实例，数据库不存在时恢复结构；已有数据库执行受控主港参考资料迁移、补齐参考数据并验证，不重复建表、不自动加载业务场景、不更新既有业务过程。完整部署及升级顺序见 [数据库说明](../database/README.md)，旧青岛场景库必须先 15、后更新版 05。`.sql` 文件必须通过 SQL Server 扩展、SSMS 或 sqlcmd 执行，不能把路径当作 PowerShell 命令。

查看对象用 SQL Server 扩展连接 `(localdb)\PortQueueDev / PortQueue`。表结构可展示列、主外键、约束、索引；表数据用 SELECT；业务视图展开 Views 后查询；表级触发器在所属表下查看，不在数据库级 DDL Triggers 节点。

现有库不要重跑 `01_schema.sql`。日常检查使用：

```powershell
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S '(localdb)\PortQueueDev' -E -C -b -f 65001 -i database/06_smoke_tests.sql
```

`-b` 让失败立即返回，`-f 65001` 按 UTF-8 解释中文。数据库结构和基线的完整验证与页面业务验收分别记录，数据库对象已存在不代表后续页面已接入。

## 可重复的中期演示

1. 打开总览：上海洋山深水港、系统登记设施、真实预约分布及港口动态；后续入口显示待接入。
2. 用调度员登录，初始密码 `123456`（仅显式启用的 Development 专用账号），登记并编辑草稿。
3. 申请到港选择未来时间，提交；切换审核员，批准或填写原因驳回。
4. 刷新或重开浏览器，核对状态、责任人和审核时间；在数据库核对本次预约及审计。

不要为恢复截图数字重置数据库。原场景是 53 条，用户可以继续新增；本轮验收时 54 条，系统以当前查询为准。日期、账号权限及无效输入按真实约束处理。

## 自动验证

```powershell
dotnet build PortQueue.sln --configuration Release
dotnet test PortQueue.sln --configuration Release --no-restore
powershell -ExecutionPolicy Bypass -File tests/database/check-sql.ps1
python tests/database/run_stage1.py
pnpm.cmd --dir web/port-queue-web run build
```

浏览器验收先启动 5180 开发 API，再执行：

```powershell
pnpm.cmd --dir web/port-queue-web run test:e2e
```

Playwright 使用本机 Edge，自动启动测试 Vite 或复用已有 5173；查询测试读取开发库，写入测试创建独立随机库及 5280 API 后清理，不修改开发库的预约。需要已装载原场景作为搜索样本，但总量和待审核数不再写死，保留用户新增数据。截图在 `.impeccable/review/`，结果和失败 trace 在 `artifacts/`，都不提交。结束后核对 5173/5180；原本由用户启动的服务由用户停止。
