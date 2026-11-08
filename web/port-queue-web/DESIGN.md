---
name: PortQueue 冰蓝港口工作台
description: 对照用户 TIDELINE 参考图的跨业务总览与预约查询工作台
colors:
  primary: "#153a56"
  canvas: "#e4ebf0"
  ink: "#0c1c35"
  muted: "#365773"
  blue: "#0872dc"
  navigation-selected: "#0757ad"
  navigation-selected-bg: "#c0dff4"
  navy-hover: "#19466b"
  glass: "rgba(241, 248, 252, .76)"
  glass-line: "rgba(255, 255, 255, .46)"
  line: "rgba(85, 132, 165, .18)"
  row-selected: "#b2d8f8"
  focus: "#0763c1"
  pending: "#785008"
  pending-bg: "#f4e6bd"
  approved: "#096442"
  approved-bg: "#b7ead9"
  completed: "#205b8a"
  completed-bg: "#c0def1"
  inactive: "#405a70"
  inactive-bg: "#dbe7ee"
  rejected: "#943d34"
  rejected-bg: "#f3d7d3"
typography:
  display:
    fontFamily: "Segoe UI, Microsoft YaHei, sans-serif"
    fontSize: "26px"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "-0.6px"
  headline:
    fontFamily: "Segoe UI, Microsoft YaHei, sans-serif"
    fontSize: "16px"
    fontWeight: 700
    letterSpacing: "-0.25px"
  title:
    fontFamily: "Segoe UI, Microsoft YaHei, sans-serif"
    fontSize: "15px"
    fontWeight: 700
    letterSpacing: "-0.25px"
  body:
    fontFamily: "Segoe UI, Microsoft YaHei, sans-serif"
    fontSize: "12px"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI, Microsoft YaHei, sans-serif"
    fontSize: "11px"
    fontWeight: 400
  metric:
    fontFamily: "Segoe UI, Microsoft YaHei, sans-serif"
    fontSize: "32px"
    fontWeight: 720
    lineHeight: 1.05
    letterSpacing: "-0.6px"
rounded:
  small: "7px"
  control: "8px"
  search: "10px"
  panel: "12px"
  shell: "16px"
spacing:
  tight: "6px"
  control-gap: "10px"
  panel-gap: "16px"
  section-gap: "16px"
  panel-inset: "17px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "#ffffff"
    rounded: "{rounded.control}"
    padding: "0 16px"
    height: "40px"
    typography: "{typography.body}"
  button-primary-hover:
    backgroundColor: "{colors.navy-hover}"
  button-icon:
    backgroundColor: "rgba(235, 247, 254, .42)"
    rounded: "{rounded.small}"
    size: "34px"
  search:
    backgroundColor: "rgba(241, 248, 253, .58)"
    textColor: "#1c3c5d"
    rounded: "{rounded.search}"
    padding: "0 11px"
    height: "40px"
  navigation:
    textColor: "{colors.ink}"
    rounded: "{rounded.small}"
    padding: "0 12px"
    height: "48px"
  navigation-selected:
    backgroundColor: "{colors.navigation-selected-bg}"
    textColor: "{colors.navigation-selected}"
  status-pending:
    backgroundColor: "{colors.pending-bg}"
    textColor: "{colors.pending}"
    rounded: "{rounded.control}"
    padding: "6px 10px"
  glass-panel:
    backgroundColor: "{colors.glass}"
    textColor: "{colors.ink}"
    rounded: "{rounded.panel}"
  summary-strip:
    backgroundColor: "{colors.glass}"
    padding: "14px 4px"
    height: "88px"
  filter-tab:
    rounded: "{rounded.small}"
    padding: "6px 13px"
    height: "32px"
  table-row-selected:
    backgroundColor: "{colors.row-selected}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
  vessel-spotlight:
    backgroundColor: "#b8d3e2"
    rounded: "{rounded.panel}"
---

# Design System: PortQueue 冰蓝港口工作台

## Overview

**Creative North Star: "冰蓝港口工作台"**

本系统以用户于 2026-11-07 确认的五张 TIDELINE 截图为视觉依据：冰蓝磨砂材质、细白边线、左侧纵向导航、紧凑工具区、船舶场景和清晰的表格选中态。参考位于 `../../docs/design/reference/`；该方向替代此前白底列表与深色顶栏，不能用通用的反玻璃或反卡片建议改写。

