# 专注页面深浅色主题

## 行为

设置 → 专注外观 → 专注期间主题中的浅色、暗色、跟随系统均已接通。
浅色和暗色始终按用户选择显示；跟随系统使用电脑的本地时间，不读取 Windows 应用颜色主题，不调用网络时间。

| 本地时间 | 原有问候阶段 | 跟随系统的主题 |
| --- | --- | --- |
| 05:00–11:59 | 早上好 | 浅色 |
| 12:00–17:59 | 下午好 | 浅色 |
| 18:00–22:59 | 晚上好 | 暗色 |
| 23:00–次日04:59 | 夜深了 | 暗色 |

问候继续使用原有的阶段判断和 TXT 随机文案。切换主题不会重新抽取副文案；
跨问候阶段时仍只更新一次标题、图标和副文案。

页面保留 800×710 尺寸，两套主题共用同一套布局。问候移至左上方，倒计时上移，
底部操作区下移，删除橙色圆点及“专注中”展示。男孩、阴影和落叶使用现有透明 PNG，
角色和阴影保持居中并使用等比缩放。TodoList 保留 270×710 尺寸、原来的布局和交互，仅替换颜色。

## 实现和保存

- `FocusThemeMode` 提供 System、Light、Dark 三种设置，默认为跟随系统。
- 设置选择立即更新当前专注 ViewModel，并通过原有设置 IPC 和保存队列写入后台 SQLite 数据库。
  快速连续切换时，较早的保存响应不会覆盖最新选择；收到启动快照或创建已连接的新窗口时恢复已保存主题。
- 数据库增加 `app_settings.focus_theme`，版本从 11 升至 12，旧数据库自动迁移并默认 System，
  保留原有设置、目标、任务和记录。数据库仍位于现有 `%PROGRAMDATA%\FocusApp\Data\<用户作用域哈希>\focusapp.db`。
  桌面和后台服务需要使用同一次构建的产物。
- 主题资源集中在 `FocusTheme.Light.xaml` 和 `FocusTheme.Dark.xaml`。
  `FocusThemeScope` 仅替换专注页面、任务侧栏及任务菜单的资源字典。
  共享任务样式保留浅色默认资源，供悬浮窗口等现有界面继续使用。
- 复用专注刷新及主窗口原有的后台维护计时器检查本地时间，没有为主题创建新的计时器或事件订阅。
  结束确认暂停倒计时时，维护刷新仍能更新跨阶段的主题。
- 本次没有添加角色切换功能；专注角色设置行保持原来的展示。

## 使用的素材

均来自 `src/FocusApp.Desktop/Assets/Images/focusPage/`，文件名未改动。

| 用途 | 浅色 | 暗色 |
| --- | --- | --- |
| 背景 | focus_white.png | focus_dark.png |
| 男孩 | white_boy01.png | dark_boy01.png |
| 阴影 | white_shadow.png | dark_shadow.png |
| 左落叶 | white_yezi01.png | dark_yezi01.png |
| 右落叶 | white_yezi02.png | dark_yezi02.png |

原有问候图标继续使用 `focus_sun.png`、`focus_yewan.png`、`focus_yeshenle.png`。
没有缺失素材，也没有重新绘制背景。

## 本次修改文件

以下为本次主题任务的修改清单，不包含此前问候任务和工作区已有的其他修改。

