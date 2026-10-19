# 添加应用弹窗的最近运行列表

## 根因

`App.CreateBlockingPageViewModel` 原先注入了 Chrome、Edge、微信、Steam 等固定路径，并用启动时间减去固定分钟数生成显示时间。`AddProgramModalViewModel.Open` 只重置搜索和选中状态，没有查询 Windows；`RecordProgramRun` 也没有接入系统运行事件。因此真实的新启动程序不会进入列表。

## 数据来源

生产入口改为注入 `WindowsRecentProgramSource`。每次打开弹窗都会重新在后台读取：

- 当前用户的 Explorer UserAssist 启动历史，解码 ROT13、已知文件夹 GUID 和已存储的 FILETIME。只接受已知的 16/72 字节记录格式，不生成时间或示例项目。
- 当前用户 SID 的 BAM 运行记录，兼容当前和较早的注册表位置，把 NT 设备路径转换为实际盘符路径。
- 当前会话的可见应用窗口及其真实进程启动时间，通过有限查询权限读取可执行文件路径；同时识别 ApplicationFrameWindow 的实际应用子窗口，过滤被 DWM 隐藏的窗口。

三种来源合并后规范化路径、忽略大小写去重，保留同一路径最新的真实时间并倒序显示。文件必须存在且为 `.exe`；历史条目须是 GUI 可执行文件，或能由当前可见应用窗口确认。过滤系统宿主、后台进程、无效时间及失效路径；Windows 目录中的历史程序使用用户应用白名单，保留记事本、画图等真实应用。名称读取程序版本信息，缺失时使用文件名。

读取均为只读操作，不更改系统追踪设置、注册表、历史记录或用户控制规则。不依赖管理员权限；受保护、已退出或正在被删除的进程/文件会被单独跳过，其他来源继续工作。Windows 未记录、已清理或受权限保护的历史无法凭空恢复，当前有窗口应用仍可从实时进程获取。

格式和接口参考：[UserAssist 格式研究](https://github.com/libyal/winreg-kb/blob/main/docs/sources/explorer-keys/User-assist.md)、[BAM 格式与验证](https://github.com/Psmths/windows-forensic-artifacts/blob/main/execution/bam-dam.md)、[EnumWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows)、[QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)。UserAssist/BAM 为尽力读取的系统历史，单条格式不认识时跳过。

## 刷新和交互

每次打开都会发起新的异步查询，并取消上一次请求；关闭后的结果和旧请求不能覆盖新列表。保留原有窗口、搜索、手动选择、保存和图标加载功能。新结果整批发布，避免逐条通知导致重复加载图标；保留仍存在的条目对象及已加载图标。扫描期间用户明确选择的程序不会被扫描结果清掉，下一次打开仍以新的系统查询为准。

## 验证

- 编译通过，0 警告、0 错误。
- 48 项相关检查通过，覆盖历史时间格式、设备路径、过滤、排序、去重、重复打开、异步取消、手动选择竞争以及现有控制列表交互。
- 使用真实独立 Windows 应用窗口验证，未向来源注入示例数据：首次运行后重新打开弹窗可识别；再次运行同一可执行文件会更新运行时间且只显示一项；结束测试进程并删除其测试文件后，重新查询会移除失效路径。测试子进程和临时可执行文件已清理。
- 本地验证程序和结果位于 `.verify/recent-programs/`，不提交到 Git。
