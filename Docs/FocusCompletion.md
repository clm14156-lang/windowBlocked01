# 专注结束页面

## 页面与主题

有目标、无目标、浅色和暗色共用一个 `CompletionView` 和一个 `FocusCompletionDetails` 模板。
结算页进入时直接保留 `IsDarkFocusTheme` 的实际值，不重新查询时间或判断主题。
结算期间更新主题偏好不会改变本次结果的主题；下一次进入专注页时使用新的偏好。
原有专注阶段的主题规则保持不变。

背景与专注页共用 `FocusBackground` 和 `FocusSceneBackground`，通过 WPF 程序集资源加载
`focusPage/focus_white.png` 或 `focusPage/focus_dark.png`，保留原有地形、渐变及暗色星空、云层。
角色和阴影仍属于专注视图，结算时随该视图隐藏；落叶层的 `IsActive` 仅绑定专注阶段，结算时停止并清理。
背景素材、角色素材及原有角色显示逻辑没有修改。

顶部问候整体起点与专注页一致，为 `(40, 40)`；保留原爱心素材、48×48 图标、14px 图标间距及标题/辅助文案字体层级。
底部操作区域复用专注页的按钮样式、高度、字体和主题颜色，底部间距为 20px，中间分隔线保留，旧横向分割线删除。
「再来一次」「返回首页」命令保持原样。

两处大倒计时共用 `FocusCountdownTextStyle`：`Segoe UI Variable Display`、100px、Normal，
Viewbox 同为 440×116，行高及文本度量沿用默认设置。深浅色分别使用原专注页的倒计时文字颜色。

完成页的时间、目标名称和今日累计数据放在独立的 `CompletionSummary` 中，
时间相对原布局上移 36 个逻辑像素，目标行高度为 26，目标与统计之间的外边距为 6；
三项内容的字体、字号和颜色及上方勾选图标的位置保持原样。
主窗口的原 1 像素浅色外框在 `IsFullScreenVisible` 为 True（专注中、已完成）时收起，
使背景覆盖窗口边缘；返回普通页面后恢复外框。

## 完成图标与信息

- 浅色继续使用 `Illustrations/focus_gouxuan.png`。
- 暗色使用指定的 `focusPage/gouxuan.png`，确认其为带透明通道的 600×600 PNG。
- 暗色完成图片显示为 128×128，保持 256×150 的原占位区域和图标中心，缩小图片不会移动下方信息组。
- 两张图片均等比显示，暗色不再绘制任何额外圆环、光晕或装饰；素材自身的光晕和圆点保留。
- 目标行使用现有 `internet.svg` 地球图标及原 `TargetIconSourceConverter`，图标随文字颜色适配主题。
- 目标名称使用单行省略及原提示文本。目标行与统计行放在同一 StackPanel 中；无目标时整行（包括行间距）Collapsed，统计直接移到目标行位置。

统计格式为 `今日累计 N 分钟 · 完成 M 次`。结算通过现有 `FocusStateCoordinator` 读取统计页的数据源，
包括已保存记录，并在当前记录尚未进入持久化快照时加入一次只读投影。
快照中已有相同完成时间和实际时长的记录时，不重复加入；记录保存、计时器和任务完成处理没有改变。
有效记录判定及自然日时长分段复用 `FocusStatisticsCalculator`；完成次数按有效记录的完成日期计算，
累计秒数合并后统一取整到分钟，包括恰好午夜完成的边界。

十句完成文案集中在 `Services/FocusCompletionMessages.cs` 中。
仅在 Stage 真正进入 Completed 时随机选择一次，并避开上一句；重绘、窗口恢复、主题偏好更新和重复完成状态不会重新抽取。

## 本次修改文件

| 文件 | 作用 |
| --- | --- |
| src/FocusApp.Desktop/Views/FocusFlowView.xaml | 共享结算视图、背景、倒计时样式、动态信息行、顶部和底部对齐 |
| src/FocusApp.Desktop/Resources/FocusTheme.Light.xaml | 浅色完成图标资源 |
| src/FocusApp.Desktop/Resources/FocusTheme.Dark.xaml | 暗色完成图标资源 |
| src/FocusApp.Desktop/ViewModels/FocusSessionViewModel.cs | 继承实际主题、结算文案及统计绑定 |
| src/FocusApp.Desktop/ViewModels/FocusStateCoordinator.cs | 接通已有统计数据源与更新通知，释放订阅 |
| src/FocusApp.Desktop/ViewModels/StatisticsOverviewViewModel.cs | 只读合并当前结果与已保存统计记录，避免重复 |
| src/FocusApp.Desktop/Services/FocusCompletionMessages.cs | 十句文案及随机选择 |
| src/FocusApp.Desktop/Services/FocusCompletionStatistics.cs | 复用核心时长计算，按完成日期计算结算次数 |
| tests/FocusApp.Tests/Desktop/FocusCompletionPresentationTests.cs | 深浅色、有无目标、长目标的实际 WPF 渲染及交互、资源、布局检查 |
| tests/FocusApp.Tests/Desktop/FocusCompletionViewModelTests.cs | 实际主题继承、随机稳定性、持久化去重、无效记录及跨日边界 |
| tests/FocusApp.Tests/Desktop/FocusCompletionTypographyPresentationTests.cs | 更新共享模板和共用字体的断言 |
| tests/FocusApp.Tests/Desktop/FocusNoTaskPresentationTests.cs | 更新背景及无目标动态布局的断言 |
| Docs/FocusCompletion.md | 本说明 |
| Docs/FocusTheme.md | 链接结算页的主题继承说明 |

## 验证

实际渲染保持 800×710，四种主题/目标组合、长目标省略、图标比例、无角色/阴影/落叶、
标题及底部对齐、按钮命令、窗口恢复、主题冻结和资源绑定均已检查。
相关测试结果位于 `.verify/completion/results/`，实际 WPF 渲染图位于 `.verify/completion/previews/`。
仅运行本次相关回归，没有把历史全量测试的已知失败计入本次验证。

- 171 个不同的相关测试用例均通过，覆盖结算、专注计时/记录、主题、任务、问候、落叶、悬浮窗口及核心统计。
- 完整 `dotnet build FocusApp.sln -c Release --no-restore --disable-build-servers` 成功，0 警告、0 错误。
- 桌面 Release 发布输出：`.verify/completion/publish/`。
- `.verify/completion/VerifyPublishedCompletion.ps1` 核对了发布程序集中的 13 个页面、主题、字体、背景、勾选、爱心和操作图标资源，全部存在。
- 本地未生成安装包；实际 WPF 渲染和已发布程序集资源加载检查均已完成。
