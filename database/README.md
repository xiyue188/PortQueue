# PortQueue 数据库脚本

脚本面向 SQL Server Developer/Express。执行顺序固定为：

1. `00_create_database.sql`：创建数据库并设置基础选项。
2. `01_schema.sql`：创建 21 个基本关系、约束和索引。
3. `02_views.sql`：创建 3 个业务查询视图。
4. `03_triggers.sql`：创建字典船型引用、事件分类、字典标识和审计追加保护触发器。
5. `04_procedures.sql`：创建预约审核、队列调序、计划版本、计划确认、锚地事件、实际进港和靠离泊过程。
6. `05_seed_reference.sql`：写入可重复执行的角色、字典和参考设施；不重新启用已停用字典项。
7. `09_permissions.sql`：创建应用最小权限角色和无登录验证用户。
8. `10_seed_demo.sql`：按需加载原五条流程样例，同时识别整理后的业务编号，重跑不会复制旧样例；不由启动脚本自动装载。
9. `11_statistics.sql`：按明确口径执行统计查询，不新增持久化指标关系。
10. `12_refresh_sample_labels.sql`：整理原五条样例的编号、船名、IMO 和旧审核文案，保留关联及业务事实，追加修改快照。
11. `13_seed_port_scenarios.sql`：按需增加 48 条合成场景，覆盖六种预约状态、锚地等待、确认计划和已完成作业；重跑跳过已存在编号。

建议在 SSMS、VS Code SQL Server 扩展或 `sqlcmd` 中按顺序执行。脚本中的 `GO` 是 SQL Server 客户端批处理分隔符，不是 T-SQL 语句。

`01_schema.sql` 是空数据库的一次性建表脚本。对已经存在业务表的 `PortQueue` 再次执行时，脚本会在第一张表之前给出一条明确提示并停止，不会重复建表。已有数据库的对象更新请只执行“阶段 1 已有数据库升级”中的增量脚本；查看当前结构请执行 `06_smoke_tests.sql`，不要把 `01_schema.sql` 当作日常启动或升级命令。

如果不确定本机 LocalDB 实例是否存在，优先执行仓库内的 `bootstrap-localdb.ps1`。它会检查并启动 `PortQueueDev`，只在 `PortQueue` 不存在时恢复 `00` 至 `05` 和 `09` 脚本；已有数据库不会重复建表，也不会自动加载业务演示数据。

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
- `datetime2(0)` 统一保存 UTC 并精确到秒；页面按港口时区 Asia/Shanghai 转换。
- 字符串业务文本使用 `nvarchar`，代码和状态使用 `varchar`。
- 历史记录不级联删除；外键默认 `NO ACTION`。
- 跨表约束不能用普通 `CHECK` 表达的部分，由触发器和事务过程共同保证。

## 验证脚本

`06_smoke_tests.sql` 检查 21 张表、3 个视图、7 个业务过程、8 个表级业务触发器、过滤唯一索引、业务表索引和参考种子数据；参考数据包括港口、码头、锚地、泊位及泊位船型关系。`07_business_smoke_test.sql` 回滚式贯通审核、锚地事件、计划确认、实际进港、靠泊和离泊；`08_constraint_smoke_tests.sql` 回滚式验证错误字典类型被拒绝。

SQL 文件按 UTF-8 保存。通过 Windows ODBC 版 `sqlcmd` 执行含中文脚本时必须显式指定 `-f 65001`；否则可能把中文解析为错误字节并报字符串未闭合等误导性错误。早期参考设施保留英文名称，本轮场景脚本使用中文业务名称；不要通过改中文内容来绕过客户端编码配置。

连接成功后，SQL Server 扩展的对象树可以直接展开 `PortQueue` 查看这些对象；对象树不显示行数据，行数据通过 `SELECT` 查询或 `06_smoke_tests.sql` 结果查看。

## 阶段 1 已有数据库升级

本机开发库已于 2026-11-02 完成升级。其他已有数据库按以下命令更新可重复的过程、触发器、参考数据和权限；不要重跑 `01_schema.sql`，也不删除旧数据库文件。`bootstrap-localdb.ps1` 仅准备环境和验证数量，不在启动时改写已有业务过程。

```powershell
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
foreach ($file in @('03_triggers.sql','04_procedures.sql','05_seed_reference.sql','09_permissions.sql')) {
    & $sqlcmd -S '(localdb)\PortQueueDev' -E -C -b -f 65001 -i (Join-Path 'database' $file)
    if ($LASTEXITCODE -ne 0) { throw "Database deployment failed: $file" }
}
```

`-f 65001` 明确 UTF-8，`-b` 保证失败停止。`09` 只创建无登录验证用户；实际 API 使用低权限连接须另外映射非管理员 Windows 身份，LocalDB 所有者不会被此脚本降权。

## 阶段 1 验收与演示

```powershell
# SQL Server 2019 语法检查，复用 SQL Server VS Code 扩展中的 ScriptDom。
powershell -ExecutionPolicy Bypass -File tests/database/check-sql.ps1
# 标准库 Python + sqlcmd；随机测试库内完成事务、权限、并发和演示验证后清理。
python tests/database/run_stage1.py
# 独立装载合成演示事实；重跑不会重置业务状态。
sqlcmd -S '(localdb)\PortQueueDev' -E -C -b -f 65001 -i database/10_seed_demo.sql
sqlcmd -S '(localdb)\PortQueueDev' -E -C -b -f 65001 -i database/11_statistics.sql
```

SQL Server 2019 LocalDB 上的基线为 21 表、3 视图、7 业务过程、8 表级触发器、45 个业务表索引。此前的 203 包含系统索引，不作为业务对象数量。完整设计和用例见 `docs/stages/stage-1-database.md`，结果见 `docs/reviews/2026-11-02-stage1-database-review.md`。

## 阶段 2 场景数据与总览

本机已于 2026-11-08 装载本批数据。新环境在基线部署完成后按 `10 → 12 → 13` 执行；`11` 是查询，不是数据导入依赖。现有库不重跑 `01_schema.sql`。

```powershell
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
foreach ($file in @('10_seed_demo.sql','12_refresh_sample_labels.sql','13_seed_port_scenarios.sql')) {
    & $sqlcmd -S '(localdb)\PortQueueDev' -E -C -b -f 65001 -i (Join-Path 'database' $file)
    if ($LASTEXITCODE -ne 0) { throw "Scenario import failed: $file" }
}
```

仅有项目场景数据时共 53 条预约：草稿 8、待审核 9、已批准 10、已驳回 8、已撤销 8、已完成 10；49 个不同申请到港时刻。原五条历史申请时间保留。数据不是实时港口或 AIS 数据；船舶标识的格式正确不代表注册资料已核实。脚本用整批事务与应用锁防止半批写入，重复执行不重置已有状态；`12` 每次实际改名保留前后审计快照。

`queries/overview.sql` 为只读 API 的 SQL 源，通过现有视图计算三个独立指标并读取最近五个实际事件；它不创建第 4 个 VIEW。`13` 的历史审核/确认是有明确时间的归档快照，实际事件通过现有事务过程登记，不能把该初始化脚本当作日常审核入口。场景账号使用不可登录的随机占位哈希，没有可供用户登录的预设密码。

业务口径、角色边界和后续实现顺序见 `docs/business-flow.md`；独立测试库会验证导入、幂等性、责任人、最小读取权限和总览结果。
