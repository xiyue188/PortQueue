# PortQueue 项目协作规则

## 项目范围

PortQueue 是“港口船舶进港预约与锚地排队协同管理系统”的课程设计实现。系统服务单一港口，业务主线是进港预约、锚地排队、泊位时间窗、实际执行和查询统计。

前四阶段的正式定稿是业务与数据模型的唯一依据：

- `C:/Users/admin/Desktop/数据库课设/前四部分定稿/` 中的四份报告是原始权威资料。
- `C:/Users/admin/Documents/Codex/2026-10-03/mie/outputs/2026-10-27_关联修订定稿/` 中的三份修订稿是已落实教师外键意见的设计依据。

## 技术基线

- 开发编辑器：VS Code。
- 后端：ASP.NET Core Web API，目标框架 `net8.0`。
- 数据访问：Dapper + `Microsoft.Data.SqlClient`；SQL 由项目维护，禁止让 ORM 自动创建或修改表结构。
- 数据库：SQL Server。课程 PPT 明确以 MS SQL Server 为数据库平台；MySQL Workbench 不作为本项目的数据库管理工具。
- 前端：Vue 3 + TypeScript + Vite + Element Plus + ECharts。
- 版本管理：Git。提交必须是可运行或可审查的完整变更。

## 数据库规则

1. `database/` 是数据库物理设计和部署脚本的唯一来源。先改 SQL 和设计文档，再修改后端代码。
2. 保持 21 个基本关系、3 个业务视图、17 类实体和 26 个 E-R 联系的语义，不为增加菜单而增加关系。
3. 所有表使用 `dbo` 架构；表名和字段名使用小写蛇形命名；主键使用 `*_id`，外键名称与目标键保持语义一致。
4. 主键、候选键、外键、值域、时间先后、状态流转和历史留痕必须由数据库约束、存储过程或受控触发器明确承担。
5. 业务写操作必须经事务存储过程或等价的应用服务事务完成。页面校验只是改善体验，不能作为唯一安全边界。
6. 船型引用必须是 `dictionary_item` 中类型为 `SHIP_TYPE` 的启用项；字典项停用后保留历史引用，不能重解释既有标识、类型或代码。
7. 计划当前版本使用过滤唯一索引保证最多一个，创建和切换版本的事务保证至少一个；不能把当前版本字段复制到计划表。
8. 事件公共记录必须和一个且仅一个匹配的分类关联一起提交。锚地实际进出时间以事件为唯一事实来源，排队视图负责查询还原。
9. 历史业务记录不得物理删除；基础资料和账号优先停用，预约使用撤销，审计日志只追加。
10. 动态 SQL 只能使用参数化查询；禁止拼接用户输入的表名、排序字段、过滤值或 SQL 片段。

## 开发流程

- 每个阶段先写设计和验收条件，再写代码。
- 每完成一个里程碑，执行构建、测试、SQL 静态检查，并做一次深度 review；review 结论写入 `docs/reviews/`。
- 后端 API、数据库过程、前端页面和测试必须围绕同一条业务用例联调，不能只做孤立页面。
- 不提交连接密码、JWT 密钥、用户密码、生成的数据库文件、`node_modules/`、`bin/`、`obj/` 或日志。
- 报告中的业务术语优先于框架术语；不在正式报告和演示页面中出现内部重构说明。

## 常用命令

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget-packages')
dotnet restore PortQueue.sln
dotnet build PortQueue.sln --configuration Release
dotnet test PortQueue.sln --configuration Release --no-restore
pnpm --dir web/port-queue-web install
pnpm --dir web/port-queue-web run build
```

本机若没有可用的 SQL Server 实例，先安装或启用 SQL Server Developer/Express，再运行 `database/README.md` 中的脚本；不要为了绕过环境问题把模型临时改成 MySQL。
