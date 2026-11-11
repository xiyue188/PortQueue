# M2 后端实现与验证

本次新增现场照片报送、原请求核对、自身/全域清单、受控照片读取及一次性处理。DTO严格采用移动M2统一契约；原会话Cookie与CSRF、预约登记/审核权限不变；PHOTO之外ETA写入未开放。模块默认关闭时控制器整体移除，新增入口不存在。

数据库先设计并由父任务只读审核后显式部署PortQueueMobileDev；01增量两表、02两个窄事务过程及权限、03显式样本、04一致性/最小权限冒烟。schema-wide直接写DENY保留，应用仅SELECT两表与EXECUTE过程。用户/request事务锁、摘要比较与唯一约束共同处理重放；决定用rowversion条件更新与事务审计。原21表语义不变。

文件校验包含5MiB限制、JPEG头尾、尺寸限制与StbImageSharp2.30.16真实RGB解码；无额外平台DLL，NuGet许可Unlicense OR MIT。固定版本包无当前漏洞公告；既有SqlClient传递Pkcs8.0.1公告保留为原依赖风险。文件在回执前落盘；确认回滚补偿，提交ACK不明则独立连接核对，不删除可能已提交照片。维护脚本限定移动库对应照片目录，默认预览且仅处理24小时前无引用文件。

验证证据：首次新增HTTP测试在M1旧实现按预期失败（REVIEWER POST期望403、实际404）；最终 `dotnet test PortQueue.sln --configuration Release --no-restore --logger 'trx;LogFileName=backend-m2-final.trx' --results-directory artifacts/mobile-m2/test-results`：111通过、0失败、0跳过。包括同请求并发200/201且一行一文件、不同内容409、跨用户detail/photo/by-request404、无全域/处理权限403、并发处理200/409、非法图片不落盘、audit失败回滚与文件补偿、实际commit后ACK丢失仍可取回JPEG。可读JPEG头/SOS损坏样本明确经过解码失败分支返回415。

`pwsh -File tests/database/check-sql.ps1`全部SQL静态解析通过；`python tests/database/test_mobile_deployment.py`2项通过；M2增量部署重复执行通过、04最小权限冒烟通过；cleanup错配目录带Apply在访问数据库和删除前拒绝。只格式化本次C#，`git diff --check`核对补丁。

当前5181旧服务由父任务核对PID后重启，子任务未停止任何既有进程。重启后使用 `pwsh -File tools/start-mobile-api.ps1 -NoBuild`（默认Release），照片为 `artifacts/mobile-private-photos`。真实手机拍照、Android两账号界面处理和视觉验收归父任务联调，此报告不将它们标记为通过。独立只读复核指出提交ACK、解码异常及清理目录配对问题，均已在本批定向处理并留测试证据。
