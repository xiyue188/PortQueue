# 独立课堂展示环境（2026-11-22）

用户授权F3→F4正式展示包与干净场景→F6。教师允许电脑辅助，不要求公网部署。

## 边界

- 独立库 `PortQueueMobileShowcase`，API仅回环5182，照片 `artifacts/showcase/photos`；开发库/5181和冻结主线/5180不切换、不清理。
- 初始化由 `database/deploy-mobile-showcase.ps1` 复用权威SQL。核心21表冒烟在移动扩展前运行，移动扩展后执行自己的完整性/最小权限检查。
- 默认场景日2026-11-22。仅新实例的原始种子时间字面量整体平移；保留预约号/MMSI用于Android地图匹配，不修改已有库的历史。已有库拒绝重跑；失败恢复须指定已检查步骤并保持原日期，已有报送或完成manifest不能续灌。
- 移动SQL只额外允许这个明确库名，不放宽任意数据库。旧M2测试场景不加载；测试文字不在客户端隐藏。

## 场景

48项合成任务，24个既有地图身份；地图为声明的人工场景，非实时AIS。

| 用途 | 预约 | 事实 |
|---|---|---|
| 主线 | PQ202611-1003，海岚星河 1 | 实际入锚、已分配、已发布版本1和版本2；前序交接导致窗口顺延半小时 |
| 等候 | PQ202611-1009，东泽星河 2 | 实际入锚、WAITING，无已发布泊位 |
| 直接安排 | PQ202611-1002，海岚明珠 1 | 真实审核通过，直接创建确认泊位，无候泊队列 |
| 历史 | PQ202611-1006 | 已执行靠离泊事实，不制造当前船位 |
| 取消 | PQ202611-1005 | 终止状态，仅查询 |

`tools/prepare-showcase-reports.ps1`只向回环5182提交，登录使用Development服务端预设。固定请求ID先查回执，仅不存在才提交；接收/退回经真实调度API。当前主任务3条ETA为ACCEPTED、REJECTED、SUBMITTED各1条。照片必须真实采集后补，不生成虚假照片。

## 操作

1. 首次 `./database/deploy-mobile-showcase.ps1 -PlanOnly`，核对后去掉PlanOnly。已有实例不得重建。
2. `./tools/start-mobile-api.ps1 -Showcase -NoBuild -SeedDemoAccounts`仅初始化公开开发账号；日常去掉SeedDemoAccounts。
3. Android仓库`tools/start-showcase.ps1`启动/核对5182并建立ADB5182转发；与debug5181并存。
4. `./tools/prepare-showcase-reports.ps1`准备/回查三条ETA；已有回执重跑不重复提交。
5. 停止确认为本轮展示API的5182进程后，`./tools/backup-showcase.ps1`成对备份SQL和照片，恢复到新名称的隔离库，核对数量与约束。保留恢复证据，不覆盖或删除现有库。真实照片加入后须再验证图片文件与鉴权API读取。

场景日过期时不能悄悄平移现有记录。当前这批用于近期验收；正式演示日期确定后需另外批准/准备新实例，保留冻结备份。
