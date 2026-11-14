# 移动扩展数据库

M1新增查询，M2新增两张独立移动报送表。核心SQL仍由上一级database目录维护。移动开发实例为 `(localdb)\PortQueueDev / PortQueueMobileDev`，与冻结演示库PortQueue分开。

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

## M2显式增量部署

先初始化M1及账号，再执行 `pwsh -File database/deploy-mobile-m2.ps1 -PlanOnly` 预览，随后 `pwsh -File database/deploy-mobile-m2.ps1 -IncludeScenarios`。该入口仅接受PortQueueMobileDev，无重建/删除逻辑，脚本可重复执行；不在原部署入口偷偷升级。01建表、02受控过程/权限、03可选两次同船到访无安排样本、04只读一致性/最小权限冒烟。有队列与已发布计划沿用原13_seed_port_scenarios.sql稳定逻辑编号查找，不写死自增ID。

API仍需 `Mobile:Enabled=true`；关闭时所有移动路由不存在。启动脚本默认Release，照片目录固定在被忽略的 `artifacts/mobile-private-photos`，仅受权GET能读取，不配置静态文件托管。维护使用 `pwsh -File tools/cleanup-mobile-photos.ps1 -PhotoDirectory <绝对照片目录>` 默认预览；核对后加 `-Apply` 才删除数据库未引用且至少24小时的GUID照片/临时文件，数据库核对失败不删。

JPEG使用纯托管StbImageSharp固定2.30.16，NuGet许可Unlicense OR MIT，支持net8.0无需平台DLL及额外依赖。先解析JPEG尺寸并限制1600，再真正解码全部RGB数据。2026-11-11实际NuGet审计无该包公告；既有SqlClient传递System.Security.Cryptography.Pkcs8.0.1有公告，未扩大本轮升级范围。ImageSharp3.1.12有漏洞，修复线3.2.0实际Release构建要求许可证密钥，因此未采用且未绕过许可。

## M3 ETA 显式增量

完成M2后，先预览 `pwsh -File database/deploy-mobile-m3.ps1 -PlanOnly`，需要升级时再显式执行同脚本。05增加ETA提交过程、升级通用处理过程的过期接受校验、增加已接收ETA查询索引；04/06只读检查一致性与最小权限。无新表、无初始化/清库/覆盖数据；数据库角色仅新增窄过程EXECUTE。已有部署脚本不会暗中升级，M3 API的overview要求M2两表已存在。重新应用M2旧过程后必须顺序补上M3。

ETA输入、UTC与未来30天、requestId重放、处理和overview口径见 `docs/stages/mobile-m3-eta.md`。本批只在随机测试库验证，未对PortQueueMobileDev执行此升级，也未重启当前API；手机接入和部署另批验收。
