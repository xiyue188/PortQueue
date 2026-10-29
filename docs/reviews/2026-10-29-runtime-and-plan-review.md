# 2026-10-29 运行基线与分阶段计划评审

## 评审范围

本次评审覆盖本地启动方式、SQL Server 脚本执行方式、项目目录组织、模块注释、前端工作区 lockfile、Development HTTPS 配置和后续阶段计划。

## 发现与处理

1. 直接在 PowerShell 输入 `.sql` 路径不能执行 T-SQL。已在 `docs/runbook.md` 明确区分 SQL Server 扩展、SSMS 和 `sqlcmd`，并给出可复制的完整命令。
2. 截图中的 `Now listening on` 和 `Application started` 表示后端已经成功启动；`Failed to determine the https port for redirect` 是 HTTP 启动地址没有 HTTPS 端口时的配置警告。已增加 `EnableHttpsRedirection` 配置，Development 使用 HTTP 时关闭重定向，生产配置保留开启能力。
3. 前端目录同时存在独立 lockfile 与工作区根 lockfile，依赖来源重复。已删除前端目录的重复 lockfile，统一以根目录 `pnpm-lock.yaml` 为准。
4. 源码和 SQL 文件缺少统一的模块说明。已补充 C# XML 注释、Vue/TypeScript/CSS 模块注释和 SQL 文件头注释，并把注释边界写入文件职责索引。
5. 原路线中“队列调序、计划版本创建、审计、权限、完整演示数据、统计查询、API 集成测试”容易被理解成已经完成。已拆为阶段 1 的明确交付项，并为后续预约、队列、泊位、执行和统计阶段补充范围、验收标准、阶段门和不做事项。

## 组织结论

当前组织符合数据库优先单仓库实践：`database/` 是独立 SQL Server 交付物，`src/` 是后端，`web/` 是前端，`tests/` 与代码并列，`docs/` 保存设计与证据。没有迁移目录，因为迁移会增加路径变化和提交噪声，不能改善课程设计的验收边界。

## 验收结果

- `dotnet build PortQueue.sln --configuration Release`：通过，0 个警告、0 个错误。
- `dotnet test PortQueue.sln --configuration Release --no-restore`：通过，1/1。
- `pnpm --dir web/port-queue-web run build`：通过，Vue 类型检查和 Vite 构建完成；包体积提示保留为后续真实页面拆分时的优化项。
- `database/06_smoke_tests.sql`：通过，21 张表、3 个视图、2 个过滤唯一索引。
- `database/07_business_smoke_test.sql`：通过，`COMPLETED / LEFT / COMPLETED / 5 / 1 / 1`。
- `database/08_constraint_smoke_tests.sql`：通过，错误字典子类型被拒绝。
- `http://localhost:5180/api/health`：返回 `service=PortQueue.Api`、`status=ok`；该端口由用户当前启动的后端占用。
- 临时端口 5181 启动：只输出 `Now listening` 和 `Application started`，未再输出 HTTPS 重定向警告。

## 过程性问题

一次同时运行 `dotnet build` 和 `dotnet test` 时，两个进程竞争同一个 Release 输出 DLL，构建出现文件占用错误；测试仍通过。串行复跑后构建通过。以后验收命令按“构建 → 测试 → 前端构建 → SQL 测试”顺序执行，不并行写同一输出目录。

## 下一阶段准入条件

进入阶段 1 前保持当前工作区干净；阶段 1 只补数据库对象和数据库验收，不同时开发完整 API 与前端页面。阶段 1 review 通过后，才进入阶段 2 的预约查询真实闭环。
