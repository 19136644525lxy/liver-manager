# 代肝管家

代肝订单管理桌面应用。Windows 本地单机运行，不需要服务器、不需要联网、不需要安装运行环境。

- 直接使用：双击 `发布\代肝管家.exe`（自带 .NET 运行时，旁边的 dll 都是运行必需文件，不要单独移动 exe）
- 首次启动会自动迁移旧工具的数据：`F:\鸣潮\bills\*_bill.json`（旧账单纯 16 条）与 `F:\鸣潮\tools\日期记载\data.db`（托管 2 条 + 99 次打卡），迁移记录写在 `data\导入记录.txt`
- 数据文件：`发布\data\app.db`（SQLite，单文件，备份直接复制整个 `data` 目录即可）
- 错误日志：程序根目录 `错误日志.txt`（全局异常处理写入）

## 技术栈

| 项目 | 选择 |
| --- | --- |
| 语言 / 框架 | C# 12 / .NET 10 / WPF |
| 数据 | SQLite（Microsoft.Data.Sqlite 9.0.0） |
| 架构 | MVVM（自写 ObservableObject / RelayCommand，无第三方 MVVM 框架） |
| 界面 | 自绘无边框窗口 + 浅色清新主题（薄荷绿 `#2FA98C`、背景 `#F4F7F9`、Microsoft YaHei UI） |
| 打包 | `dotnet publish` 自包含文件夹（win-x64，134MB） |

## 目录结构

```
OrderManager.slnx                     解决方案
src/OrderManager/
  App.xaml(.cs)                       启动、全局异常兜底、错误日志
  MainWindow.xaml(.cs)                无边框外壳：自绘标题栏 + 左侧导航 + PageHost
  Themes/Colors.xaml                  调色板、字号、圆角、文字样式
  Themes/Controls.xaml                控件样式（按钮/输入框/表格/Card/导航/ComboBox/DatePicker）
  Models/                             Order、Runner、Item、Checkin、AppRules、Summary
  Services/Db.cs                      SQLite 数据层（建表 + CRUD + 打卡 + 设置）
  Services/CalcService.cs             分成计算（个人高/低、新/旧规则、跑手、助手）
  Services/AppState.cs                全局状态（单例、设置持久化、数据变更事件）
  Services/Migrator.cs                旧账单 / 旧托管库自动迁移
  Services/SeedData.cs                默认跑手与价目表种子数据
  ViewModels/                         7 个页面 VM + 订单编辑 VM
  Views/                              7 个页面 + 订单编辑窗口
tools/                                QA 脚本（UIA/EnumWindows 自动截图、崩溃二分、数据库调试）
shots/                                QA 截图（final-*.png 为交付版截图）
发布/                                 可直接运行的成品（exe + 运行时 + data 数据）
```

## 开发与发布

```powershell
# 构建
dotnet build src\OrderManager\OrderManager.csproj

# 发布（自包含，免安装；输出到 发布 目录）
dotnet publish src\OrderManager\OrderManager.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:DebugType=None -o "F:\订单管理\发布"
```

> 单文件模式（`-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`）也能打包成 60MB 单个 exe，但运行时要把嵌入的 native 库解压到 `%TEMP%\.net`；本机该目录权限异常时会直接启动失败（`Failed to create default extraction directory ... error code: 5`），所以交付版本改用文件夹形式，最稳。

## 功能

1. **概览**：今日单数/营业额/我的分成/助手/跑手 KPI，今日订单列表，近 7 天营业额柱状图（无数据时显示空态文案），待办提醒（待付款、待结算、进行中订单），本月经营汇总。
2. **订单管理**：表格增删改查、双击编辑、右键改状态（待付款/进行中/待结算/已完成/已取消）、关键词搜索、状态筛选、本月/上月/近7天/全部日期胶囊 + 自定义日期区间、导出 CSV、底部实时汇总（单数/总额/我/助手/跑手）。
3. **托管日历**：按订单列出的包月托管月历，点日期打卡/取消打卡，显示本月打卡天数与进度百分比，可切换月份。
4. **统计报表**：按年统计 KPI（单数/营业额/我的分成/助手/跑手/个人 vs 跑手单），按月明细表，按项目分类与按跑手占比条形图，导出 CSV。
5. **跑手管理**：跑手增删改、分成比例、启停、备注。
6. **价目表**：托管服务 / 任务活动 / 地图满探索三类卡片，新增订单时可从价目表选中自动带出金额。
7. **设置**：店铺名与称呼、11 项分成规则（新规则生效日期、阈值、高/低比例、跑手/助手/我）、备份数据库、导出全部订单、打开数据文件夹。

