# 阶段 3C 独立视觉收尾审查

## 1. Disposition

**ship（定向 verdict，仅覆盖 F1–F3）**。首轮 disposition 为 fix；单批修正后，独立复核将三项全部评为 **resolved**，本次清单无 partial 或 unresolved 残余项。此结论关闭下面的三项发现，不扩大为整站或全部动态状态的全面通过。

## 2. Evidence / scope

- 审查依据：用户最新五张反馈图优先于旧版 DESIGN.md；总览主图层级、亮蓝柱条、被拒绝的旧作业概要与旧标题、拥挤的预约入口均逐项对照。保留既定侧栏、工具区、船舶图像、玻璃材质与中文业务语言；无新增批准设计稿，本轮属于既定视觉系统的代码扩展。
- 已打开并确认全部 12 张当前截图有效：`.impeccable/review/stage3c-overview-{1440,2470,973,390,320}.png`、`stage3c-drawer-desktop.png`、`stage3c-drawer-mobile.png`、`stage3c-drawer-mobile-bottom.png`、`stage3c-{queue,plans,events,workflow}.png`。总览为完整页面，抽屉为首屏及明确下段，业务页为内部滚动区首屏；没有空白或错位截图，因此无需 recapture disposition。
- 静态阅读 PortOverview、OperationDistribution、AppointmentRegistration、ReferenceSelect、BusinessRecords 及 business.css、registration.css，并参考现有 DESIGN.md / PRODUCT.md。遵循 Impeccable `new-work.md` 收尾契约及 `craft-floor.md`，用户固定风格优先。
- 主线程提供的验证结果为后端 97 项、SQL 59 项、Playwright 33 项通过；detector 为 190 条 advisory（98 字体、77 配色、15 圆角），无非 advisory。新亮蓝配色属于明确用户要求，不能按旧配色文档机械回退。
- 定向复核重新打开修正后的 `stage3c-drawer-mobile.png`、`stage3c-drawer-mobile-bottom.png`、`stage3c-drawer-desktop.png` 与 `stage3c-overview-2470.png`；同时读取修正后的抽屉宽度、宽屏字号规则、BusinessRecords 的 `lastRequest` / `retry` 实现及新增浏览器测试。其余同名截图矩阵由主线程回传，本次没有重新开展全面缺陷搜索。
- 已直接核对 `artifacts/stage3c-browser-results.json` 的 33 项通过、0 失败，以及 `artifacts/stage3c-retry-results.json` 的新增业务查询重试测试 1 项通过、0 失败（最后重跑 7.3 秒）。测试分别注入关键词查询与第 2 页请求的 503，确认重试仍携带原关键词或 `page=2`，并验证结果、导航往返、分页与关联详情保留。主线程报告前端构建与后端 97 项继续通过。

## 3. Findings（首轮记录，均已关闭）

| 编号 | 严重性 | 证据与影响 |
| --- | --- | --- |
| F1 | P2 | 390px 登记抽屉左侧标题、字段仅余约 5px，特殊要求焦点环接近画面左边约 1px；右侧留约 15px 外部底色。桌面有正常 32px 内距，移动 CSS 也声明 20px，说明是抽屉宽度与视口滚动条预留宽度冲突，不是设计意图。`width: min(680px,100vw)` / `max-width:100vw` 在根页面 `scrollbar-gutter:stable` 下使移动抽屉向左超出可用区域，削弱字段与焦点的完整边界。 |
| F2 | P2 | 2470px 总览预约分布的中文状态标签与计数同样大，压弱标题、数值和标签的层级；1440px 没有此问题。主线程提交的准确代码证据是 styles.css 约 451 行残留 `.state-distribution .status-text,`，使后接的 `.state-distribution strong { font-size:22px; }` 同时命中标签。既定宽屏状态入口标签为 16px，数值可保留 22px。 |
| F3 | P2 | BusinessRecords.vue 的 `load(next = page.value, query = committed.value)` 只在成功后记录条件；查询表单传入新关键词失败后，“重新读取”调用无参数 `load()`，会请求上一成功查询而不是本次失败查询。用户搜索“新润”失败后重试可能得到全部记录或旧关键词结果，错误恢复改变了用户意图。 |

当前截图中无需改动的已满足项：预约分布与作业柱条采用明亮蓝色，图例对应六个状态；“青岛港 / CNTAO / 中国”及设施数量建立清楚主层级；四个业务入口已有按钮语义与真实落地页；“查看全部预约”恢复足够底部留白；登记使用明确字段分组、固定底部操作、移动单列以及可见焦点。此结论不等于所有动态状态均已视觉验收。

## 4. Concrete corrections / verdict

| 编号 | 评分 | 已落实的修正与复核证据 |
| --- | --- | --- |
| F1 | **resolved** | registration.css 改为 `width:min(680px,100%)` / `max-width:100%`，宽度按实际可用包含块计算。390px 首屏标题与字段左侧恢复 20px，下段焦点环左侧约 16px，两侧边界完整；桌面抽屉仍保留原宽度与 32px 内距。滚动条的 15px 预留属于外部可用宽差异，不再把表单挤出左边。 |
| F2 | **resolved** | 已直接读取独立的 `.state-distribution .status-text { font-size:16px; }` 与 `.state-distribution strong { font-size:22px; }`；2470px 新截图确认状态名称退回标签层级，数值更突出，原残留逗号选择器已移除。 |
| F3 | **resolved** | `load()` 在发起请求时保存 `lastRequest = { page:next, keyword:query }`；错误入口调用 `retry()`，显式使用该请求，而 `committed` 仍只在成功后更新。新增测试同时通过关键词查询 503 → 原关键词重试、翻页 503 → `page=2` 重试，并验证导航、分页与关联详情保留。实现、测试源码和最终 1/1 通过报告均已核对。 |

单批修正已完成，原三项清单全部关闭。本次不要求第二轮修正或第二次 detector。

## 5. Verification limits

本审查未启动浏览器、未修改实现代码、未重跑测试，测试结论来自已读取的报告与测试源文件。截图能证明当前可见布局与内容层级；真实认证、数据写入、并发、完整键盘路径和辅助技术体验不能仅凭截图宣称通过。390px 登记抽屉具有独立截图，320px 抽屉没有单独回传截图；本次以修正后的包含块宽度规则确认其不会重复原有 `100vw` 问题，不声称完成 320px 抽屉的独立视觉验收。业务查询页窄屏截图亦不属于本次范围。

终端读取曾间歇出现 `helper_unknown_error: setup refresh had errors`，之后已成功读取三项修正代码、结果报告及测试源码，不影响最终定向结论。主线程报告修正后的验证曾因 Vite 退出中断，恢复服务后的独立重跑已通过；中断本身不计为通过。

文档收尾应记录最新亮蓝图形、作业分布、真实业务入口与 3C 开放状态；该工作在主线程完成，不属于本次三项视觉/恢复缺陷的残余项。