| 文件 | 修改内容 |
| --- | --- |
| src/FocusApp.Core/FocusThemeMode.cs | 新增三种主题设置 |
| src/FocusApp.Core/LocalDataModels.cs | 本地设置保存主题 |
| src/FocusApp.Contracts/IpcOperations.cs | 设置 DTO 增加主题字段 |
| src/FocusApp.Service/LocalDataContractMapper.cs | IPC 与本地模型映射 |
| src/FocusApp.Infrastructure/Persistence/SqliteDatabaseInitializer.cs | 数据库版本 12 升级 |
| src/FocusApp.Infrastructure/Persistence/SqliteLocalDataStore.cs | 主题读取与保存 |
| src/FocusApp.Desktop/FocusApp.Desktop.csproj | 打包现有 focusPage 图片资源 |
| src/FocusApp.Desktop/Resources/FocusTheme.Light.xaml | 新增浅色颜色和图片资源 |
| src/FocusApp.Desktop/Resources/FocusTheme.Dark.xaml | 新增暗色颜色和图片资源 |
| src/FocusApp.Desktop/ViewModels/FocusSessionViewModel.cs | 主题选择与原有问候阶段关联 |
| src/FocusApp.Desktop/ViewModels/SettingsPageViewModel.cs | 主题选择命令和状态 |
| src/FocusApp.Desktop/ViewModels/MainWindowViewModel.cs | 即时应用、保存、恢复及维护刷新 |
| src/FocusApp.Desktop/ViewModels/FocusTaskDrawerViewModel.cs | 暴露现有 Session 供颜色绑定 |
| src/FocusApp.Desktop/Views/FocusThemeScope.cs | 新增专注局部资源切换 |
| src/FocusApp.Desktop/Views/FocusFlowView.xaml | 统一布局、素材、文字和图标配色 |
| src/FocusApp.Desktop/Views/FocusTaskDrawer.xaml | TodoList 颜色及菜单主题绑定 |
| src/FocusApp.Desktop/Views/FocusTaskResources.xaml | 共享任务样式颜色资源化 |
| src/FocusApp.Desktop/Views/TargetIconSourceConverter.cs | 目标 SVG 图标跟随文字颜色 |
| src/FocusApp.Desktop/Views/SettingsPage.xaml | 三个选项、当前标签和选中标记 |
| src/FocusApp.Desktop/Views/SettingsPage.xaml.cs | 选择后关闭主题下拉菜单 |
| tests/FocusApp.Tests/Desktop/FocusThemeViewModelTests.cs | 新增边界、手动优先级、状态保持测试 |
| tests/FocusApp.Tests/Desktop/FocusThemePersistenceTests.cs | 新增连续保存和窗口恢复测试 |
| tests/FocusApp.Tests/Desktop/FocusThemePresentationTests.cs | 新增主题渲染、布局、任务草稿、菜单测试 |
| tests/FocusApp.Tests/Desktop/FocusPagePresentationTests.cs | 更新布局/配色断言，保留原问候检查 |
| tests/FocusApp.Tests/Infrastructure/SqliteLocalDataStoreTests.cs | 升级测试适配及新增主题保存测试 |
| tests/FocusApp.Tests/Service/NamedPipeCommunicationTests.cs | 验证主题通过实际设置 IPC 保存 |
| Docs/FocusTheme.md | 本说明 |

## 验证（2026-10-12）

- 相关回归测试 **234 项全部通过**：时间边界、手动优先级、旧数据库迁移、
  三种主题数据库重开、IPC 设置传输、连续切换、窗口恢复、原问候/计时/任务/设置逻辑及 WPF 页面渲染。
- 实际渲染 800×710 专注页与 1070×710 带 TodoList 的页面，确认两套主题区域边界一致，
  图片可加载且等比显示，切换前后计时、问候、任务数量和未保存草稿保持稳定；
  任务菜单和编辑面板也随主题切换。全部 40 句默认问候继续完整显示，超长自定义文本沿用单行省略。
- `dotnet build FocusApp.sln -c Release --no-restore --disable-build-servers` 成功，
  **0 警告、0 错误**。
- 桌面 Release 发布成功，输出 `.verify/focus-theme/publish/`；
  发布程序集验证确认两套主题字典、页面、任务侧栏、设置页、十张使用的 PNG 和原问候模板均已嵌入。
- 最终测试结果：`.verify/focus-theme/results/theme-regression.trx`。
  实际渲染预览：`.verify/focus-theme/previews/`。
  发布资源检查脚本：`.verify/focus-theme/VerifyPublishedTheme.ps1`。
- 本次运行的是相关回归测试，未重新运行全量测试。此前全量测试的已有失败和 WPF 环境干扰记录见 `Docs/FocusGreetings.md`。

所需素材齐全，本次要求均已实现。
