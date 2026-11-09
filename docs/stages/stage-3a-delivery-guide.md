# 阶段 3A 交付说明：身份权限与全港概要

日期：2026-11-09。

## 1. 本轮解决什么

旧船舶轮播从最近五条预约取数据，草稿、待审核、驳回和撤销占据首批时看不到已批准；这不代表数据库缺少批准记录。扩大轮播条数仍会重复预约页职责，所以移除单船焦点、审核摘要和近期清单。

总览现在回答“全港各业务的当前规模和执行概况”；进港预约回答“查哪条申请、资料和审核结果是什么”。总览不再主动调用预约清单 API。状态分布覆盖全部六种状态，点击已批准进入全部已批准预约，清除旧关键词；从搜索候选或动态选择详情仍保留清单原条件。

本机现有场景：53 条预约，草稿 8、待审核 9、已批准 10、已完成 10、已驳回 8、已撤销 8；锚地在场 10，待靠泊计划 4。作业概要单列有效队列等待/已分配、当前计划草拟/执行中和累计进港/靠泊/离泊。数值全部查询，不为填空制造非零数据。统计报表专页、AIS 与其他业务操作页仍未接入。

## 2. 第三阶段已实现的边界

本批为 3A：标准 Cookie 登录、退出、真实姓名与角色、防伪造令牌、来源地址登录限速、账号/角色启停复核、密码重置使旧会话失效、本机账号维护。无 JWT、Redis、第三方身份服务或新增权限表。

| 角色 | 登记/提交策略 | 审核策略 |
|---|---|---|
| ADMIN 管理员 | 允许 | 允许 |
| DISPATCHER 调度员 | 允许 | 拒绝 |
| REVIEWER 审核员 | 拒绝 | 允许 |
| RECORDER 记录员 | 拒绝 | 拒绝 |

策略已经实现并经过 HTTP 管道验证；预约写端点与按钮尚未开放，不能把策略存在说成登记/审核功能已完成。测试用授权探针仅存在测试程序集，不部署到产品。当前只读功能继续允许匿名使用。

## 3. 创建自己的登录账号

现有场景里的操作人仅供历史业务引用，其随机占位不是密码。本轮未给这些账号设置默认密码。浏览器登录验收在独立随机数据库创建临时账号，结束后测试库已清理；截图中的验收账号不是可直接登录开发库的账号。

在 VS Code 的仓库根目录终端执行以下命令。程序会在终端提示输入两次密码，输入不回显；密码为 12—128 字符，自己设置，不要写入命令行、SQL、截图或 Git。

```powershell
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --configuration Release --no-build -- account create port.dispatcher "调度员" DISPATCHER
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --configuration Release --no-build -- account create port.reviewer "审核员" REVIEWER
```

两条命令逐条执行；每条完成两次输入后再执行下一条。然后打开 http://127.0.0.1:5173，点左下“登录”，输入刚创建的用户名与密码。窄屏先展开导航。账号命令执行后直接退出，不启动额外 Web 服务；不需要停掉已运行 API。

账号维护示例：

```powershell
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --configuration Release --no-build -- account reset-password port.reviewer
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --configuration Release --no-build -- account disable port.reviewer
dotnet run --project src/PortQueue.Api/PortQueue.Api.csproj --configuration Release --no-build -- account enable port.reviewer
```

首次其他机器使用前需构建 Release，并用管理身份执行 `database/09_permissions.sql`。本机本轮已经执行。不要重新运行建表脚本或重新灌入业务样例。管理命令使用同一配置下的数据库连接，需要数据库管理身份；部署时应用身份只读取认证列，仍不能直接写账号表。

维护事务追加账号审计，不记录密码或哈希。当前引导方式以目标账号归属审计，JSON 同时保存数据库实际执行身份 ORIGINAL_LOGIN()；这明确区别于以后由登录管理员发起的账号管理页面。

## 4. 输入、处理与输出

