# 前端启动与阶段说明 review

日期：2026-11-07。范围：本机包管理器、前端启动说明和阶段 2 输入输出说明；不增加业务功能。

## 已确认的原因

1. 普通 Windows 用户/系统 PATH 中找不到 pnpm；此前工具会话额外提供的 pnpm 不等于 VS Code 终端已安装该工具。
2. npm 日志显示，在 pnpm 建立的 junction 依赖目录中执行 `npm --prefix web/port-queue-web install`，触发了 axios 的 `prepare: husky` 和 ECharts 的 prepare。该失败不是 Vue 源码或数据库错误。
3. 用户粘贴了 `>> npm ...`，PowerShell 把续行提示符作为命令执行，启动步骤未真正运行。
4. 当前 Vite 依赖仍可用；前端类型检查、临时启动和代理返回 200。修复无需删除依赖目录或重新创建数据库。

## 本轮计划与验收条件

- 将仓库声明的 pnpm 11.25.0 安装到普通 Windows PATH 已包含的 npm 全局目录；不依赖工具专用路径。
- 在项目根目录使用 `pnpm install --frozen-lockfile` 校验现有依赖，不改锁定版本。
- 在仅包含普通 Windows PATH 的进程中验证 pnpm 版本、前端构建和 5173 启动；确认代理查询到达 5180 并返回真实预约。
- 修正 README、runbook 和 VS Code 任务；明确安装、启动、停止和端口占用的区别。
- 固化每次交付的范围、文件、输入、处理、输出、数据库影响、验证和遗留项说明。
- 梳理教师通知中的表结构展示、数据展示、数据库 VIEW 和系统界面的区别，提供可执行的只读查询。

## 验证结果

| 检查 | 结果 |
|---|---|
| 普通 Windows PATH 与 Windows PowerShell | `pnpm --version` 为 11.25.0，解析到 `C:\npm-global\pnpm.ps1` / `pnpm.cmd` |
| Node.js 兼容性 | 22.20.0，满足 pnpm 11.25.0 的 Node.js >=22.13 要求 |
| 锁文件安装 | `pnpm.cmd install --frozen-lockfile` 成功，Already up to date，无锁文件和依赖声明变更 |
| 前端构建 | 普通 Windows PATH 下 `pnpm --dir web/port-queue-web run build` 成功，vue-tsc 与 Vite 均通过 |
| 原始启动命令 | 普通 Windows PATH 下 `pnpm --dir web/port-queue-web run dev` 成功，监听 127.0.0.1:5173 |
| 前端资源 | HTML 和实际 Vue 页面模块 HTTP 200 |
| 真实 API 代理 | 全部 5 条；待审核与关键词组合 1 条；每页 2 条的第 2 页为 2 条且总数 5；不存在关键词为 0 条 |
| VS Code 任务 | JSON 可解析，dev 入口唯一，dev/build 均固定仓库根目录和 Windows `.cmd` 命令 |
| 文档 SQL | `sp_help`、`sp_helpconstraint`、预约数据、关联数据、队列视图查询均在 PortQueue 成功执行，仅只读 |
| 数据库对象清单 | 21 表、3 视图、7 过程、8 表级触发器、45 个业务表索引 |
| Git diff | 无空白错误，业务源码、database 目录、package.json 与 pnpm-lock.yaml 均未改动 |

证据保存在本机忽略目录 `artifacts/2026-11-07-startup/`：`http-evidence.json`、`document-readonly-queries.sql` 和 `sql-evidence.txt`。长效结论保存在本 review 中，避免把生成日志提交进仓库。

## Review 结论与边界

- 撤回此前“没有 pnpm 时直接 npm install”的建议，统一安装工具和依赖管理方式；安装 pnpm 本身的 npm 全局命令与项目依赖安装有不同职责。
- 不安装无关的 Husky，不删除 node_modules，不修改执行策略或全局 PATH，不更新依赖版本。普通终端和工具会话的 PATH 差异已被验证并处理。
- SQL 数据展示与数据库 VIEW 概念在交付说明中分开，示例查询已执行；数据来自持久化合成样例，不冒充实际港口数据。
- 阶段 2 改为“代码实现与 HTTP 联调完成，浏览器验收待补”。HTTP 200 不等于浏览器点击、读屏或窄屏行为全部通过。
- 本轮没有修改业务代码，不重复运行上一轮完整 36 项后端测试和 59 项数据库用例；只验证受影响的工具链、启动、请求链路与文档示例。VS Code 任务命令已等价验证，任务面板点击未操作。