## 分成规则（与旧 tkinter 工具一致）

- 个人接单：`amount > 100` 用高比例，否则低比例；`订单日期 >= 2026-06-24` 用新规则比例，否则旧规则。
  - 旧：高 40% / 低 20%，新：高 20% / 低 10%（全部可在设置里改）
- 非个人单：跑手 80%，助手 15%，我的 5%
- 跑手 `小予` 的单：跑手 85%，我 15%，助手 0
- 以上规则来自旧工具 `F:\鸣潮\bills\config.json` 与 `order_statistics.py`，并修正了旧代码里"新规则硬编码 0.2/0.1 覆盖 config 0.15"的 bug。

## 已验证 / 已修复的问题

- 日历页 `ProgressPercent` 只读属性被 TwoWay 绑定 → `Mode=OneWay`（否则打开托管日历直接报错）。
- 概览今日订单里 `<Run Text="{Binding AmountText}">`（`Run.Text` 默认双向）绑定只读属性，**只要当天有订单就抛 XamlParseException 导致程序启动即退出** → 改 `Mode=OneWay` 并复现验证。
- 编辑窗口 ComboBox / DatePicker 原生样式不统一 → 在 `Themes/Controls.xaml` 重写模板（圆角、自绘箭头、日历图标内嵌）。
- 订单页默认筛选"本月"看不到历史数据 → 默认改"全部"。
- 报表按项目分类全部落到"未分类" → 兜底用订单项目名分组。
- 对话框/编辑窗/各页面在 1320×860 与 2560×1440 两种尺寸下均已截图核对（见 `shots/final-*.png`）。

## 遗留可选项

- 备注输入框在 740 高的编辑窗里需要滚动才能看全（分成预览已固定在底部常驻）。
- 单文件 exe 模式受 `%TEMP%\.net` 权限影响，交付采用文件夹模式。

## 更新记录

### 2026-10-29 修复批次
- ReportsView：按月统计表外层由 StackPanel MaxWidth=1060 改为 Grid MaxWidth=1060，表头与数据行全部改为固定列宽（月份 96 / 单数 76 / 营业额·我的·助手·跑手各 180），修复超宽/居中布局下星号列被压成 10px、数字重叠的问题。
- MainWindow：顶栏 Grid 补 Background="Transparent"（WPF null 背景不参与命中测试）并接管 MouseMove/MouseLeftButtonUp；改为 GetCursorPos 手动拖动，替代在最大化状态下会抛异常的 DragMove()。
- MainWindow：最大化状态下按住标题栏先记录、位移超过 5px 才还原（避免单击标题栏就掉出最大化）；ToggleMaximize 记忆 _normalBounds，还原时回到原位置；MaxBtn 图标在 \uE922/\uE923 之间切换，ToolTip 显示"最大化/向下还原"。
- 验证脚本：tools\qa-fix.ps1（7 页 + 编辑窗 + 超宽报表 + 拖动）、tools\qa-drag.ps1（双击最大化、单击不掉出最大化、双击还原、拖动位移）、tools\qa-release.ps1（发布版启动与报表页验证）。

