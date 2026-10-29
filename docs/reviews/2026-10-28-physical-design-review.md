# 2026-10-28 物理设计与业务操作阶段评审

## 范围

评审对象是 `database/` 的 SQL Server 脚本、后端骨架、前端骨架及本阶段实施文档。业务依据为前四阶段定稿和 2026-10-27 外键修订稿。

## 结论

本阶段初始化基线可继续进入 API 与数据库联调。物理对象与逻辑模型保持一致：21 张基本表、3 个业务视图、过滤唯一索引、船舶与泊位适用船型的真实字典外键均已落地。核心过程覆盖预约审核、锚地事件、实际进港、泊位确认及靠离泊事件。

## 检查证据

- 独立 SQL Server 2019 LocalDB 实例 `PortQueueDev` 可启动。
- 从空数据库依次执行 `00_create_database.sql` 至 `05_seed_reference.sql` 成功。
- SQL Server 实际创建 21 张表、3 个视图、5 个存储过程、6 个业务触发器和 2 个过滤唯一索引。
- `07_business_smoke_test.sql` 贯通审核、进入锚地、离开锚地、计划确认、实际进港、靠泊和离泊，并输出：`COMPLETED / LEFT / COMPLETED / 5 / 1 / 1`。测试末尾回滚业务数据。
- `08_constraint_smoke_tests.sql` 验证非 `SHIP_TYPE` 字典项不能写入泊位适用船型，测试末尾回滚。
- 后端 `dotnet build PortQueue.sln --configuration Release` 通过，`dotnet test PortQueue.sln --configuration Release --no-restore` 通过 1 项健康检查测试。
- 前端 `npm --prefix web/port-queue-web run build` 通过，包含 Vue 类型检查和 Vite 生产构建。

## 修订记录

深度 review 发现并修复了三类问题：SQLCMD 对含中文错误字符串的脚本解释不稳定，执行对象改为 ASCII 错误码消息；过滤唯一索引要求模块创建和调用使用正确的 `ANSI_NULLS`、`QUOTED_IDENTIFIER` 选项；离泊必须沿用靠泊时固定的执行基准版本，实际进港过程单独受控。前端原型明确标注为“接口待联调”，不冒充已连接数据库。

## 未完成项

数据库仍需补充通用审计触发器、队列调序过程、计划版本创建过程、数据库权限用户和可重复的全量演示数据。后端还需接入 Dapper 查询和过程调用，前端需把原型数据替换为 API 数据。这些属于下一阶段实现工作，不改变本阶段已确定的物理模型。
