# 专注页面落叶动画

## 素材及图层

直接使用 `src/FocusApp.Desktop/Assets/Images/focusPage/` 内的五张透明 PNG：

- 暗色：`dark_yezi01.png`、`dark_yezi02.png`。
- 浅色：`white_yezi01.png`、`white_yezi02.png`、`white_yezi03.png`。

现有项目已通过 `Resource Include="Assets\Images\focusPage\*.png"` 将这些素材打包为 WPF 程序集资源。
两套主题字典提供 `FocusSceneLeaves` 图片数组，使用 `/FocusApp.Desktop;component/Assets/Images/focusPage/文件名` 加载。
生成时在当前数组中随机选取，不修改素材或依赖安装目录的文件访问。

检查了 `focus_dark.png` 和 `focus_white.png`：背景中没有静态叶子。
原画面的两片固定叶子来自 `FocusLeafLeft`、`FocusLeafRight` 独立 Image，现已移除并由动画层替代。
新增 Canvas 放在背景之上、问候/计时/角色/操作控件之下，裁剪到页面实际边界且不参与鼠标命中。
原有角色、角色阴影、布局、任务交互、计时及主题时间规则保持原样。

## 原生动画

每片叶子独立随机设置：尺寸 20–38 DIP、持续 10–18 秒、最高透明度 0.65–0.9、
横向摇摆幅度 10–30 DIP、摇摆周期 2–4 秒、倾斜幅度 15–40 度。
初次出现等待 0.4–1.3 秒，后续每隔随机 3–7 秒尝试生成，活动叶子最多三片。

使用 WPF Storyboard 和关键帧动画叠加：

1. 基础平移从实际页面的右上区域飘至左下边界之外，横纵进度各自加入小幅正弦风速变化。
2. 独立的横向摇摆和轻微纵向浮动叠加在平移之上。
3. 慢速倾斜随风改变旋转方向，并加入轻微横向缩放模拟翻转；不进行高速整圈旋转。
4. 入场约 0.9–1.6 秒淡入，结束前约 1.8–2.6 秒淡出。

曲线在生成时每 0.25 秒采样一次，播放和插值由 WPF 动画时钟执行，目标属性通过图片的变换属性路径绑定。
原生动画限制为 30 FPS；仅缓存最多三片小尺寸叶子，没有逐帧 C# 回调或每片叶子的永久定时器。

## 生命周期

- 全层只有一个 DispatcherTimer，负责下一次随机生成。
- Storyboard 完成后移除动画时钟、Image 和活动记录；数量达到三片时跳过此次生成。
- 切换主题立即替换现有叶子的素材以匹配新主题，动画层用 250 毫秒柔和淡出，
  清理旧运动后再生成新叶子，入场继续淡入。快速连续切换会取消上一段过渡。
- 页面不在专注状态、窗口隐藏/最小化/关闭、页面卸载时停止生成并清理全部动画。
  页面恢复显示后重新生成，不补播隐藏期间的叶子。
- 实际页面尺寸改变时清理旧轨迹，按新尺寸计算下一批轨迹；不会增加窗口尺寸或影响布局。
- 卸载或关闭时解除宿主窗口事件订阅，不保留关闭窗口的引用。

## 本次修改文件

| 文件 | 作用 |
| --- | --- |
| src/FocusApp.Desktop/Views/FocusLeafLayer.cs | Canvas 动画层、生成、主题过渡和生命周期清理 |
| src/FocusApp.Desktop/Views/FocusLeafMotion.cs | 独立随机轨迹和 WPF 原生关键帧动画 |
| src/FocusApp.Desktop/Views/FocusFlowView.xaml | 插入动画层并移除原有固定叶子 |
| src/FocusApp.Desktop/Resources/FocusTheme.Light.xaml | 三张浅色叶子资源数组 |
| src/FocusApp.Desktop/Resources/FocusTheme.Dark.xaml | 两张暗色叶子资源数组 |
| tests/FocusApp.Tests/Desktop/FocusLeafAnimationTests.cs | 轨迹范围、实际运动、主题、回收及生命周期测试 |
| tests/FocusApp.Tests/Desktop/FocusThemePresentationTests.cs | 更新叶子图层检查，保留布局及主题交互回归 |
| Docs/FocusTheme.md | 补充动画层说明 |
| Docs/FocusLeaves.md | 本说明 |

## 验证（2026-10-12）

- 三种页面尺寸下生成 360 组独立轨迹，验证方向、参数范围、非匀速、旋转换向及淡入淡出。
- 在原生 WPF 窗口中验证实际平移/摇摆/旋转属性正在动画，主题对应素材、三片上限、
  飞出后清理、切换主题、最小化/隐藏/卸载/关闭及恢复。
- 连续 100 轮完成约 300 片叶子的动画，控件和活动记录清零；弱引用回收检查通过。
  此项是加速生命周期压力检查，不等同于数小时的人工运行观察。
- 相关 107 项回归测试全部通过，覆盖专注计时、问候、布局、角色/阴影、结束确认、主题与悬浮窗口。
- 完整 Release 解决方案编译成功，0 警告、0 错误。
- 渲染图和测试结果保存在 `.verify/leaves/`（不提交 Git），深浅色截图用于检查图层和素材，
  动画本身由运行时属性及原生时钟验证。
