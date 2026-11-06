# 阶段 2 预约查询真实闭环 review

## Review 范围

本次 review 覆盖 `database/queries/appointments.sql`、Dapper 查询实现、ASP.NET Core 查询接口、Vue 预约列表及本地运行文档。目标是把一条只读业务链路打通：SQL Server → Dapper → API → Vue。阶段 3 的预约登记、审核写入、登录和角色授权不在本次范围内。

## 交付结果

- `GET /api/appointments` 使用维护在 `database/queries/appointments.sql` 的参数化 SQL；状态和关键词先校验、归一化，关键词按字面匹配，不开放动态排序字段。
- 一次查询同时返回筛选结果总数和当前页，固定按申请到港时间倒序、预约主键倒序；空页仍返回真实总数。
- 主键以字符串传输，避免 `bigint` 超出浏览器安全整数范围；时间以带零时区的 ISO 8601 返回，页面固定按北京时间显示。
- 审核人采用左连接，未审核预约保留且审核字段为 `null`；数据库不可用返回 503，参数错误返回 400，响应不泄露 SQL、连接字符串或堆栈。
- 前端默认进入真实预约页面，筛选、重置、分页、刷新、加载、空结果和错误恢复均有明确状态。旧总览保留为单独标记的静态原型，不作为本阶段真实功能。
- Vite 开发服务器和 preview 均把 `/api` 代理到 5180；API 与页面没有加入 Mock 回退。

独立 UI finish review 以源码范围检查了真实查询、请求取消、响应校验、状态页面、表格语义和窄屏策略，结论为 `ship with polish`。根据 review 已补充“总览原型（静态）”入口名称、到港/离港列内标签、窄屏横向滚动提示以及 44px 查询控件；静态原型入口按阶段范围保留，并在页面和文档中明确其不是真实业务功能。

## 自动化与静态验证

| 检查 | 结果 |
|---|---|
| `dotnet test PortQueue.sln --configuration Release --no-restore` | 36 通过，0 失败，0 跳过；其中 22 项使用随机独立 SQL Server 测试库验证真实查询，12 项验证 HTTP 参数和故障契约，2 项健康接口回归 |
| `python tests/database/run_stage1.py` | 59 个数据库阶段 1 用例通过，4 个双连接并发场景通过，测试库已删除 |
| `tests/database/check-sql.ps1` | 13 个 SQL 文件通过 SQL Server 语法解析，包括预约查询 SQL |
| `npm --prefix web/port-queue-web run build` | `vue-tsc --noEmit` 和 Vite production build 通过 |
| 本地 API 成功请求 | `/api/appointments?pageSize=2` 返回 2 条、总数 5 |
| 本地 Vite 代理 | `/api/appointments?status=PENDING` 返回 1 条真实记录 |
| API 非法参数 | `page=0` 返回 400；自动化测试确认稳定 `INVALID_QUERY` 契约 |
| API 数据库故障 | 自动化测试以不可达 SQL 端点确认 503、`DATABASE_UNAVAILABLE` 和无敏感信息 |

开发库未重建，当前演示数据仍为 5 条 `PQ-DEMO` 预约：2 条已完成、2 条已批准、1 条待审核。查询过程只读，不会改变这些记录。

## Review 结论

本阶段后端和数据链路达到验收条件，可以用于中期答辩演示。数据库对象数量和阶段 1 业务规则保持不变，真实页面已经有可复现的查询输入和结果输出。

浏览器自动化工具在本机启动时连续出现运行环境的 `helper_unknown_error`，因此本次没有生成浏览器截图，也没有把键盘焦点、窄屏滚动和点击流程写成已经自动验证。页面构建、代理和接口结果已验证；中期冻结前需在 VS Code 浏览器或本机常用浏览器中人工完成一次桌面/窄屏检查并保存截图。

## 保留边界

- 当前 API 使用本机 Windows 身份连接 LocalDB，尚未绑定实际登录身份和业务角色；数据库最小权限已在阶段 1 的独立用户测试中验证。
- 健康接口表示进程存活，不代表数据库可用；预约查询失败会单独返回 503。
- 数据为课程演示合成数据，不是实时 AIS、地图或传感器数据。
- 下一阶段应先完成中期截图、演练和 PPT，再进入预约登记与审核写入；不在本阶段扩展菜单或数据库关系。
