# 技术决策记录

## ADR-001 数据库平台

**决定：使用 SQL Server。**课程 PPT 将 MS SQL Server 列为数据库管理系统示例；项目逻辑模型中的 `DATETIME2`、`NVARCHAR`、过滤唯一索引、事务和存储过程也与 SQL Server 方案一致。MySQL Workbench 只管理 MySQL，不能直接管理 SQL Server。若改用 MySQL，需要重新验证所有物理类型、索引、过程、触发器和报告截图，因此不作为当前路线。

## ADR-002 开发编辑器

**决定：使用 VS Code。**VS Code 不限制 .NET、Node 或 SQL Server 的能力；C# Dev Kit、C# 扩展、Vue - Official 和 SQL Server 扩展分别承担代码、调试、Vue 语言服务和数据库查询功能。Visual Studio 不是项目运行的必要条件。

## ADR-003 后端数据访问

**决定：ASP.NET Core Web API + Dapper + Microsoft.Data.SqlClient。**课程评分重点是数据库设计。Dapper 能让 SQL、视图、存储过程、事务和约束在代码与报告中清晰可见，减少自动迁移掩盖设计的问题。实体映射保持在 Infrastructure 层，业务规则由 Application 层编排。

## ADR-004 版本和依赖

**决定：目标框架使用已安装的 .NET 8 LTS。**项目锁定 SDK 版本，前端锁定 `package.json` 和 lockfile；升级 SDK 或数据库版本必须通过构建、迁移和验收测试。

## ADR-005 业务写入边界

**决定：业务写操作以数据库事务过程为核心。**API 可以做参数和权限校验，但预约审核、锚地事件、计划确认、版本切换和实际执行必须由事务服务完成。这样老师可以在数据库工具中直接检查多表原子性和约束效果。
