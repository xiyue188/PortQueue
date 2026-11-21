# F2 已发布安排历史

2026-11-21：仅隔离移动分支/PortQueueMobileDev。数据库新增只读过程与EXECUTE授权，无新表、无历史数据改写。

GET /api/mobile/appointments/{id}/changes?page=1&pageSize=20，Cookie认证，当前四种内部角色可读；不支持的角色无法合法登录，已有会话变为不支持角色后401；角色授权声明保留，未虚构第五角色403测试。Mobile关闭仍404。无任务404，无历史200空列表，非法页/ID400。

只有confirmed_at和confirmed_by均存在的版本进入历史；前值是同计划前一条已确认版本，不能把中间草稿公开。首次发布前值为空；当前计划状态不作为历史是否曾发布的依据。界面只读，正式当前安排继续看overview。此次范围只含泊位发布历史，调序历史暂未实现，不伪造合并时间线。

先运行database/deploy-mobile-f2.ps1 -PlanOnly，再显式执行部署；只接受PortQueueMobileDev。应用更新与数据库过程需配对。集成测试用随机隔离库；不得改冻结PortQueue实例。验收结果另见reviews。