| 操作 | 输入 | 处理 | 输出 |
|---|---|---|---|
| 总览 | GET /api/overview?eventsPage=1 | 单 SELECT 读取全量状态、队列/计划视图和事件 | 全港概要与分页事件 |
| 当前会话 | GET /api/session | 有 Cookie 时复核账号、角色状态及密码指纹 | 公开身份/权限或 null，防伪造令牌 |
| 登录 | POST /api/session/login：用户名、密码、X-CSRF-TOKEN | 校验令牌、限速、标准密码哈希和启用状态 | HttpOnly 会话 Cookie、姓名角色与新令牌 |
| 退出 | POST /api/session/logout：X-CSRF-TOKEN | 校验令牌，清除当前浏览器 Cookie | 无身份及新匿名令牌 |
| 后续审核 | 当前批次尚未开放 | 将复用审核策略和事务过程 | 在 3B 实现 |

Cookie 是非持久会话，绝对有效期八小时；每次认证请求检查账号/角色，不依赖旧 Cookie 中的角色。生产环境使用 Secure Cookie 与 HTTPS；本机 Development 允许现有 HTTP 同源代理。防伪造令牌仅在前端内存中保存，密码不写 localStorage/sessionStorage。

连续每来源地址每分钟最多五次登录尝试，第六次 429；错误密码、未知、锁定或停用账号统一拒绝。缺少或错误防伪造令牌 400；未登录受保护操作 401、错误角色 403。角色变化影响下一次后端请求；账号区重新读取会话时同步显示。

## 5. 文件职责

- `database/queries/overview.sql`：唯一全港统计 SQL，不重复存储派生数量。
- `database/queries/account.sql`：可信服务端认证列查询；哈希不序列化给浏览器。
- `database/queries/manage-account.sql`：本机账号维护和脱敏审计的原子事务。
- `src/PortQueue.Api/Security/SessionSecurity.cs`：Cookie、权限策略、密码验证、账号状态复核、限速。
- `src/PortQueue.Api/Controllers/SessionController.cs`：会话 HTTP 输入输出和防伪造校验。
- `src/PortQueue.Api/Commands/AccountCommand.cs`：本机隐藏输入与受控维护命令。
- `src/PortQueue.Application/Accounts/Account.cs` 与 Infrastructure/Accounts：账号契约和 Dapper 实现。
- `web/port-queue-web/src/components/PortOverview.vue`、`PortOperationsSummary.vue`：全港状态分布及作业概要。
- `SessionAccount.vue`、`composables/useSession.ts`：会话交互、错误恢复与登录焦点；不承担后端授权。
- `tests/PortQueue.Api.Tests/Security/SessionTests.cs`：真实 SQL + HTTP 认证和权限负向验证。
- `web/port-queue-web/tests/session.spec.ts`：随机测试库、真实独立 API 与浏览器登录验收；不修改开发库账号。

## 6. 验证结果与范围

本轮 Release 后端构建零警告零错误，59 项后端测试和 19 个 SQL 文件解析通过；前端 TypeScript/Vite 构建通过，最终完整浏览器回归 24 项全部通过。浏览器覆盖全部状态入口、既有检索分页及错误恢复、真实登录/刷新/退出、停用账号失效和 320/390px 窄屏。独立界面审查结论为 `ship`，没有本轮交付阻断问题。

验证采用随机独立测试库，不改变开发库业务数据或预设登录密码。未测试真实手机软键盘、Safari/Firefox、读屏器或生产多实例；本轮不包含审核/登记写端点。具体证据、截图与运行边界见 [阶段 3A review](../reviews/2026-11-09-stage3a-review.md)。

## 7. 下一批：3B 审核

先补全审核需要的申报资料，再接待审核记录的批准/驳回接口；后端从会话取审核人，数据库再次检查角色和当前状态。验收状态、审核人、数据库时间、一次审计一致，重复/并发审核最多一个成功。最后在已有详情区加入真实操作和刷新，不新增重复页面。3B 收口后才做 3C 登记与提交。