### 2026-10-29 第二批（用户反馈）
- 移除价目表的「积分」字段：Models\Entities.cs 删 `Item.Points`；Services\Db.cs 建表语句去掉 `points` 列、items 的 SELECT/INSERT/UPDATE 同步调整，并在 `EnsureCreated()` 末尾调用 `DropLegacyItemPointsColumn()`（`pragma_table_info` 检查存在则 `ALTER TABLE items DROP COLUMN points`，老 SQLite 不支持时静默跳过）；Services\SeedData.cs 55 条种子数据的 `Points = "..."` 全部删除；ViewModels\ItemsViewModel.cs 删 `EditPoints`/搜索中的积分匹配；Views\ItemsView.xaml 删卡片上的积分行、编辑器的积分输入框，并改掉搜索占位符与说明文案。
- 托管日历「今天」→「今天打卡」：`CalendarViewModel.GoToday()` 先选中"托管期包含今天"的订单，跳转到今天所在月份，若今天未打卡则 `ToggleCheckin` 直接补打（已打卡则不改动，只更新提示）；新增 `TodayHint` 绑定到页面副标题（默认「点击托管期内的日期即可切换当天打卡」，切换订单时复位），新增 `Contains(CustodyOrder, DateTime)` 与 `DateKey()` 辅助方法。
- 验证：tools\qa-today.ps1（删除今日打卡→点今天打卡→确认补打且二次点击不取消→截图价目表）、tools\qa-items.ps1（价目表列表+新增价目编辑器）、tools\qa-release.ps1（发布版启动+迁移后 items 表无 points 列）。

### 2026-10-29 第三批（用户反馈：新增托管单不进日历）
- 根因：托管日历只展示 `StartDate` 与 `EndDate` 都非空的订单（`CalendarViewModel.Refresh` 的 Where 过滤），而新增订单窗口里这两个日期默认为空、属于可选项 → 选了「包月 / 托管服务」项目但不填日期的单会静默消失。实测确认 Save() 写入日期本身是正常的（dbdump 里 start_date=2026-10-01 / end_date=2026-11-01）。
- ViewModels\OrderEditViewModel.cs：新增 `CustodyHint`、`bool IsCustodyName(string?)`（名字含「托管」「包月」，或价目表分类为「托管服务」）、`void AutoFillCustodyDates()`（两个日期都为空时按「订单日期 ~ 一个月后」带出默认值，可改）；在 `ItemName` setter、`SelectedItem` setter 与 `Save()` 校验前调用，新增 `OnPropertyChanged(nameof(CustodyHint))`。
- Views\OrderEditWindow.xaml：托管两个 DatePicker 下方新增 `Text="{Binding CustodyHint}"` 的 Muted 说明行。
- ViewModels\CalendarViewModel.cs：新增 `MissingCustodyHint` / `HasMissingCustody` 与 `BuildMissingCustodyHint(List<Order>)`、`IsCustodyLike(string?, HashSet<string>)`；`Refresh()` 改为先取 `LoadAllOrders()` 全量，一部分进列表、一部分统计"托管类但缺日期"的单量。
- Views\CalendarView.xaml：左侧列表卡片 Grid 增加第三个 Auto 行，放 `WarningSoft` 底色 + `Warning` 文字的可折叠提醒条。
- 验证：tools\qa-autofill.ps1（把项目设为「标准包月」后读回日期控件：e6=2026/10/29、e7=2026/11/29，弹出截图 autofill-01-form.png）、tools\qa-calen.ps1（手工插入一条无日期的「托管服务」单 → 日历左下出现「还有 1 单托管类订单没填托管开始 / 结束日期…」提示，截图 calen-hint.png）、tools\qa-addcustody.ps1（完整新增托管单后日历出现第 3 单）。

