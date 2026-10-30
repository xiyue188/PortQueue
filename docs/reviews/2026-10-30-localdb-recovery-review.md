# 2026-10-30 LocalDB 恢复与数据库展示评审

## 根因

用户执行引导脚本时，`PortQueueDev` 实例刚创建成功，但当前实例目录中尚未登记 `PortQueue`；同时用户目录下已经存在旧的 `PortQueue.mdf` 和 `PortQueue_log.ldf`。`00_create_database.sql` 直接执行 `CREATE DATABASE [PortQueue]`，因此因同名文件存在而失败。原引导脚本没有检查 `sqlcmd.exe` 的退出码，建库失败后继续执行 `01` 至 `05`，并错误输出完成信息。

## 修复

- 引导脚本增加统一的外部命令和 SQL 查询失败检查，任何 SQL 错误立即停止。
- 数据库未登记但成对 MDF/LDF 文件存在时，先尝试 `FOR ATTACH` 安全附加；只有没有旧文件时才执行 `00` 至 `05` 恢复。
- 参考种子脚本单独幂等执行，即使数据库已经存在，也会补齐缺少的码头、锚地、泊位等基础资料；不会重复插入业务事实。
- 旧文件只有一半时明确报错，不删除或移动文件。
- 成功后强制校验 21 张表、3 个视图、5 个存储过程和 6 个触发器，并输出索引数量和参考数据行数。
- 种子脚本补充码头、两个锚地、一号泊位和泊位船型关系，使用幂等 `IF NOT EXISTS`，不会重复插入。
- 新增对象树和行数查看说明，明确数据库脚本、数据库对象和 VS Code 展示之间的关系。

## 实际证据

当前 `(localdb)\PortQueueDev` 中已验证：

```text
database       PortQueue
tables         21
views          3
procedures     5
triggers       6
indexes        203
```

参考数据：

```text
role              4
dictionary_item   8
port              1
terminal          1
anchorage         2
berth             1
berth_ship_type   1
```

业务冒烟测试继续输出：

```text
COMPLETED / LEFT / COMPLETED / 5 / 1 / 1
```

约束冒烟测试继续通过：错误的非 `SHIP_TYPE` 字典项被拒绝。

## 已知边界

本次补充的是基础参考数据，不是完整业务演示链路。预约、队列、计划和事件测试数据仍由回滚式冒烟测试生成，完整可重复答辩数据将在阶段 1 的剩余工作中单独设计和验收。
