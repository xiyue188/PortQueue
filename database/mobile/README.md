# 移动扩展数据库

当前M1只新增查询，不新增业务表。核心SQL仍由上一级database目录维护。移动开发实例为 `(localdb)\PortQueueDev / PortQueueMobileDev`，与冻结演示库PortQueue分开。

```powershell
# 先预览生成计划，不连接数据库
pwsh -File database/deploy-mobile-dev.ps1 -PlanOnly
# 首次创建核心模型及合成业务场景
pwsh -File database/deploy-mobile-dev.ps1 -IncludeDemoData
# 初始化公开Development账号；已有账号不重置
pwsh -File tools/start-mobile-api.ps1 -SeedDemoAccounts
# 启动独立API，端口5181
pwsh -File tools/start-mobile-api.ps1
```

部署入口只接受PortQueueMobileDev，不接受PortQueue、master或任意名称。将权威SQL的固定库引用转换到被忽略的artifacts目录执行，不修改源SQL。已有目标库只验证，不自动覆盖过程、重置或补灌演示数据；首次创建时需要样例请显式加IncludeDemoData。失败时保留移动库供排查，没有自动删库逻辑。

M1查询使用既有 `09_permissions.sql` 的视图和列授权；集成测试以 `portqueue_probe` 验证，无需新增全表读取授权。后续M2新增两张报送表将另有版本化脚本，不能直接放宽原21表基线断言。LocalDB所有者是开发身份，部署生产时仍需映射最小权限数据库用户。
