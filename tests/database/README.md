# 数据库验收工具

本目录验证数据库对象和事务业务，不依赖 API，也不改变前端。完整用例定义见 `docs/stages/stage-1-database.md`。

## 运行

在仓库根目录执行：

```powershell
python tests/database/run_stage1.py
```

需要 Python 3.10+、SQL Server 2019+ 和 `sqlcmd`。测试只用 Python 标准库，无需新增 pip 依赖；默认实例为 `(localdb)\PortQueueDev`，可通过 `--server` 指定测试实例。运行身份必须能够创建/删除测试数据库和模拟数据库用户。

工具在随机命名的 `PortQueueStage1Test_*` 库里部署项目 SQL、运行 50 个事务/业务/权限用例、4 个双连接并发用例和 5 个集成用例。正常和错误路径均断言数据库结果，SQL 错误立即报告失败。每个业务夹具回滚后检查无测试预约残留；并发测试的共享数据只存在于独立测试库。默认结束时删除本次创建的库，不读取开发库业务数据。

`--keep-database` 可保留主要测试库供人工复核，终端打印保留的库名；第二个演示验收库始终清理。结果写入被 Git 忽略的 `artifacts/stage1-results.json`，长期验收结论记录到 `docs/reviews/`。

## 静态检查

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
powershell -ExecutionPolicy Bypass -File tests/database/check-sql.ps1
```

`check-sql.ps1` 查找 SQL Server VS Code 扩展自带的 ScriptDom；没有该扩展时用 `-ParserAssembly` 指向本机已有 DLL。独立的 `SqlSyntaxCheck` .NET 8 控制台程序解析 SQL Server 2019 语法，不执行 SQL，不需要数据库连接。它不加入 API 解决方案，避免 API 构建依赖本机扩展路径。

## 验证边界

双连接使用两个 sqlcmd 进程，通过数据库已授予的应用锁确认请求重叠；不会把单线程顺序执行当作并发通过。部署脚本用不带 `-I` 的普通连接，要求每个脚本自行声明所需 SET 选项。历史时间用例的审批和确认时间由管理员夹具明确设置，生产过程校验不会为测试放宽。
