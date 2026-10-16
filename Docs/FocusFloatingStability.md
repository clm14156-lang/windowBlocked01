# 悬浮窗口异常退出排查（2026-10-16）

## 已确认的原因

Windows Application 日志中找到 6 条 FocusApp.Desktop 的 .NET Runtime 1026 记录，均为同一未处理异常：

```text
System.InvalidOperationException: “System.Windows.Documents.Run”不是 Visual 或 Visual3D。
System.Windows.Media.VisualTreeHelper.GetParent(...)
FocusApp.Desktop.Views.FloatingContent.IsInteractiveRegion(...)
FocusApp.Desktop.Views.FocusFloatingWindow.Window_PreviewMouseLeftButtonDown(...)
... WPF dispatcher ...
FocusApp.Desktop.App.Main()
```

发生时间（UTC+08:00）：2026-10-10 16:49:45、21:00:22；2026-10-11 03:05:46、03:06:25、03:07:08；2026-10-13 19:46:51。均有相应的 Application Error / WER 退出记录。KERNELBASE.dll / 0xe0434352 是托管异常的通用退出信息，实际可修复的问题在上面的 .NET 堆栈。

总时长文字由 `Run` 组成，真实窗口上的 `TotalTimeText.InputHitTest(new Point(1, 1))` 确实返回 `Run`。原代码在拖拽入口判断交互区域时，无条件调用 `VisualTreeHelper.GetParent`，但 `Run` 是 `ContentElement`。该异常发生在 `DragMove()` 的 try/catch 之前，因此直接逃逸到 Dispatcher。

用户报告的 21:15、21:18、00:32 没有给出日期，现有事件记录也没有对应这三个时刻的产品堆栈。以上证据确认了一个真实的拖拽退出原因，不能据此认定三次报告均来自它。尤其 Tab 那次尚无匹配堆栈。没有找到当前产品的 Application Hang 1002 记录或保留的 dump；也不能据此排除尚未捕获的卡死。

## 修改范围

- `IsInteractiveRegion` 按节点类型取父级：`ContentElement` 使用内容/逻辑父级，`Visual/Visual3D` 使用视觉父级。按钮、输入框和任务区域仍阻止拖拽，时间文字仍允许拖拽。
- `DragMove` 前重新检查鼠标按下和窗口存活状态。原有宽泛的 `InvalidOperationException` 捕获收窄到松手、窗口隐藏/卸载或 Dispatcher 关闭这些取消场景，并记录异常；其他异常继续传播。WPF 的 `DragMove` 本身会因鼠标已松开而抛出异常，参见 [WPF 8.0.31 源码](https://github.com/dotnet/wpf/blob/v8.0.31/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs)。
- 增加全局异常观察与最近操作记录；不设置 Dispatcher 的 `Handled`，也不调用 `SetObserved()`。
- 未修改 XAML、布局、配色、尺寸、动画时序、任务保存顺序或快捷键语义。

对 Tab 路由、创建、持久化和异步桥接的审计没有发现第二个可证实的根因。窗口和内容虽都收到 PreviewKeyDown，但 `e.Handled` 防止重复处理；提交有重入保护；正常持久化路径使用异步等待；焦点和滚动回调在隐藏、卸载或切换目标时取消，并检查窗口状态。没有为这些未经证实的方向增加业务重构或笼统的异常捕获。

## 后续诊断

应用发生托管异常时写入：

```text
%LOCALAPPDATA%\FocusApp\Logs\desktop-crash.jsonl
%LOCALAPPDATA%\FocusApp\Logs\desktop-crash.previous.jsonl
```

每条 JSON 包含当地时间及偏移、异常来源、是否为终止事件、完整异常和 inner exception 堆栈、线程 ID、进程 ID/路径、程序集版本、.NET 版本、位数，以及最近 64 条操作。操作覆盖窗口加载/关闭、鼠标命中类型、拖拽开始/结束、Tab、编辑器状态/焦点、提交结果和创建后的滚动。不记录任务标题或输入内容，仅记录长度/数量。

普通操作只写有界内存，不进行磁盘 I/O。异常时同步落盘以尽量在进程退出前保留信息；默认约 1MB 轮转，保留一个 previous 文件。单条超大异常保留完整内容，下一次写入轮转。日志不可写时使用 Trace 报告日志故障和原异常，不替代原异常。Dispatcher 和 AppDomain 可能分别记录同一次故障，通过时间、PID 和堆栈判断即可。

该日志不能保证捕获强制结束进程、断电、原生崩溃或没有抛出托管异常的永久死锁。若再次卡住且进程仍在，应先在任务管理器中对 FocusApp.Desktop 创建内存转储，并记录完整日期、时间和操作，再结束进程。

## 验证

- 用真实 WPF 命中进行修复前/后对照：原版 3 项输入路由测试均抛出上述异常；同一测试程序集配修复后二进制，3 项均通过。
- 新增 6 项日志测试覆盖完整异常、操作顺序/时间/线程、有界内存、并发 JSONL、轮转和不可写路径。
- 创建测试显式覆盖浅色/暗色、0/1/4/5/6/8 个任务。原测试固定断言浅色边框却默认跟随系统时间，夜间会误报；仅将测试主题固定并增加暗色用例。
- 测试项目构建通过，0 警告、0 错误。输入路由、日志、任务创建、视图模型、贴边计算、窗口布局和完成动画相关回归共 96 个用例全部通过；本机沙箱内使用独立进程内反射 runner 顺序执行 Fact/InlineData 用例，避免 testhost 通信限制。最终输出位于 `.verify/floating-stability/regressions.log`。

人工验收应从总时长文字及空白处拖动，快速按下/松开，拖到屏幕上下边缘后展开/折叠，并在 Tab 展开动画期间连续 Tab、Enter、Esc、拖动、切回主窗口或结束专注。确认每次 Enter 最多保存一项任务，按钮/勾选框/输入框仍可操作，倒计时、尺寸和位置行为保持原样。若再次退出，结合上述 JSONL 和同一日期/时间的 Windows .NET Runtime 事件定位。

本次原始事件、WER 检查结果及修复前/后验证输出保存在工作区 `.verify/crash-evidence`、`.verify/floating-crash-audit` 和 `.verify/floating-stability`（诊断产物，不纳入 Git）。