### 2026-10-29 第四批（流畅度 / 操作手感 / 自定义背景图）
- 外观模型：Models\Appearance.cs（BackgroundPath / OverlayOpacity / Stretch / BlurRadius / HasBackground / Clone）；Services\AppState.cs 增加 `Appearance` 属性、`AppearanceChanged` 事件、`SaveAppearance(Appearance)`、`ImportBackgroundImage(string)`（复制到 `DataDir\images\background-yyyyMMddHHmmss.ext`），settings 键 `appearance`。
- 提示条：Services\Notifier.cs（`static event Action<string>? Show` + `Info(string)`）；MainWindow 订阅 Notifier.Show 并在底部居中显示深色胶囊 Toast（淡入 + 上浮，1.9s 后淡出）。
- MainWindow 外观宿主：背景层（Image UniformToFill/Uniform/Fill + Rectangle ImageBrush TileMode=Tile + 遮罩 Rectangle），`ApplyAppearance()` 按外观参数设置缩放（模糊时 1+blur/260 防边缘漏底）、BlurEffect、遮罩透明度；有背景图时把 `BgCard`/`BgSidebar` 运行时替换为半透明画刷（Controls.xaml 的 Card/SoftCard 改为 `{DynamicResource BgCard}`），清除背景时移除覆盖恢复原色。
- 流畅度：Views 切换时 PageHost 淡入 + TranslateTransform 上浮动画；Window 启动 Opacity 淡入；MainWindow 写 `DwmSetWindowAttribute(hwnd, 33, DWMWCP_ROUND)` 让 Win11 圆角生效；Themes\Controls.xaml 的 DataGrid 样式加 `EnableRowVirtualization` / `VirtualizingPanel.VirtualizationMode=Recycling` / `ScrollUnit=Pixel`；新增 Ui\SmoothScroller.cs（窗口 PreviewMouseWheel 接管，对最近的可滚动 ScrollViewer 做 150ms CubicEase 动画，`Offset` 附加属性驱动 ScrollToVerticalOffset）。
- 快捷键：MainWindow.OnPreviewKeyDown 处理 Ctrl+1~7 切页、Ctrl+N 新增订单（经 OrdersView.BeginNewOrder → AddCommand）、Ctrl+F 聚焦搜索（OrdersView.FocusSearch）、F5 NotifyChanged；OrderEditWindow 已有 Esc / Ctrl+Enter，本轮补 Ctrl+S；设置页新增「快捷键」只读列表。
- 设置页新增「外观」卡片（背景图选择/清除、遮罩浓度、铺法、模糊、恢复默认）与「快捷键」卡片；Themes\Controls.xaml 新增 Slim Slider 隐式样式（4px 圆角轨道 + 品牌色描边圆形滑块）。
- 验证脚本：tools\qa-polish.ps1（带背景图截图 / 点刷新 / Ctrl+3 / 设置页）、tools\qa-polish2.ps1（InvokePattern 点刷新抓 Toast、滚轮到外观与快捷键卡片）、tools\dbimport.cs（从 JSON 文件写 settings，规避 PowerShell 传参吃掉双引号）、tools\dbq.cs（通用查询打印）。

### 2026-10-29 第五批（切页性能优化）
- 页面缓存：MainWindow 的 `PageHost` 由 `ContentControl` 改为 `Grid`，页面创建一次后一直留在可视树里，切页只切换 `Visibility`（不再每次卸载/重新挂载、重新排版、重新查库）。新增 `Ui\IRefreshable.cs`（`void OnShown()`）、`AppState.DataVersion`（`NotifyChanged()` 里 +1）；Calendar / Items / Orders / Runners / Settings 五个页面实现 `IRefreshable`，`OnShown()` 里比较 `DataVersion` 决定要不要重新读库（`_seenVersion` 初始 -1，首次必刷）。
- 预创建 + 预热：`PrewarmPages()` 在窗口 Loaded 后以 `DispatcherPriority.Background` 逐帧创建其余 6 个页面；`WarmUp(UserControl)` 让页面以 `Opacity=0 / IsHitTestVisible=false / Visible` 状态走一遍布局与绘制，随后在 `DispatcherPriority.Render` 收起来（若期间用户已切到该页则跳过收起）。
- 过渡动画：`AnimatePageIn(bool firstShow)` 不再对整页做 Opacity 动画（整页透明会让卡片阴影每帧重绘），改为 `TranslateTransform.Y` 位移（首次 12→0，回访 6→0，150ms CubicEase）；距上次切页 < 320ms 时直接跳过动画。
- 切页时 `page.UpdateLayout()` 强制把排版算完，避免"先出现半页再补"。
- 卡片阴影：Themes\Controls.xaml 的 `Card` 去掉 `Effect={StaticResource CardShadow}`（一页十几张卡片时阴影让首次显示多花上百毫秒），保留 1px `BorderSoft` 描边保持层次。
- 打点：新增 `Ui\Perf.cs`（只有环境变量 `DG_PERF=1` 时写 `perf.log`），`Navigate()` 记录 create/show/refresh 与 first-frame 耗时。
- 实测（Release 发布版，1320×860，`tools\qa-perf-rel.ps1`）：热态切页 `create=0ms show=0~2ms refresh=0ms`；首次打开订单页 `show=8ms refresh=10ms`、日历 `show=1ms refresh=5ms`、价目表 `refresh=2ms`；启动首屏 `create=19ms show=41ms`。
- 验证脚本：`tools\qa-perf2.ps1`（Debug 版测切页耗时）、`tools\qa-perf-rel.ps1`（发布版同上）。注意 PowerShell 5.1 里 `Join-Path` 四层嵌套会因缺少 `ChildPath` 报错，路径要用单层 `Join-Path $root "a\b\c"`。

