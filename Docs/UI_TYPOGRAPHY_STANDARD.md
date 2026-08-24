# UI_TYPOGRAPHY_STANDARD.md

## 1. 目的

统一整个 WPF 软件的字体规范。

AI / Codex 修改任何 UI 前必须先读取本文件。

**字体规范优先级高于参考图。**

---

## 2. 字体

优先使用：

```xml
FontFamily="Segoe UI Variable, Microsoft YaHei UI, Segoe UI"
```

说明：

* 英文 / 数字优先 `Segoe UI Variable`
* 中文回退 `Microsoft YaHei UI`
* 不使用 SF Pro、PingFang SC 等 Apple 专用字体
* Apple 风格通过层级、留白、颜色和字重实现

---

## 3. 单位

本文件所有 `FontSize` 均表示 **WPF DIP**，不是 CSS px 或 macOS pt。

---

## 4. 字号体系

普通 UI 只使用以下字号：

| 类型              | FontSize | FontWeight | 用途       |
| --------------- | -------: | ---------- | -------- |
| PageTitle       |       20 | 600        | 页面标题     |
| DialogTitle     |       17 | 600        | 弹窗标题     |
| SectionTitle    |       15 | 500        | 模块标题     |
| Body            |       13 | 400        | 正文、列表、菜单 |
| Button          |       13 | 500        | 按钮       |
| Secondary       |       12 | 400        | 次级说明     |
| SecondaryMedium |       12 | 500        | 标签、状态    |

### 规则

* 普通文字不得小于 `12 DIP`
* 不得随意使用 `11 / 14 / 16 / 18 / 19`
* 不得使用 `12.5 / 13.5` 等碎片字号
* 不得为了匹配参考图临时调整字号
* 新字号必须先加入本规范

---

## 5. 大型数字

倒计时、统计数字等使用独立 `Display / Metric` 字体，不受 `20 DIP` 限制。

例如：

```text
25:00
3h 42m
128 小时
```

具体字号由对应组件统一定义，禁止页面自行决定。

---

## 6. 字重

全项目主要使用：

```text
Regular  = 400
Medium   = 500
SemiBold = 600
```

规则：

* 正文 / 列表：400
* 按钮 / 模块标题 / 标签：500
* 页面标题 / 弹窗标题 / 重要数字：600
* 普通 UI 禁止随意使用 `700 / Bold`

---

## 7. 文字颜色

统一使用：

```text
PrimaryText   = #1D1D1F
SecondaryText = #6E6E73
TertiaryText  = #8E8E93
DisabledText  = #AEAEB2
WhiteText     = #FFFFFF
```

使用规则：

* `PrimaryText`：标题、正文、核心信息
* `SecondaryText`：时间、域名、时长、说明、状态
* `TertiaryText`：Placeholder、弱提示
* `DisabledText`：仅禁用状态
* `WhiteText`：深色 / 彩色按钮

禁止普通文字使用纯黑 `#000000` 或页面自行创建不同灰色。

---

## 8. 常见组件

```text
页面标题
20 / 600 / PrimaryText

弹窗标题
17 / 600 / PrimaryText

区域标题
15 / 500 / PrimaryText

正文 / 列表 / 菜单
13 / 400 / PrimaryText

按钮
13 / 500

输入框
13 / 400 / PrimaryText

Placeholder
13 / 400 / TertiaryText

次级说明
12 / 400 / SecondaryText

标签
12 / 500
```

---

## 9. 行高

多行文字建议：

```text
Body 13 → LineHeight 20
Secondary 12 → LineHeight 18
```

单行 UI 默认通过控件高度进行垂直居中。

---

## 10. AI / Codex 约束

1. 修改 UI 前先读取本文件。
2. 参考图只用于判断字体层级，不直接决定字号。
3. 优先使用已有 Typography。
4. 不得为了叠图临时创建字号、字重或文字颜色。
5. 相同层级在所有页面保持一致。
6. 能通过 Margin、Padding、布局解决的问题，不修改字号。
7. 新 Typography 必须先修改本规范。
8. Display 大数字不受普通字号限制。

---

## 11. 优先级

```text
全局 Typography
↓
组件规范
↓
页面设计
↓
参考图像素还原
```

**一致性优先于局部像素级一致。**

---

## 12. 核心原则

> 字体是整个软件共享的一套系统，不是逐页面设计。

> 页面只能选择已有 Typography，不允许自由创造。

> Apple 风格的核心是简洁、克制、清晰和统一，而不是使用 Apple 字体。
