# M3 ETA 服务端实现与验证

本批从`a9e8a1fec569e51de277900c1c4d1d83cdc8d01d`开始，只修改既有隔离`feature/mobile-m2`工作区。新增ETA报送、原请求核对、调度处理和原账号回查；未修改Android、未部署PortQueueMobileDev、未重启已有5181服务、未改原冻结工作区。报送身份是内部现场人员/调度，不冒充船方自助申请。

## 实现与边界

`POST /api/mobile/reports/eta`接收requestId、appointmentId、proposedEta、description。Cookie/CSRF和真实角色沿用现有认证；ADMIN/DISPATCHER/RECORDER提交，ADMIN/DISPATCHER处理，其他账号不能读取他人报送。未知字段、空requestId、非法预约、空白/超长说明、无时区或错误时间返回400，未认证401、无角色403。首次201，同规范内容重放200，变化409；+08:00和Z的同一时刻规范为UTC后得到相同摘要。

SQL先于后端接口实现。M2已有proposed_eta字段，本批未加表或修改核心21表；`05_eta.sql`在一个迁移事务中增加受控提交过程、更新通用decision、增加已接收ETA过滤索引和仅该过程EXECUTE权限。首次提交在SQL重验启用用户/角色、预约APPROVED及航次PLANNED/IN_PROGRESS、未来30天窗口；用户/request事务锁、唯一约束和规范SHA256保证幂等。回执读取及audit追加与写入同事务。重放先于移动时间窗口与航次资格判断；连接结果未知时客户端继续用原requestId回查/重试，不能另造新请求。

decision保留rowversion和一次性处理，先检查状态/版本，再判定ETA是否已过期。过期ACCEPTED返回409 ETA_EXPIRED，不改变版本或审计；REJECTED允许。照片处理路径经原M2测试回归。详情和清单新增proposedEta，PHOTO为null；overview同一port_call只取ACCEPTED ETA，按processed_at/report_id倒序。待处理、退回、别的到访不覆盖；接受后随时间自然过期的记录保留为带处理时间的历史信息。

独立`deploy-mobile-m3.ps1`只允许PortQueueMobileDev，PlanOnly不连接数据库；迁移前要求M2已存在，不重建或覆盖业务数据。04/06冒烟验证照片一致性、受控EXECUTE、直接写DENY及读取权限。M3版本overview要求M2 schema；重跑旧M2过程后必须按版本顺序补M3。

## 实际验证

- 先新增HTTP与SQL失败用例。初次常规Release构建被现有API占用DLL阻止；改用独立`bin/M3/`输出。普通沙箱不能访问用户LocalDB注册表，随后以获准执行方式运行随机隔离测试库，未把环境错误算作红测证据。
- `eta-red.log`/`eta-red.trx`：12项失败；HTTP旧版本实际405，SQL实际2812（过程不存在）。其中主流程快照夹具一处SQL保留字别名修正后单独重跑，`eta-flow-red.log`/`eta-flow-red.trx`确认期望201、实际405；这些是实现前的实际失败证据。
- 实现后`mobile-green.log`/`mobile-green.trx`首轮25项全部通过。随后补迁移重复应用/最小权限成功写入和decision审计回滚检查。
- 最终命令：`dotnet test PortQueue.sln --configuration Release --no-restore -p:BaseOutputPath=bin/M3/ --logger 'trx;LogFileName=backend-m3-final.trx' --results-directory artifacts/mobile-m3/test-results`；125通过、0失败、0跳过，包含完整Mobile 27项（ETA新增14项）。输出`artifacts/mobile-m3/backend-m3-final.log`与TRX。
- `pwsh -NoProfile -File tests/database/check-sql.ps1`全部SQL解析通过，包括05/06及overview；输出`artifacts/mobile-m3/sql-static.log`。`python tests/database/test_mobile_deployment.py`3项通过，覆盖M3的目标库拒绝与PlanOnly不改SQL；输出`deployment-tests.log`。
- 同request并发返回200/201且仅一条记录、一条提交审计；ETA/原因/预约变化409。UTC输出保持datetime2(7)精度，直调过程也拒绝过期、超过30天和REVIEWER；30天边界提交与保存后过期/关闭航次的重放路径在最小权限用户下验证。
- 真实Cookie完成提交→处理→by-request及列表回查；正式预约、航次、泊位计划和修订以完整行JSON快照逐字比较保持不变。overview同时验证处理顺序、同处理时间下ID排序、排除退回/待处理及同船其他到访。
- 过期接受保持SUBMITTED/原rowversion/原审计数量，退回成功，旧版本再处理409。注入audit触发器失败分别证明提交无残留、decision不留下处理状态；触发器只在随机测试库出现并在finally移除。

## 审查结论与待验收

本批未扩大核心数据库写授权。最小权限用户实际执行增量提交并重放，04/06断言mobile_report的INSERT/UPDATE/DELETE及audit_log INSERT仍拒绝。报送实体不赋值预约/航次/泊位字段；完整行快照对此提供行为证据。SQL迁移重复执行已在隔离fixture验证，实际开发库升级和部署后HTTP烟测尚未执行。

当前服务继续是M2。Android ETA表单、记录类型区分、Room迁移、模拟器双账号及真机验收仍待下一批；现有照片、相机及定位的旧证据不算本批ETA手机验收。没有推送或部署。review记载的测试日志及TRX位于被Git忽略的artifacts目录，提交保留源码、测试和此证据索引。
