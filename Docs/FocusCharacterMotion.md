# 专注页面角色呼吸与摇摆

`FocusFlowView.xaml` 在现有 `FocusCharacter` Image 上启用 `FocusCharacterMotion` 附加行为。
角色 PNG、Image 的 240×240 尺寸及布局位置、独立阴影和背景继续沿用现有资源。
动画使用 `RenderTransform`，不参与布局计算；底部坐姿锚点保持固定。

## 集中参数

参数位于 `src/FocusApp.Desktop/Views/FocusCharacterMotion.cs` 文件顶部。

| 参数 | 值 |
| --- | --- |
| 呼吸 ScaleX | 1.000 → 1.004 → 1.000 |
| 呼吸 ScaleY | 1.000 → 1.008 → 1.000 |
| 呼吸完整周期 | 4 秒 |
| 摇摆角度 | -0.3° → +0.3° → -0.3° |
| 摇摆完整周期 | 5 秒 |
| 变换中心 | (0.5, 0.85) |
| 缓动 | SineEase / EaseInOut |
| 原生动画期望帧率 | 30 FPS |

每条动画以半周期为 Duration，使用 AutoReverse 和 RepeatBehavior.Forever。
关闭效果可将 XAML 中的 `views:FocusCharacterMotion.IsEnabled` 设为 `False`，
或调用 `FocusCharacterMotion.SetIsEnabled(image, false)`；关闭时移除时钟并恢复原始变换。

## 生命周期与资源

- 每个 Image 缓存一套 TransformGroup、Storyboard 和三条 DoubleAnimation。
- 显示且已加载、窗口可见且未最小化、Source 非空时运行；隐藏、折叠、最小化或 Source 为空时暂停，恢复显示时继续原进度。
- Image 卸载或窗口关闭时移除动画时钟、解绑 Source 观察绑定并解除窗口事件；再次加载时复用原 Storyboard。
- PNG 切换仅更新现有 Image.Source，复用动画时钟与变换，不新增素材、不重复加载或生成图片。
- 使用 WPF 原生时钟，不增加逐帧事件、Timer、Storyboard 的重复创建或 BitmapCache；保留原有 HighQuality 图片缩放设置。

## 验证

`FocusCharacterMotionTests` 在真实 WPF 窗口中检查深浅角色的动画数值、固定锚点、静止阴影、素材与布局，
并检查 40 次切换 PNG、暂停/恢复、结束及重新开始专注、卸载/重新加载、关闭/重开动画和窗口关闭后的时钟清理。
30 次创建及关闭页面后，通过弱引用与 GC 检查 Image 和动画状态可以回收。
动画数值验证包含原生时钟定位到 2 小时的时间点；这不等同于真实运行 2 小时的 CPU/内存压力测试。
现有主题与落叶测试的角色位置断言改为检查布局区域，其他 UI 的位置断言继续保留。
