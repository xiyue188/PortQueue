# F2 发布历史核查

2026-11-21，隔离移动工作区，Android配对提交见移动仓库同日本批review。主课设仓库与5173/5180未修改。

## 业务效果与数据边界

手机可按本次预约读取曾发布的泊位安排及上一次发布的前值。初次发布无前值，中间未确认版本不会泄露；当前有效安排仍看overview，旧已结束安排可回查，不冒充当前指令。无新表、无核心业务数据修改，只有只读过程与窄EXECUTE授权。

## 已执行

- MobileHistoryTests在随机隔离SQL库，通过原create revision与confirm过程造第一版→草稿→新发布→后续草稿，验证排除草稿、真实前值、分页、无历史、404/400、UTC、portqueue_probe最小权限。
- 角色测试先用真实REVIEWER登录可读，再把该随机库账号改为不支持角色，旧会话被拒401。初版测试错误地预期未知角色可登录后403，依据SessionSecurity真实约束修正，未更改产品认证。
- MobileGateTests验证关闭404、开启未登录401；本批针对性4项通过。
- 移动API与SessionTests回归45项通过；Release构建0警告0错误。
- 对PortQueueMobileDev执行deploy-mobile-f2.ps1，仅创建/更新过程。后端旧5181 PID41108核实路径后替换，新PID46784，照片目录沿用artifacts/mobile-private-photos。新服务为用户日常联调保留，主课设服务未停止。
- 初次publish指定发布输出仍触发常规Release编译文件锁；随后按核实身份停止移动服务，再完成Release构建并由既有入口恢复。不是业务故障，也未批量清理dotnet/Studio。

## 未包含

无调序历史、无自动发布、无跨航次消息汇总、无干净正式场景导入、无HTTPS部署。Android设备实际结果另记，不以API通过代替。