这是供中文港口业务人员操作的工作台。视觉身份来自蓝白层次和紧凑比例；“总览”呈现跨业务概况，“进港预约”承载检索和详情。图像提供港口场景感，业务文字保持真实、克制。当前文件记录 2026-11-08 已实现界面的视觉规则，不代表与参考图逐像素一致。

Avoid unnecessary secondary labels and descriptions. Let the layout do the explaining. 删除重复副标题、装饰性说明和调试性质页脚，保留必要字段名、会改变判断的业务口径、错误恢复和无障碍名称。后续业务页面继续使用这套冰蓝商务风格。

**Key Characteristics:**
- 较深冰蓝工作区衬托更亮的玻璃面板与详情栏。
- 深蓝操作按钮、蓝色导航选中态、整行浅蓝记录选中态。
- 船舶场景铺满焦点面板，底部连续暗层承托白色时间窗文字。
- 摘要底缘只用一道轻量波浪；表格和表单维持规整边界。
- 紧凑中文字段、统一线性图标、可见的键盘焦点与明确状态文字。
- 共用稳定外框和主次栏，查询反馈局限于结果区，页面往返保留操作上下文。

## Colors

主色是带灰度的冰蓝环境色，以深海军蓝承载正文和操作，状态色只用于明确的业务状态。

### Primary

`primary` 是主按钮的纯色深蓝底；`blue` 用于品牌、输入光标与交互强调。`navigation-selected` 用于导航当前项文字，搭配纯色 `navigation-selected-bg`。`navy-hover` 是主按钮悬停色，`focus` 是键盘焦点描边色。`row-selected` 对应真实表格选中背景；不要误用未被表格消费的旧 CSS 变量作为表格色值。

### Secondary

`pending`、`approved`、`completed`、`inactive`、`rejected` 及各自背景用于待审核、已批准、已完成、草稿或已撤销、已驳回。状态始终同时显示中文文字；表格内用文字与圆点，面板内用有底色的标签。

### Neutral

`canvas` 是外框之外的蓝灰画布；`ink` 是主要正文，`muted` 是辅助信息和搜索占位文字。`glass` 叠在工作区冰蓝渐变之上，`glass-line` 以更柔和的白边描出面板轮廓，`line` 分隔摘要和详情信息。详情比列表更亮，不能把所有面板合并成同一灰蓝平面。

**The 层次 Rule.** 比较参考图时同时检查工作区、列表和详情的净显示颜色；半透明色必须在实际背景上判断。

## Typography

**Display Font:** Segoe UI，Microsoft YaHei，sans-serif。

**Body Font:** 同一系统字体栈。Windows 中文操作界面需要清楚的中英数字混排与稳定回退；这里采用系统字体是使用场景和参考密度的选择，不引入宣传页装饰字体。

### Hierarchy

船名焦点使用 `display`；页面标题使用 `headline`；面板标题使用 `title`；表格与按钮使用 `body`；辅助标签使用 `label`。摘要数字使用 `metric`，日期、编号和数字采用等宽数字特性；详情船名为 20px，选中摘要船名为 21px。辅助说明可到 10px，必须结合实际屏幕阅读检查。长船名、审核意见和详情值允许换行。

## Layout

桌面外框最大宽 1600px，宽度为视口减 64px，高度为 100dvh 减 48px，最小高 760px，外距为 24px；外框内边距为 0，默认侧栏占 15.2%。主区左右与底部内距为 22px，工具区所在网格行高 78px，主内容区接续填满剩余高度。面板横向与纵向间距统一为 16px；窄于内容最小高度的桌面视口允许页面自然滚动。

总览和进港预约共用稳定外框、侧栏、工具栏及 1.65:1 主次栏；次栏最小宽 270px。总览采用三行：88px 高概况摘要加 8px 波浪留白、最小高 280px 的场景与预约信息区、最小高 230px 的预约动态与港口动态区；后两行按 1.08:1 分配余高。预约动态显示五条查询摘要；港口动态显示独立总览接口返回的最近至多五条实际事件。预约页首行是 88px 选中摘要，其下清单与详情填满剩余高度。桌面表格和详情在各自区域内滚动，保留标题、筛选和分页位置。

总览五条摘要与完整清单分别缓存查询结果和选中项；页面往返不清空筛选、不因导航重复请求，浏览器前进后退恢复对应页面。全港指标独立查询，不跟随预约筛选。显式刷新更新总览、摘要和已访问的完整清单；首次进入完整清单时加载。查询只更新结果区，已有表格与滚动位置保留到请求结束，不用整页过渡遮盖布局变化。

