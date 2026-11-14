# M3 第一步：ETA 报送、处理与回查

本批依据中期后继续实施计划，只完成服务端纵向用例。报送人仍是内部港区现场人员、调度或管理员；记录代表核实中的到达信息，不是船方自助预约，也不自动修改正式计划。Android 输入表单、Room 类型隔离和手机联调在下一批实现。

## 契约与状态

`POST /api/mobile/reports/eta` 接受 JSON `requestId`（非空 UUID）、`appointmentId`（正整数的十进制字符串）、`proposedEta`（ISO 8601，必须带 Z 或数值时区）、`description`（去首尾空白后1–500字）。拒绝额外身份等字段。沿用真实会话 Cookie 和 `X-CSRF-TOKEN`；ADMIN、DISPATCHER、RECORDER可提交，ADMIN、DISPATCHER可处理。

首次保存201；同用户同requestId、规范内容相同返回200及原记录；不同内容409 `REPORT_CONFLICT`。时间在服务端统一为UTC，数据库对首次提交验证 `现在 < proposedEta <= 现在+30天`，错误400 `INVALID_ETA`。时区表示不同但指向同一时刻视为同一内容；不带时区的时间被拒绝。只有APPROVED预约且航次为PLANNED/IN_PROGRESS可首次报送。重放先于时间/航次资格检查，避免已提交请求因时间流逝而无法核对。

响应复用报送详情，`reportType=ETA`、`category=null`、`photo=null`，新增 `proposedEta`；照片报送的该字段为null。自身清单、全域清单、detail及by-request均保留既有可见性边界。通用decision入口仍要求处理意见与Base64 rowversion；过期ETA接受返回409 `ETA_EXPIRED`且不写处理状态/审计，退回仍允许。重复处理或旧版本409。

例如在2026-11-14提交`proposedEta="2026-11-15T16:30:00+08:00"`及原因“引航协调后预计抵达”，先得到201、SUBMITTED和UTC时间`2026-11-15T08:30:00+00:00`。调度用原version提交ACCEPTED及意见后，原账号按requestId读到ACCEPTED、同一proposedEta和处理人/时间/意见；overview的latestAcceptedEta指向该记录，正式计划字段保持原值。此示例日期只解释时区，实际提交仍需满足当时的未来30天窗口。

overview的`latestAcceptedEta`是同一port_call已接收ETA，按`processed_at DESC, report_id DESC`取一条，返回reportId/proposedEta/processedAt。待处理和退回记录不影响该字段；已接收但后来过期的记录仍作为有时间标记的历史反馈显示。它与原航次plannedArrivalAt、泊位publishedPlan分别表达。

## SQL 增量及事务

沿用M2的mobile_report.proposed_eta，不新增表，不改核心21表。新增`mobile/05_eta.sql`：受控`usp_mobile_submit_eta`、带ETA过期接受约束的`usp_mobile_decide`、已接收ETA查询索引及新增过程EXECUTE授权。保留schema范围直接写DENY与现有列读取授权。首次提交、处理及audit_log追加分别在同一事务完成；提交按用户/requestId事务级applock和唯一键串行化，SHA256摘要比较规范内容。

先执行M2，再显式执行M3增量；升级不自动初始化、删除或重建库，不更改运行中API。`deploy-mobile-m3.ps1 -PlanOnly`只列出增量与冒烟检查，真正执行仅允许PortQueueMobileDev；脚本也允许既有隔离fixture命名的测试库。重放M2旧版过程后须重新应用M3，不能将旧版本过程当最新版本部署。

## 验收

新增实际API及SQL失败测试后实现：提交→处理→原账号回查、并发同请求只有一行和一次提交审计、时区等价重放、内容冲突、时间及元数据错误、Cookie/CSRF/角色/所有权、SQL直接调用仍重验、过期接受失败而退回成功、按处理时间和ID排序、另一到访不串数据、审计失败回滚。正式预约、航次、泊位计划及修订用完整行快照证明不变。

所有集成用例使用AppointmentDatabaseFixture随机PortQueueStage2Test_*数据库。测试输出使用`-p:BaseOutputPath=bin/M3/`，避免覆盖既有5181进程占用的Release文件。本批不部署PortQueueMobileDev、不重启服务、不修改原冻结仓库。实际红绿结果与完整回归记录到review。
