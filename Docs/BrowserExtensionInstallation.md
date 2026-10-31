# Chrome 与 Edge 插件安装检测

目标扩展 ID：`pmjlephefhhnghkobipdjdbilcoienkh`。当前 Chrome 与 Edge 使用同一个扩展包及 ID，包括本机通过开发者模式加载的未打包扩展；将来单独发布 Edge Add-ons 时，需要配置其实际商店 ID 和地址。

桌面端通过 `ChromeExtensionService` 与 `EdgeExtensionService` 分别异步读取当前 Windows 用户的安装记录，共用 `ChromiumExtensionService` 检测逻辑。Chrome 检查 `Google/Chrome*`，Edge 检查 `Microsoft/Edge*` 下正式版、Beta、Dev、Canary 的常规用户数据目录，以及各自用户级、机器级 `UserDataDir` 策略指定的目录；两种浏览器的安装状态独立，不互相代替。策略路径支持环境变量以及 `${local_app_data}`、`${roaming_app_data}`、`${profile}` 占位符。用户数据目录内的配置档逐一检查，不依赖插件名称，也不读取浏览历史或 Cookie。

优先读取配置档的 `Secure Preferences`，兼容旧版 `Preferences`。只有注册记录中的安装路径和对应的有效 `manifest.json` 同时存在，才判定为已安装。受到保护的安装记录优先于旧记录，显式外部卸载记录不计入安装；只有残留目录、缺失包文件不计入安装。文件被占用、无读取权限、JSON 不完整等情况返回“无法确认”，不会假装检测成功。检查等待上限为 2 秒；慢目录超时也返回“无法确认”，并复用尚未完成的后台检查，避免轮询积压线程。

打开设置中的插件弹窗时并行刷新两种浏览器的安装状态；弹窗可见期间每 3 秒检查一次，离开或卸载视图后停止轮询。各自检测成功的按钮显示绿色“已安装”徽标并停止重复安装入口；说明文字区分仅 Chrome、仅 Edge、两者已安装。开始手动专注前会先完成检测；任一种浏览器已安装则继续准备专注，否则保留安装/跳过流程。安装提示打开期间检测到任一种浏览器安装完成，会继续之前请求的专注流程且只执行一次；关闭弹窗或改为设置入口会取消之前等待的开始请求。

未安装时，点击 Chrome 或 Edge 安装按钮会分别通过 `chrome.exe` 或 `msedge.exe` 打开现有 Chrome Web Store 链接；Edge 支持从该商店安装扩展，当前没有另行配置 Edge Add-ons 地址。浏览器无法启动时在弹窗内显示对应浏览器的提示，不会启动专注。

## 检测范围

- “已安装”表示当前用户至少一个被检查的对应浏览器配置档有安装记录，不代表当前活动配置档已安装、插件已启用或屏蔽通信已连通。说明文字会提示用户在对应浏览器扩展管理中确认启用状态。
- 不检查其他 Windows 账户。仅通过命令行 `--user-data-dir` 启动、且未设置对应策略的自定义目录不在默认检测范围内。
- 浏览器尚未写入磁盘的安装/卸载变更，会在保存配置后的下一次检查反映出来。
- 不修改浏览器配置，不新增 Native Messaging Host，也不宣称已经实现插件与桌面端的运行状态握手。

目录结构依据 [Chromium 用户数据目录说明](https://chromium.googlesource.com/chromium/src/+/HEAD/docs/user_data_dir.md)。安装记录及卸载标记依据 [Chromium ExtensionPrefs](https://raw.githubusercontent.com/chromium/chromium/main/extensions/browser/extension_prefs.cc)，旧版状态兼容参照 [Chromium Extension 状态定义](https://chromium.googlesource.com/experimental/chromium/src/+/refs/tags/80.0.3953.2/extensions/common/extension.h)。

Edge 策略目录依据 [Microsoft Edge UserDataDir](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/userdatadir)，Chrome Web Store 安装来源参照 [Microsoft Edge 扩展分发说明](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/alternate-distribution-options)。