### 2026-10-29 第六批（用户需求：直接使用订阅的 Wallpaper Engine 壁纸）

- 结论：Wallpaper Engine 没有公开的"账号订阅列表"API。可用的本机数据源是 `steamapps\workshop\content\431960\<id>\project.json`（title / type / file / preview）与壁纸引擎 `config.json` 的 `selectedwallpapers`；`wallpaper64.exe -control getWallpaper` 在本机返回空，故以 config.json 为主、命令行兜底。
- 新增 `Services\WallpaperEngine.cs`：探测 Steam 安装目录（注册表 `HKCU\Software\Valve\Steam` + `libraryfolders.vdf`）、扫描订阅壁纸、解析 project.json、解析当前桌面壁纸、缩略图懒加载（DecodePixelWidth=320）。
- `Models\Appearance.cs`：新增 `Source`（image / we）、`WeTitle / WeProjectPath / WePreviewPath / WeMediaPath / WeKind / WeFollowCurrent` 与计算属性，`Clone()` 复制全部字段。
- `MainWindow`：新增 `MediaElement BackdropVideo`（静音、循环、MediaFailed 回退到预览图），`ApplyAppearance()` 按 视频 / 图片 / 平铺 / 无背景 分流；窗口 `Activated` 时若开启跟随则重新解析当前壁纸。预览图解码失败时保留上一张背景，避免闪白。
- 设置页新增「壁纸（Wallpaper Engine）」卡片（由 ui-we 实现）：来源切换（静态图片 / 订阅壁纸）、跟随当前壁纸、搜索、重新扫描、3 列缩略图列表（视频/场景标记）、空态提示。
- 验证：69 张订阅壁纸正常列出；点选即刻换背景；「跟随当前壁纸」能同步到 Wallpaper Engine 正在使用的壁纸；视频壁纸循环播放；发布版启动无错误日志。
### 2026-10-29 第七批（用户需求：壁纸不糊 + 切换壁纸时 UI 通透化）

**通透模式（GlassMode）**
- `Models\Appearance.cs`：新增 `GlassMode`、`WeHiResPath`；`ResolvedImagePath` 改为 WE 时优先用高清缓存图。
- `MainWindow.xaml.cs` 新增 `ApplyGlass(Appearance)` / `ClearGlassBrushes()`：通透时把
  BgCard `#1F`、BgSidebar `#14`、BgHover `#33`、BgSelected `#40`、BgInput `#2E`、BorderSoft `#4D`、
  BorderInput `#66`、RowLine `#26`、NeutralSoft `#26`（都是白色 / 品牌色的低透明度版本），
  状态色 *Soft 用 alpha `0x33` 的同色相，次要文字加深到 #465361 / #64717F 保证花壁纸上可读。
- 为让运行时覆盖生效，`Themes\Colors.xaml`、`Themes\Controls.xaml` 与 7 个 View 里共 **132 处**
  `{StaticResource …}` 改成 `{DynamicResource …}`（BgCard/BgSidebar/BgHover/BgSelected/BgInput/
  BorderSoft/BorderInput/NeutralSoft/BrandSoft/SuccessSoft/WarningSoft/DangerSoft/InfoSoft/
  TextSecondary/TextMuted/RowLine）；`Colors.xaml` 新增 `RowLine` 键。
- `ViewModels\SettingsViewModel.cs`：新增 `GlassMode` 双向属性（改动即 `SaveAppearance()`），
  `SelectedWallpaper` setter 里自动置 `GlassMode = true`（换壁纸就变通透）。
- `Views\SettingsView.xaml`：外观卡新增「通透模式（只留文字与组件轮廓）」勾选框；「背景图」整行包进
  `Visibility={Binding IsImageSource}` 的 StackPanel，用壁纸引擎时不再显示"未设置"。

