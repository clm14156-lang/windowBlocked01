# 折叠和悬浮窗口暗色主题

折叠条与悬浮窗口使用统一的深灰黑渐变、灰色圆角描边、轻微阴影、白色主要文字和柔和灰色次级文字。
保留应用原有的橙色强调色。暗色任务区增加只读子任务进度胶囊和灰色虚线连接线；浅色不显示这些装饰。

## 主题和布局

- 绑定同一个 `FocusSessionViewModel.IsDarkFocusTheme`，复用现有 `FocusThemeScope`。
  设置中的浅色、暗色和跟随系统继续使用已有规则，没有增加计时器、设置或保存字段。
- 手动主题优先；跟随系统沿用本地时间：05:00–17:59 浅色，18:00–次日04:59 暗色。
- 折叠条高度仍为 50，宽度按现有任务文字计算并限制在 160–320；悬浮窗口宽度仍为 300，
  高度仍按现有任务/创建/动画规则计算。字体、行高、间距和按钮交互未改动。
- 任务列表可视上限从 3 行调整为 5 行。每行仍为 28 DIP，窗口宽度仍为 300 DIP；
  1～5 行时任务列表分别为 28/56/84/112/140 DIP，窗口分别为 189/217/245/273/301 DIP。
  第 6 行开始保留 140 DIP 的任务列表可视高度并启用滚动，任务完成或恢复时沿用原有动态缩放通知。
  父任务与子任务继续按实际占用行数计算，创建输入区仍按原规则从任务可视区中预留空间。
- 胶囊显示已有 `SubTaskProgress` 的数字部分（如 `2/3`），跟随已有子任务通知更新，不能点击。
  长任务名称继续单行省略并保留完整提示，暗色下为胶囊预留文字空间。连接线置于原有左侧空隙，
  不参与点击，也不增加行高。未增加折叠子任务的按钮或业务逻辑。
- 完成动画仅从主题资源取得文字颜色，原有时序、保存次数及完成/撤销规则不变。
- 配色资源编译为 WPF 程序集资源，安装目录无需额外文件和写入权限。

## 配色

| 用途 | 暗色资源 |
| --- | --- |
| 两种窗口的共享背景渐变 | `#252628` → `#1D1E20` → `#17181A` |
| 外边框及胶囊边框 | `#3D4046` |
| 倒计时、状态及任务文字 | `#F5F5F8` |
| 总时长和折叠任务文字 | `#A0A3AD` |
| 进度轨道 | `#303236` |
| 分隔线 | `#474A50` |
| 父任务和子任务勾选框描边 | `#A0A4AE` |
| 子任务虚线 | `#5B5E66` |
| 进度胶囊背景 | `#292B30` |

## 修改文件

| 文件 | 用途 |
| --- | --- |
| src/FocusApp.Desktop/Resources/FocusFloatingTheme.Light.xaml | 保存原有浅色颜色 |
| src/FocusApp.Desktop/Resources/FocusFloatingTheme.Dark.xaml | 集中配置暗色渐变、颜色和阴影 |
| src/FocusApp.Desktop/Views/FocusThemeScope.cs | 为现有局部主题切换添加资源前缀选择 |
| src/FocusApp.Desktop/ViewModels/FocusFloatingWindowViewModel.cs | 暴露已有 Session 供主题绑定 |
| src/FocusApp.Desktop/Views/FoldedHorizontal.xaml | 折叠条使用局部动态颜色资源 |
| src/FocusApp.Desktop/Views/FloatingContent.xaml | 悬浮窗口动态颜色及暗色只读装饰 |
| src/FocusApp.Desktop/Views/CompactSubTaskProgressConverter.cs | 将已有子任务进度转成紧凑数字展示 |
| src/FocusApp.Desktop/Views/FloatingContent.Completion.cs | 动画文字颜色取自当前主题 |
| tests/FocusApp.Tests/Desktop/FocusFloatingThemePresentationTests.cs | 浅色基准、暗色渲染、尺寸/状态及时间边界 |
| tests/FocusApp.Tests/Desktop/FocusFloatingCompletionAnimationTests.cs | 深浅色动画、动画期间切换主题后资源恢复 |
| Docs/FocusTheme.md | 更新主题范围说明 |
| Docs/FocusFloatingTheme.md | 本说明 |

## 验证（2026-10-12）

- 修改前保存了折叠条和悬浮窗口的有任务/无任务浅色 PNG 基准。修改后四种状态及切换回浅色的状态，
  均比较解码像素，确保浅色外观不变。
- 实际 WPF 渲染检查暗色有任务/无任务窗口、折叠条，检查胶囊进度更新和虚线；
  验证手动主题、18:00/05:00 自动主题边界、窗口尺寸、计时、任务及输入草稿保持稳定。
- 相关回归覆盖窗口布局、环形进度、折叠、吸附、位置恢复、任务创建、完成动画和主专注页面主题。
  共 110 个不同用例验证通过。
  位置用例会关闭全局 WPF Application，其他原生窗口用例应在独立测试进程运行。
- 完整 Release 解决方案编译成功：0 警告，0 错误。
- 本地渲染图及测试结果保存在 `.verify/floating-theme/`，该目录不会提交到 Git。

2026-10-13 的任务容量调整验证：编译成功，0 警告、0 错误；86 项悬浮窗口相关测试和单独进程中的 15 项位置测试全部通过。渲染检查覆盖 1～5 行完整显示、6 行起固定高度并滚动、完成/恢复任务时自动缩放、原有行高及字号、计时区域位置与进度值稳定，以及创建任务和折叠展开。效果图及结果位于 `artifacts/floating-five-tasks-qa/`。
