# 总览与预约页面完成审查（2026-11-10）

## disposition

**ship**

本次为前次完整审查后的 **verdict pass**，范围限定为原 R1、R2 的修复评分及其直接引入的视觉/交互回归，不是另一次全项目无缺陷认证。两项均为 **resolved**；没有发现这批修复引入的实质回归，可以关闭这两项审查问题。

前次完整审查已确认的流程居中、四节点对齐、帆船轮廓、纵向窄屏布局、全部七个状态入口和既定冰蓝 TIDELINE 身份，在本轮同名重采截图中保持成立。

## brief matrix

| 原要求/检查范围 | 本轮证据 | 评分 |
|---|---|---|
| R1：三个指标口径能由键盘和指针按需打开 | 原生 details/summary；Enter、Space 与点击回归；1440/320 实际展开截图 | **resolved**。默认折叠，带展开提示、焦点样式；说明可见且没有越出面板。 |
| R1 直接回归：展开不破坏指标条与主图关系 | overview-pane 第一行改为 max-content；dashboard-metric-definition-1440.png、dashboard-metric-definition-320.png | 通过。波浪边随内容撑高，后续区域顺序下移，文本未压在边界或主图上；关闭后保持紧凑三列指标。 |
| R2：动态第 2 页遇到失败后仍恢复第 2 页 | 独立 requestedEventsPage；轮询恢复和显式刷新均断言 eventsPage=2 | **resolved**。失效数据清空不会清空查询页码，旧响应仍由序列保护挡住。 |
| R2 直接回归：错误重试保留失败的目标页 | RecentPortEvents 的重试不再强传第一页；search-and-events 回归与真实 API 第 2 页首条比较 | 通过。一次 503 后重试回到 13–24，并读取原请求页真实记录。 |
| 布局回归：桌面居中流程、移动纵向流程 | 四张 overview 和两张有效 process 重采截图 | 通过本次回归检查。流程整体仍居中，窄屏无横向溢出。 |
| 布局回归：七个预约状态按钮及主图四入口 | 四张 statuses 和四张 overview 重采截图 | 通过本次回归检查。六种状态加全部均可见，320px 自然换行；主图四入口及待接入边界未改变。 |
| 验证产物 | artifacts/ui-browser-results.json | 已读取最终结果：40 expected、0 unexpected、0 skipped、0 flaky，约 115 秒。此套件由主线程执行，审查者读取证据，未重跑。 |

## material findings exact locations

| 编号 | 原问题 | 修复位置 | 本轮裁定与依据 |
|---|---|---|---|
| R1 | 三指标仅使用 title，键盘/触摸没有稳定的口径阅读入口 | web/port-queue-web/src/views/AppointmentListView.vue:143；web/port-queue-web/src/styles.css:114；web/port-queue-web/src/styles/business.css:18 | **resolved**。三项已改为 details/summary，正文处于各自展开内容中；summary 可聚焦，CSS 提供 focus-visible 与展开箭头。浏览器回归覆盖三个指标的 Enter 开关、点击展开、Space 关闭及 1440/320 可见边界。 |
| R2 | overview 清空后，轮询通过 overview.value?.eventsPage ?? 1 回到第 1 页 | web/port-queue-web/src/composables/useOverview.ts:61；web/port-queue-web/src/components/RecentPortEvents.vue:9、101 | **resolved**。requestedEventsPage 独立于 nullable overview；请求开始保留目标页，当前成功响应按返回页更新，定时器和无参数刷新复用此状态，失败分支只清空失效数据。错误重试发出无页码事件，保留 composable 内的原请求页。 |

本轮没有 partial 或 unresolved 项。未新增超出 R1/R2 及其直接回归范围的整改清单。

## evidence/limits

本次已逐一打开以下 **12 张当前重采截图**，确认文件展示内容与命名一致、无黑屏/空白/未加载状态：

- .impeccable/review/dashboard-overview-1440.png
- .impeccable/review/dashboard-overview-2470.png
- .impeccable/review/dashboard-overview-390.png
- .impeccable/review/dashboard-overview-320.png
- .impeccable/review/dashboard-process-1440.png
- .impeccable/review/dashboard-process-2470.png
- .impeccable/review/dashboard-statuses-1440.png
- .impeccable/review/dashboard-statuses-2470.png
- .impeccable/review/dashboard-statuses-390.png
- .impeccable/review/dashboard-statuses-320.png
- .impeccable/review/dashboard-metric-definition-1440.png
- .impeccable/review/dashboard-metric-definition-320.png

desktop process 截图有意将工作区内部滚动到流程区域，顶部指标部分离开滚动视口，不作为裁切缺陷；mobile process-390/320 不在审查包中，本轮继续排除，移动流程由从文档顶部采集的 overview 图核对。流程位置截图冻结 SVG 时间以保证可比性，小船实际运动与暂停依赖浏览器行为验证，静态图本身不证明动画。

已读取当前源码与回归定义：

- dashboard-final.spec.ts:59 开始覆盖一次 503、失效数值清空、恢复后仍请求 eventsPage=2、页码仍为第 2 页及显式刷新保留第 2 页。
- dashboard-final.spec.ts:74 开始覆盖三个指标默认折叠、Enter 开关、1440/320 点击显示、面板边界及 Space 关闭。
- search-and-events.spec.ts:65 开始覆盖动态翻页失败后的重试，恢复 13–24 并与真实 API 第 2 页首条船名比对。
- artifacts/ui-browser-results.json 的最终 stats：startTime 为 2026-11-10T11:58:15.731Z，duration 为 114999.129ms，expected 为 40，其余 skipped、unexpected、flaky 均为 0。

前端最终构建通过由主线程报告；本审查没有再次运行构建、浏览器、SQL、服务器或 detector，也没有修改产品代码。此前 detector 的 74 条 advisory 维持原审查处理：它们不构成这两项修复的阻塞点，本轮没有为清除历史 token 提示而要求重构视觉语言。手机实体设备、屏幕阅读器朗读和跨浏览器原生 disclosure 表现未在本次独立复核中运行，不以桌面自动化冒充实机覆盖。

## final verdict

**ship — R1 resolved，R2 resolved。**

原审查要求的两项修复均已完成，定向代码/测试证据和同一批视口截图支持关闭它们；未发现这些修复导致的布局或交互回归。该裁定的范围是 **R1/R2 修复与直接回归**，不是扩大为整个项目、未来模块或所有运行环境的完成声明。主线程可据此继续文档记录与交付。