- ≤1250px：侧栏固定 170px，外框两侧合计留 48px；主区左右内距收紧为 18px 和 13px，隐藏完整清单船舶缩略图。
- ≤1000px：外框高度改为自然内容高度，最小高为 100dvh 减 28px，外距 14px；侧栏 155px，两侧合计留 28px。日期隐藏，总览下方两块内容及清单与详情纵向排列；总览表格记录区高 310px，完整清单记录区高 420px。恢复清单缩略图，详情内部暂用两列；场景与预约信息暂按 1.7:1 并排。
- ≤700px：外框两侧合计留 20px；导航由按钮展开为两列菜单，搜索独占一行，场景与信息上下排列，详情回到单列。搜索高 44px，常用筛选和分页控件至少 40px。
- 手机表格保留 570px 最小宽度，在局部横向滚动并显示提示；不得通过挤掉状态和时间来消除滚动。页面基础最小宽度 320px。

每次视觉改动保存桌面与窄屏截图，并加入用户参考自身 973×640 尺寸的对照检查；构建通过不替代视觉验收。

## Elevation & Depth

深度来自冰蓝渐变底、半透明白蓝面板、柔和白边与少量结构阴影。普通面板采用 16px 背景模糊，侧栏采用 18px；侧栏是单层半透明浅底，列表与详情分别使用较亮的半透明背景。主按钮用纯色底和细浅边建立操作边界，无内阴影。候选浮层有独立浅底与阴影，浮在工具区下方，不挤压内容区。

### Shadow Vocabulary

- 外框：`0 14px 40px rgba(28, 55, 77, .1)`，提供轻量环境层次。
- 搜索候选浮层：`0 10px 30px rgba(25, 53, 74, .18)`，区分浮层与后方内容。

**The 场景文字 Rule.** 港口场景图铺满焦点容器；底部深色渐变位于左侧浅色蒙层之上，让时间窗两端白字落在连续深色背景上。

## Shapes

面板与外框使用前置 token 中的圆角层级，输入略圆于按钮。1px 白色透明边线持续表达磨砂玻璃边缘，表格内部保持轻分隔。图标遵循 24px 画布、1.8px 圆端圆角描边，默认显示 22px；品牌波纹是独立标志。状态点和船舶切换按钮使用圆形；摘要图标使用透明底，不再添加圆形底块。

概况摘要的底缘使用 10px 高的波浪遮罩；曲线只提供少量航运特征，不能伸入字段网格或在每块容器重复。表格、表单和密集信息区保持整齐边界。

## Components

### Buttons

深蓝主按钮承载刷新、重新查询、查看预约详情；纯色 `primary` 底、白字、细浅边，悬停转为 `navy-hover`。禁用透明度为 0.5。图标按钮基准 34px 方形，浅冰蓝底，悬停近白且图标转蓝；禁用透明度为 0.55。焦点使用 2px 描边和 3px 外偏移。按钮与表格行仅以 140ms ease-out 过渡背景色与文字色；系统要求减少动态时关闭。手机场景中的主行动有更高触达区域，不能把现有较小图标控件描述为全部达到了 44px。

### Inputs / Fields

顶部搜索包含搜索图标、输入、非空时的清除按钮和“查询”按钮，整体采用磨砂浅底；聚焦容器使用 `focus` 的 2px 轮廓，外偏移 2px，placeholder 使用 `muted`。真实 API 返回最多六条候选，浮层显示船名、预约编号、申请到港时间和状态；上下键切换，Enter 选择高亮候选或提交自由关键词，Escape、移出焦点及点击外部关闭。中文输入法合成期间不提交、不请求候选；输入防抖 220ms，旧请求取消并用序号防止旧响应覆盖新结果。无候选时仍允许自由提交；候选读取失败提供“重试”，重试先恢复输入焦点，保持浮层打开。

输入草稿与已提交条件分开，状态切换基于最近已提交的查询意图，不能夹带尚未提交的草稿。选择候选后按其预约编号进入清单并显示该条详情。原生状态选择器与分页大小选择器保持原生键盘行为。

### Navigation

纵向菜单采用线性图标与中文名称；当前项用纯浅蓝底、深蓝文字，并以 `aria-current` 表达。可用入口为“总览”“进港预约”；“锚地排队”“泊位计划”“进出港记录”“查询统计”禁用且显示“待接入”。账号区当前显示“未登录”，不附环境说明。认证接入后，用户名、角色和权限必须来自后端会话，不使用前端角色切换冒充授权。

### Chips

