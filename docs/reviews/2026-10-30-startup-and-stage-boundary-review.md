# 启动体验与阶段边界 review

## 本次范围

本次只处理启动时的可解释性和阶段计划边界，不进入业务 API 或前端视觉重构：

- 后端根路径不再返回无说明的 404；
- 前端 TypeScript 配置移除已弃用的 `baseUrl` 用法；
- 运行手册说明后端端口、前端端口和表级触发器的查看方式；
- 阶段 1 明确为数据库业务对象收尾，阶段 2 才开始只读 API 联调；
- 记录当前前端仍是演示原型，导航按钮不代表已完成业务功能。

## 修改结果

### 后端启动地址

`GET /` 返回服务名、运行状态、环境和健康检查地址。`GET /api/health` 保持原有健康接口。这样打开 `http://localhost:5180/` 时可以确认 API 已启动；前端页面仍访问 `http://localhost:5173/`。

### 前端 TypeScript

保留路径别名，但将映射值改为 `./src/*`，不再使用已弃用的 `baseUrl`。当前代码没有因为此调整改变页面行为。

### 阶段顺序

本轮重新确认：

1. 阶段 1：补齐队列调序、泊位计划版本、审计、最小权限、演示数据和负向测试；
2. 阶段 2：通过 Dapper 和 ASP.NET Core 接入只读预约查询，再连接 Vue 页面。

阶段 1 未完成前，不同时开发大批 API 和页面，避免数据库基线与页面 Mock 数据并行扩张。

## 验证记录

| 检查 | 结果 |
|---|---|
| `dotnet build PortQueue.sln --configuration Release` | 通过，0 警告，0 错误 |
| `dotnet test PortQueue.sln --configuration Release --no-restore` | 通过，2/2 |
| `npm --prefix web/port-queue-web run build` | 通过，类型检查和 Vite 构建均通过 |
| `pnpm --dir web/port-queue-web run build` | 未执行成功，本机未安装 `pnpm`；使用手册允许的 npm 等价命令完成验证 |

## 遗留项

- 当前 `App.vue` 仍使用演示数据，属于阶段 0 原型边界；阶段 2 才替换预约列表为真实数据库数据。
- Vite 报告产物大于 500 kB，主要来自当前前端依赖；待真实页面和路由确定后再评估按页面拆分加载。
- `Programmability > Database Triggers` 为空仍是 SQL Server 扩展的对象分类显示差异，6 个业务触发器通过所属表和 `sys.triggers` 查询确认，不新增无业务意义的数据库级触发器。