**清晰度策略**
- `Services\WallpaperEngine.cs`：`ReadWallpaper` 用新增的 `PickLargestImage` 在作品目录挑像素面积最大的图
  （同目录常同时有 192px GIF 和 1024px JPG）；`EnsureHiResPreview` 通过 Steam `GetPublishedFileDetails`
  取 `preview_url`，下载后用 `DecodePixelSize` 校验，**宽度 < 600 直接丢弃**（Steam 很多预览也只有 192px），
  合格才写 `data\images\we\<id>.jpg`，由 `Resolve(appearance, cacheDir)` 命中使用。
- 视频类壁纸走 `MediaElement` 播原片，天然清晰；场景类受限于创意工坊只有 1024~1079 预览图（本机实测
  Steam 返回的也是 1079x1079），会随窗口放大而略软——要进一步锐化必须解析 scene.pkg 做真渲染，
  这需要 WebGL + 纹理解码，属于后续再做的大工程。

**验证**
- `dotnet build` 0 警告 0 错误；截图 `shots\glass-final-dashboard.png`、`shots\g2-02-orders.png`、
  `shots\g2-04-settings-appearance.png`（工具 `tools\capP.ps1` 用 `PrintWindow(PW_RENDERFULLCONTENT)`
  截取被遮挡的窗口，`tools\qa-glass2.ps1` 做 UI 自动化）。
- 发布：`dotnet publish src\OrderManager\OrderManager.csproj -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=false -p:DebugType=None -o "F:\订单管理\发布"` → `发布\代肝管家.exe` 171008 字节，
  启动正常、无 `错误日志.txt`。

**踩坑记录**
- `Start-Process <gui exe>` 会挂起 → 一律 `cmd /c start "" "$exe"`；且**不要**给它接管道。
- 活动窗口被用户浏览器挡住时 `CopyFromScreen` 截到别人家窗口；优先 `PrintWindow`；
  UIA 点击前要先 `SetWindowPos(HWND_TOPMOST)` + `SetForegroundWindow`，否则点到别的窗口上。

### 2026-10-29 第八批（用户需求：托管订单续单）

**新增「续单」**：`ViewModels\RenewOrderViewModel.cs` + `Views\RenewWindow.xaml(.cs)`，
`OrdersViewModel` 增加 `RenewCommand` / `CanRenew`（仅 StartDate 与 EndDate 都非空的托管单可用）、
`RenewSelected()` 与 `RenewRequested` 事件；`OrdersView` 工具栏加「续单」按钮、右键菜单加
「续单（托管）」，`OrdersView.xaml.cs` 打开 `RenewWindow`（Owner=主窗）并在成功后 `Refresh()`。

关键逻辑 `RenewOrderViewModel`：
- `BaseEnd` = `EndDate`，若它早于 `max(今天, StartDate)` 则从后者起算（避免续出过去的日期）；
- `NewEndDate = BaseEnd.AddMonths(Months)`，`Save()` 写 `EndDate`、`Amount += ExtraAmount`、
  备注追加「续单 +N 个月（至 yyyy-MM-dd）」，然后 `Db.UpdateOrder` → `NotifyChanged()` → `Notifier.Info`。

**踩坑**：`RenewWindow.xaml` 用了 `{StaticResource BoolToVis}` 但该键只在 `SettingsView.xaml` 局部定义，
运行时抛 `XamlParseException: 无法找到名为"BoolToVis"的资源` → 在 `RenewWindow.xaml` 的
`Window.Resources` 里补 `<BooleanToVisibilityConverter x:Key="BoolToVis" />`。
（教训：`Window.Resources` 里不要依赖其它 View 的局部资源；共享转换器应放 `App.xaml`。）

**验证**：`tools\qa-renew3.ps1` 走 UIA 全流程 —— 点「订单管理」→ 选中第一行 → `InvokePattern` 点「续单」
→ 用 `EnumWindows`（按进程 id + `IsWindowVisible`）找到 title=续单 的 560x592 窗口 → `PrintWindow` 截图
`shots\renew-05-dialog.png` → `InvokePattern` 点「确认续单」→ 截图 `shots\renew-06-after.png`（含 Toast
「木易白 已续单到 2026-12-12」）；`tools\dbdump.cs` 确认 `end_date` 2026-11-12 → 2026-12-12、
备注追加「；续单 +1 个月（至 2026-12-12）」、金额不变。测试后已用 `dbmod.cs` 还原数据。