状态标签有圆点、中文名称和对应底色；表格版本去掉底色。筛选标签为独立按钮，选中时白色磨砂底、加深加粗文字，并用 `aria-pressed` 表达。不要把状态只编码为颜色。

### Cards / Containers

基础玻璃面板共用细白边、圆角和背景模糊。总览摘要呈三列，仅显示“待审核预约”“锚地在场”“待靠泊计划”及全港查询值；加载或错误显示破折号，不保留过期数字。当前开发库的 53 条预约是持久化合成场景，所有列表、状态和统计均通过 API 查询数据库；数据性质记录在产品和数据库文档中，不增加界面角标。详情栏使用更亮的底色和左右对齐的字段组；长文本可折行，审核意见保留原有换行。

### Vessel Spotlight

港口图像为铺满裁切的场景焦点，左上显示真实选中船名、预约编号及状态；下方为申请到港、申请离港与“到离港时间窗”。时间统一按 Asia/Shanghai 格式化。切换按钮仅遍历本页已有记录；“查看预约详情”切换布局并将焦点移到详情。场景标题保留为无障碍名称，不重复显示可见副标题。

主场景使用用户提供的 `src/assets/port-scene.png`（733×826），来源与用途保存于同名 `.json`。原有 `vessel-reference.png` 仅用于清单缩略图、选中摘要和详情图，其裁切来源保存在 `src/assets/vessel-reference.png.json`。两者均为全局装饰，不绑定具体船舶，图片使用空 `alt`；不显示图片来源角标，不把场景图冒充记录的实船照片。不得展示原海报水印、社交账号或背景大字。

### Appointment Table

表格显示船舶与预约编号、申请到港及预约状态。普通单元格内边距为 12px 8px，总览紧凑行上下为 8px；整行悬停浅蓝，选中使用 `row-selected`。行内船舶按钮提供键盘选择，整行点击只是快捷方式。固定倒序写在无障碍表格标题中，表头保留必要的 UTC+8 时区信息；不额外显示“全部申请日期”等排序说明行，不放置虚假的可排序控件。状态筛选、每页条数、总数和分页集中在“进港预约”，总览提供“查看全部预约”。

### Recent Events

“港口动态”沿用玻璃面板和轻分隔线，每条展示船名、实际事件类型和发生时间，最多五条。窄屏将时间移到下一行，保持船名和事件可扫读。查询失败提供“重试”，没有记录时显示空状态，不填入虚构事件。

### Loading / Empty / Error

反馈限制在对应内容区。首次加载显示骨架，后续查询保留已有表格 DOM、滚动位置与选中详情，表格降至 0.52 透明度并置为不可交互；顶部 2px 细线和“正在更新…”提示处理状态，不播放伸缩动画。请求结束前旧数据仍对应旧筛选、旧页码，成功后同步替换条件、数据与计数，避免把旧数据冒充新结果。

错误发生后清空失效记录、计数和详情，保留失败的查询意图用于“重新查询”；分页显示“查询未完成”。空结果提供清除筛选或返回第一页。取消旧请求和请求序号共同隔离过期响应，不以假记录填补空白；当前界面没有全局装饰动效。

## Do's and Don'ts

### Do:

- **Do** 对照用户五张参考图保留冰蓝磨砂、紧凑侧栏、深蓝操作和明亮详情层次。
- **Do** 使用数据库查询字段和独立的全港概况，保持申请时间、实际事件时间与 UTC+8 时区语义清楚。
- **Do** 让布局承担解释，保留必要字段、错误恢复和无障碍名称；素材来源与合成数据性质保存在文档和元数据。
- **Do** 仅在摘要或场景区域少量使用海浪边，表格和表单保持整齐边界。
- **Do** 保留可见键盘焦点、状态文字、局部横向滚动与移动端导航。
- **Do** 延续共用外框与主次栏比例，导航保留筛选和选中项，查询使用局部反馈并保留表格滚动位置。
- **Do** 在桌面、窄屏与参考尺寸保存有效截图后再评价视觉完成度。

### Don't:

- **Don't** 恢复白底列表或深色顶栏，或用通用风格建议抹掉用户指定的玻璃材质。
- **Don't** 复制海报大字、水印、社交账号，或把裁片冒充真实船舶照片。
- **Don't** 在业务界面添加 Demo、课程演示数据、本地演示、更新时间、船舶示意角标或重复的排序说明行。
- **Don't** 为凑版式捏造 AIS、地图、登录身份、通知、全港统计或未实现的操作。
- **Don't** 用构建成功、静态预览或局部修复结论声称逐像素还原和整站全面验收。
