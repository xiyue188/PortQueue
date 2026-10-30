# PortQueue 数据库脚本

脚本面向 SQL Server Developer/Express。执行顺序固定为：

1. `00_create_database.sql`：创建数据库并设置基础选项。
2. `01_schema.sql`：创建 21 个基本关系、约束和索引。
3. `02_views.sql`：创建 3 个业务查询视图。
4. `03_triggers.sql`：创建字典船型引用和事件分类保护触发器。
5. `04_procedures.sql`：创建预约审核、计划确认、锚地事件和靠离泊事件过程。
6. `05_seed_reference.sql`：写入可重复执行的角色、字典和演示基础资料。

建议在 SSMS、VS Code SQL Server 扩展或 `sqlcmd` 中按顺序执行。脚本中的 `GO` 是 SQL Server 客户端批处理分隔符，不是 T-SQL 语句。

如果不确定本机 LocalDB 实例是否存在，优先执行仓库内的 `bootstrap-localdb.ps1`。它会检查并启动 `PortQueueDev`，只在 `PortQueue` 不存在时恢复 `00` 至 `05` 脚本；已有数据库不会重复建表。

## 本地连接示例

```powershell
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
& $sqlcmd -C -S '(localdb)\PortQueueDev' -i 'database\00_create_database.sql'
```

这里的 `(localdb)\PortQueueDev` 是本机 LocalDB 实例名，`PortQueue` 是实例中的数据库名。实例停止时先执行 `SqlLocalDB start PortQueueDev`；实例启动和数据库创建是两个步骤。`00_create_database.sql` 不会创建 LocalDB 实例，只会在 SQL Server 连接成功后创建数据库。

当前项目不把连接密码写入脚本。若 LocalDB 无法启动，请安装 SQL Server Developer 或 Express 并在本地配置连接字符串；不要因为客户端工具不同而改动关系模型。

## 物理设计原则

- 表、视图和过程全部使用 `dbo` 架构。
- 业务表的标识使用 `BIGINT IDENTITY(1,1)`，便于演示环境稳定生成；业务编号和联合候选键仍由唯一约束保证。
- `datetime2(0)` 统一精确到秒；系统约定按本港时区解释。
- 字符串业务文本使用 `nvarchar`，代码和状态使用 `varchar`。
- 历史记录不级联删除；外键默认 `NO ACTION`。
- 跨表约束不能用普通 `CHECK` 表达的部分，由触发器和事务过程共同保证。

## 验证脚本

`06_smoke_tests.sql` 检查 21 张表、3 个视图、5 个存储过程、6 个业务触发器、过滤唯一索引、索引总数和参考种子数据；参考数据包括港口、码头、锚地、泊位及泊位船型关系。`07_business_smoke_test.sql` 回滚式贯通审核、锚地事件、计划确认、实际进港、靠泊和离泊；`08_constraint_smoke_tests.sql` 回滚式验证错误字典类型被拒绝。

为避免不同 Windows 客户端对无 BOM UTF-8 SQL 文件的编码差异，种子脚本中的设施名称使用稳定的英文文本；业务编码、关系和后续前端中文显示不受影响。

连接成功后，SQL Server 扩展的对象树可以直接展开 `PortQueue` 查看这些对象；对象树不显示行数据，行数据通过 `SELECT` 查询或 `06_smoke_tests.sql` 结果查看。