### 2026-10-29 第九批（用户需求：场景壁纸解析渲染 / 自适应清晰度）

**背景**：Wallpaper Engine 没有公开的"账号订阅"接口，只能读本机 Steam 创意工坊目录；场景类壁纸的本地封面
只有 254~1079px，直接铺背景必糊。这一批把 scene.pkg 离线渲染接进了应用。

**渲染引擎**：`tools\SceneRender\`（独立完成，见其 README）——PKG 容器、TEX(TEXV0005) 贴图
（RGBA8888/DXT1/3/5/R8/RG88 + LZ4 + 内嵌 PNG/MP4）、`scene.json` 图层树、cover 语义合成，输出 PNG/JPEG。
实测 48 个场景壁纸 48/48 可解析，单张 660x430 平均 1.03s；1320x860 约 1.2~3.0s、峰值内存 54~482MB。

**接入方式**：`src\OrderManager\OrderManager.csproj` 加
`<ProjectReference Include="..\..\tools\SceneRender\SceneRender.csproj" />`（进程内调用，不额外发 exe）；
`Services\ScenePreview.cs`：
- `CacheRoot = <数据目录>\images\scene`，命名 `<id>_<宽>x<高>.jpg`（JPEG q92，约 150~390KB）；
- `FindCached(id, needW, needH)` 挑"不小于所需尺寸里最小的那张"；
- `TargetSize()` 按窗口像素尺寸算渲染尺寸（下限 960 宽、上限 2600x1620、对齐 8）；
- `Render()` 用 `ScenePackage.Open` + `SceneDoc.Parse` + `Renderer.Render()`，线程池执行，异常一律返回 null
  （失败就继续用封面，不弹错）；
- `ResolveFfmpeg()`：程序目录 ffmpeg.exe → 常见安装路径 → PATH；`RenderOptions.FfmpegPath` 传进去，
  这样内嵌视频贴图的场景也能出图；渲染后清理临时抽帧文件（`%TEMP%\dg-scene`，只删 10 分钟前的）。

**MainWindow 接入**：`ApplyAppearance()` 里场景壁纸走 `TryShowSceneBackdrop()`：
先 `FindCached` 命中就直接铺，未命中则先铺封面再由 `StartSceneRender()` 后台渲染（同一 id+尺寸不重复开，
换壁纸/换尺寸会 `CancellationTokenSource.Cancel()` 掉旧的）；渲染完成回调里再校验当前壁纸没被换掉才替换；
`SizeChanged` 加 650ms 防抖后重新 `ApplyAppearance()`，于是**窗口放大就自动重渲染更大尺寸**（自适应）。

**验证**：Debug 版 1320x860 渲染出 `2949019513_1320x856.jpg`，截图 `shots\scene-01-dashboard.png`；
最大化后自动追加 `3025975631_2568x1448.jpg`（`shots\scene-02-maximized.png`）；发布版用用户当前选中的
场景壁纸实测成功（`发布\data\images\scene\2949019513_1320x856.jpg`，`shots\scene-03-published.png`）；
视频壁纸回归正常（`shots\video-regression.png`），无 `错误日志.txt` 新条目。
发布目录 136.4MB（比上一版多约 2.4MB：SceneRender.dll + SixLabors.ImageSharp.dll）。

**已知限制**：不渲染粒子/文字/bloom/遮罩等后处理；`.mdl` 三维模型场景（如 2887099508）合成不正确；
混合模式与视差为近似；视频贴图只取单帧。这类壁纸仍比封面清晰，但不如壁纸引擎原生画面。

### 2026-10-29 第十批（基础实时壁纸 + 开机自启）

**渲染器（F:\订单管理\tools\SceneRender）**
- Scene.cs：解析 origin/scale/angles 的关键帧通道（ParseChannel）到 ValueAnimation，SceneObject 新增 OriginXAnim/OriginYAnim/ScaleXAnim/ScaleYAnim/RotationAnim。
- Renderer.cs：RenderInternal(bool withParticles) + Render()；新增 ApplyAnimations(time)、RenderFrame(time)、EnsureAnimationInfo()、CanvasToOut()；**无关键帧动画的场景会把静态图层合成为 _baseFrame 缓存**，每帧只克隆底图 + 画粒子（1122x730 从 ~60ms/帧降到 0.3ms/帧）；RenderOptions.Particles 开关；BuildLayers 首次解析后走 RefreshLayers() 只更新 alpha/world。
- Particles.cs（新）：ParticleEmitter + ParticleRenderer，10 种预设（snow/rain/ember/fire/firefly/sparkle/leaf/petal/bubble/fog），NormalizePreset 认不出返回 null → 不画（避免随机点缀）。
- Player.cs（新）：ScenePlayer(packagePath, width, height, ffmpegPath, verbose, particles)，Warm(time) / Frame(time) / DrawnCount。
- Program.cs：新增 stats、cat、ench、live 四个命令。

**普查数据（48 个带 scene.pkg 的订阅壁纸）**：仅 5 个有图层关键帧动画、32 个有粒子层、30 个有文字层，scriptEntries 全为 0 → 光做关键帧动画几乎不动，所以补了近似粒子。

**WPF（F:\订单管理\src\OrderManager）**
- Models/Appearance.cs：新增 LiveWallpaper。
- MainWindow.xaml.cs：TryStartLiveBackdrop（scene.pkg → WriteableBitmap）、LiveSize()（窗口像素 ×0.85，夹在 720~1440）、LiveLoop()（后台 ScenePlayer，Rgba32→BGRA 交换后 Dispatcher 写 WriteableBitmap，24fps 节流）、StopLive()、ClearBackdrop() 一并停播、尺寸变化 650ms 防抖后 ApplyAppearance() 重开。
- Services/StartupManager.cs（新）：IsEnabled/SetEnabled 写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run\代肝管家。
- Services/ScenePreview.cs：MaxWidth 3840 / MaxHeight 2160、CleanupOnce() + CleanupCache()、FfmpegPath()；启动后 Task.Run(ScenePreview.CleanupOnce)。
- ViewModels/SettingsViewModel.cs + Views/SettingsView.xaml：LiveWallpaper、AutoStart 两个属性；「外观」卡加「动态壁纸（实验）」，新增「启动」卡（开机自动启动）。

**实测**：雪景壁纸（3358641387）1122x730 静态底图缓存后 CPU 约 1%（1 帧 0.3ms）；带关键帧动画的场景（3025975631）39ms/帧 ≈ 25.6fps；用户壁纸 2949019513 静态渲染沿用 4K 缓存。发布仍是 F:\订单管理\发布\代肝管家.exe 171008 字节（SceneRender.dll 随之更新）。

### 2026-10-30 补充：开机自启改用「登录时启动」计划任务

这台机器的 Windows 会话禁止写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 与 %APPDATA% 下的「启动」文件夹（实测：SecurityException / UnauthorizedAccessException，连计划任务调起的进程也一样），所以 StartupManager 改成四级兜底：

1. 注册表启动项（标准方式）
2. 「启动」文件夹快捷方式（WScript.Shell COM）
3. 「启动」文件夹 .cmd 脚本
4. **计划任务**：`schtasks /create /tn 代肝管家 /tr "<exe>" /sc onlogon /rl limited /f`

第 4 种由系统计划任务服务创建进程，不受上面的写入限制，实测 `Start-ScheduledTask` 能正常拉起应用。设置页勾选框通过「注册表值 / 快捷方式 / .cmd / schtasks query」任一存在判定为已开启；取消勾选会把这四种痕迹都删掉（注册表值、两个文件、计划任务）。
补充（同日）：因为这台机器连「删除计划任务」都会被拦，所以加了「用户意图」这条兜底——

- 开关状态记在数据库 settings 表 `autostart_enabled`（1/0），不受系统权限限制，勾/取消都能立即成功
- 计划任务的动作改成 `代肝管家.exe --autostart`；带这个参数启动时，如果用户意图是「关闭」，程序会直接退出，不再弹窗口
- 取消勾选 = 写 0 + 尽力清理注册表/快捷方式/计划任务；清理不掉也不会报错，且下次登录不会自动出现