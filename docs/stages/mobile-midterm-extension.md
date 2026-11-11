# 移动端 M1：真实登录与单航次候泊查询

日期：2026-11-11。对应移动仓库 `Gangbotong` 的M1。实施在 `feature/mobile-m1` 隔离工作区，原main及未提交的答辩材料保留。

## 范围

沿用Cookie+CSRF真实会话，增加移动专用只读候泊详情与能力查询。默认 `Mobile:Enabled=false` 时不注册移动Controller，路由返回404。启用后仍须登录；M1尚无报送写入功能，能力标志为false，不把计划中的功能当作可用操作。

`GET /api/mobile/appointments/{appointmentId}/overview` 返回船名、本次航次、预约状态、队列与实际入离锚时间、当前已发布泊位安排及服务器快照时间。通过预约唯一关联本港到访；当前草稿和未确认候选不外露。已完成/取消计划不冒充当前有效安排，取消队列不自行推断离锚。

## 新增职责

- `database/deploy-mobile-dev.ps1`：将权威SQL生成到artifacts后部署，只接受PortQueueMobileDev，已有库只验证；`-PlanOnly`可离线检查目标映射。
- `database/queries/mobile-visit-overview.sql`：参数化一次查询，组合现有视图和业务关系。
- `src/PortQueue.Application/Mobile/`：手机所需DTO及查询接口。
- `src/PortQueue.Infrastructure/Mobile/`：Dapper读取，保留可空队列/计划。
- `src/PortQueue.Api/Mobile/`：移动路由、真实授权、模块开关。
- `tools/start-mobile-api.ps1`：专用开发库、Development账号和5181监听，不占用原5180。
- `tests/database/test_mobile_deployment.py` 与 `tests/PortQueue.Api.Tests/Mobile/`：目标库保护、真实会话/查询/最小权限/计划过滤测试。

## 输入、处理、输出

用户在手机输入船名 → 原预约分页接口返回候选 → 选中一条预约 → 移动详情接口按唯一到访关联队列和计划 → 手机将UTC时间转上海时间，按实际入离锚计算等待时长 → 刷新可重新获取服务器快照。未知/无安排明确显示，不能造一个泊位填充页面。

## 阶段门

部署保护测试与重复执行验证；既有及新增API/SQL测试；手机真实登录/搜索/查看/刷新/返回；Android构建、单测、Lint；review注明配对提交与证据。M1后再进行照片输入与反馈闭环，不在本批次新增报送表或正式事件写入。
