# 2026-10-29 仓库与范围评审

## 评审范围

本次检查覆盖仓库初始化状态、数据库脚本、ASP.NET Core 骨架、Vue 原型、启动说明、忽略规则和课程设计中期风险。依据为 `AGENTS.md`、前四阶段定稿和数据库物理设计评审记录。

## 结论

当前项目没有出现“业务功能一次性开发过快”的情况。已完成的内容主要是数据库物理基线、可恢复脚本、后端最小骨架和前端演示原型；真实预约 API、前后端联调、队列调序、泊位计划页面和统计报表仍未实现。当前阶段适合建立 Git 可审查历史并进入单一业务闭环开发，不适合并行铺开全部模块。

## 已确认内容

- SQL Server 物理设计与前四阶段逻辑模型保持一致，已有 21 张表、3 个视图、核心过程、触发器和约束测试。
- ASP.NET Core API 可以提供健康接口，数据库连接工厂已经隔离到基础设施层。
- Vue 3 原型可以独立启动和构建，页面明确标注演示数据与“接口待联调”。
- `.gitignore` 已排除构建产物、依赖目录、本地配置、数据库文件和日志；未发现应提交的密码或密钥。
- GitHub 远程仓库为空，可以从当前 `main` 分支安全建立首次提交。

## 当前缺口

1. 后端还没有真实预约列表查询和预约审核接口。
2. 前端还没有从 API 读取预约数据，也没有真实写入数据库。
3. API 集成测试目前只覆盖健康接口；下一阶段要补成功和失败各一条预约场景。
4. SQL 仍需在每个新增业务阶段继续执行静态检查和真实实例冒烟测试。

## 中期检查判断

分析设计阶段已经完成，数据库核心实现已经建立，完整应用实现约处于 20%—30%。能否满足中期检查，取决于下一阶段是否打通“预约查询—预约审核”的真实数据库链路，并形成一个可在页面上重复演示的结果。仅展示当前 Mock 总览不能计入真实实现比例。

## 本轮验收标准

- 工作区按中文提交信息拆成规则基线、文档、数据库、代码骨架四个小提交。
- 每个提交只包含同一类职责，提交前通过 `git diff --cached --check`。
- `dotnet build`、`dotnet test` 和前端生产构建可重复执行。
- README、文件职责索引和启动手册明确说明当前 Mock 边界，不把原型描述成已联调系统。

## 实际验收结果

- 四个中文小提交已建立，`git diff --cached --check` 和提交后工作区检查通过。
- `dotnet build PortQueue.sln --configuration Release` 通过，0 个警告、0 个错误。
- `dotnet test PortQueue.sln --configuration Release --no-restore` 通过，健康接口测试 1/1。
- `pnpm --dir web/port-queue-web run build` 通过，Vue 类型检查和 Vite 生产构建均完成；Vite 对当前单页原型体积给出优化提示，但不影响本阶段验收。
- `database/06_smoke_tests.sql` 通过：21 张表、3 个视图、2 个过滤唯一索引。
- `database/07_business_smoke_test.sql` 通过：预约 `COMPLETED`、队列 `LEFT`、计划 `COMPLETED`、5 个事件、1 条队列视图记录、1 条当前计划记录。
- `database/08_constraint_smoke_tests.sql` 通过：错误字典子类型被拒绝；测试业务数据已回滚。
- GitHub 远程为空状态下首次推送成功，`main` 已跟踪 `origin/main`，工作区保持干净。
- 复核发现 `pnpm-workspace.yaml` 中 `esbuild` 构建许可是占位文本，已改为明确的 `true`；`pnpm --dir web/port-queue-web run build` 重新通过。Vite 仍提示当前原型单页包体较大，记录为后续真实页面拆分时的优化项。
- 本轮为源码、SQL 和前端配置补充模块级注释，并在 `docs/file-guide.md` 固化注释边界。JSON 和 lockfile 保持纯格式，不添加会破坏解析的伪注释。
- 清理了前端目录下重复的 `pnpm-lock.yaml`，工作区统一使用根目录 lockfile，避免依赖版本出现两份来源。
- 将本地 Development 环境的 HTTPS 重定向改为显式配置。临时端口 `5181` 启动验证输出 `Now listening` 和 `Application started`，不再出现 `Failed to determine the https port for redirect`；原端口 `5180` 被用户已启动的 API 占用，访问 `/api/health` 返回 `status: ok`。
- 一次并行运行 build/test 曾造成 .NET 输出文件占用，串行复跑后构建和测试均通过。后续验收命令按串行顺序执行，避免工具并发造成假失败。

## 下一阶段范围

只实现预约查询：数据库查询视图/SQL、后端 DTO 与 GET 接口、一个真实数据来源的前端列表，以及成功和数据库不可用两条测试。预约审核、锚地队列、泊位计划和报表继续留在后续阶段，待本阶段 review 通过后再开始。
