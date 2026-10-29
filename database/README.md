# PortQueue 数据库脚本

脚本面向 SQL Server Developer/Express。执行顺序固定为：

1. `00_create_database.sql`：创建数据库并设置基础选项。
2. `01_schema.sql`：创建 21 个基本关系、约束和索引。
3. `02_views.sql`：创建 3 个业务查询视图。
4. `03_triggers.sql`：创建字典船型引用和事件分类保护触发器。
5. `04_procedures.sql`：创建预约审核、计划确认、锚地事件和靠离泊事件过程。
6. `05_seed_reference.sql`：写入可重复执行的角色、字典和演示基础资料。

建议在 SSMS、VS Code SQL Server 扩展或 `sqlcmd` 中按顺序执行。脚本中的 `GO` 是 SQL Server 客户端批处理分隔符，不是 T-SQL 语句。

## 本地连接示例

```powershell
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -i 'database\00_create_database.sql'
```

当前项目不把连接密码写入脚本。若 LocalDB 无法启动，请安装 SQL Server Developer 或 Express 并在本地配置连接字符串；不要因为客户端工具不同而改动关系模型。

## 物理设计原则

- 表、视图和过程全部使用 `dbo` 架构。
- 业务表的标识使用 `BIGINT IDENTITY(1,1)`，便于演示环境稳定生成；业务编号和联合候选键仍由唯一约束保证。
- `datetime2(0)` 统一精确到秒；系统约定按本港时区解释。
- 字符串业务文本使用 `nvarchar`，代码和状态使用 `varchar`。
- 历史记录不级联删除；外键默认 `NO ACTION`。
- 跨表约束不能用普通 `CHECK` 表达的部分，由触发器和事务过程共同保证。

## 验证脚本

`06_smoke_tests.sql` 检查 21 张表、3 个视图、过滤唯一索引和关键字段；`07_business_smoke_test.sql` 回滚式贯通审核、锚地事件、计划确认、实际进港、靠泊和离泊；`08_constraint_smoke_tests.sql` 回滚式验证错误字典类型被拒绝。
