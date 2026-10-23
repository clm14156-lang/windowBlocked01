# Toast 白天与夜晚主题

网站/应用拦截通知 `BlockedAccessNotificationWindow` 和自动屏蔽提醒
`AutomaticBlockingToastWindow` 共用 `ToastTheme.Light.xaml`、`ToastTheme.Dark.xaml` 两套语义配色。
白天为浅蓝白色渐变卡片、深色标题与正文、灰蓝色说明和边框；夜晚保留原有深灰渐变、浅色文字及交互颜色。
网站通知的更多菜单、菜单图标、分隔线及按钮悬停配色同步适配主题。

两套主题使用原来的布局：两类窗口均为 400×180 DIP，拦截通知圆角 12、自动屏蔽提醒圆角 18，
内容边距、字体、字号、图标位置、窗口位置及原有显示/关闭动画均未改动。
浅色说明文字及小字号文字对最深背景的对比度至少为 4.5:1，自动屏蔽提醒的大号橙色数字至少为 3:1。

## 主题来源

复用现有 `BlockedAccessNotificationService.SetFocusSession` 的启动注册与退出清理入口，
将同一 `FocusSessionViewModel` 交给 `ToastThemeService`。
两个窗口构造完成后都调用 `ToastThemeService.Apply`，通过显式 Source 的单向绑定，
把专注页已解析的 `IsDarkFocusTheme` 传给现有 `FocusThemeScope`，并加载 Toast 局部资源。

Toast 不查询本地时间、系统颜色主题或问候阶段，不增加计时器或独立主题判断。
专注期间切换主题后，新通知使用新主题；当前已显示的通知、打开的更多菜单及复用的自动屏蔽窗口也随之更新。
未连接专注会话的独立预览默认使用浅色主题。通知 ViewModel 的 DataContext 保持原样。

## 调用入口检查

- 网站和应用的后台拦截事件均经 `App.ServiceConnection_AccessBlocked` → `BlockedAccessNotificationService.Show`。
- Debug 的 Ctrl+B 预览也使用同一通知服务。
- Ctrl+Q 的自动屏蔽提醒经 `AutomaticBlockingToastService.Show`，已有窗口继续复用。
- 所有窗口在构造入口统一接入主题，直接创建窗口的预览同样覆盖。
- Agent 原有的不发送系统拦截通知行为保持原样。

通知内容、触发条件、通知开关、五秒自动关闭、悬停暂停、网站请求限流、后台事件合并、
关闭及「本次专注不再提醒」命令均保持原有实现。主窗口内的导出、设置及专注结果提示不属于这两类独立通知。

## 验证

`ToastThemePresentationTests` 通过真实通知服务验证网站/应用与自动屏蔽提醒的主题切换、后续弹出、
打开中的菜单、手动主题优先、现有跟随系统状态、无会话默认配色、布局稳定性及浅色文字对比度。
原有菜单操作测试覆盖白天与夜晚两种主题，并检查自动关闭及两项命令。
相关回归结果与效果图位于 `artifacts/toast-theme-qa/`。
