# FocusApp 技术架构

## 1. 架构目标

FocusApp 采用 **桌面 UI + 后台服务 + 用户会话代理** 分离架构。

核心目标：

* 主界面关闭后，专注和屏蔽仍可继续运行。
* 强制专注、自动屏蔽和异常恢复由后台统一负责。
* UI、悬浮窗口和后台共享同一业务状态。
* 本地功能不依赖云端。
* 主题、会员和 UI 不侵入核心业务逻辑。

---

## 2. 技术基线

* 语言：C#
* 桌面框架：WPF
* UI：MVVM
* 平台：Windows 10 / 11
* 本地数据库：SQLite
* 后台：Windows Service
* 进程通信：Named Pipe
* 网站屏蔽：本地代理
* 程序屏蔽：进程检测与终止
* 日志：Serilog

不引入不必要的消息队列、微服务或复杂基础设施。

---

## 3. 项目结构

```text
FocusApp
├─ FocusApp.Desktop
├─ FocusApp.Agent
├─ FocusApp.Service
├─ FocusApp.Core
├─ FocusApp.Infrastructure
├─ FocusApp.Contracts
└─ FocusApp.Tests
```

### FocusApp.Desktop

负责所有 WPF 界面：

* 首页
* 专注页面
* 悬浮窗口
* 目标与任务
* 屏蔽管理
* 统计
* 设置
* 账号、会员、主题

只负责展示和用户操作。

**禁止：**

* 直接操作 SQLite
* 直接执行屏蔽
* 独立维护专注倒计时真相
* 仅依靠 UI 判断 VIP 权限

---

### FocusApp.Agent

运行在当前 Windows 用户会话中，负责：

* 托盘
* Windows 桌面提示
* 唤起 Desktop
* 需要用户会话才能完成的 Windows 操作

不保存核心业务状态，不决定屏蔽规则。

---

### FocusApp.Service

后台核心运行进程，是业务状态的主要权威。

负责：

* 专注开始、结束和恢复
* 强制模式
* 自动屏蔽调度
* 网站和程序屏蔽
* 当前专注状态
* 会员权限校验
* 本地数据读写
* 云同步协调
* 向 Desktop / Agent 推送状态变化

Desktop 关闭不得影响 Service 的运行。

---

### FocusApp.Core

保存纯业务规则，不依赖 WPF、SQLite 或 Windows API。

主要包含：

* 专注会话
* 目标
* 任务
* 专注记录
* 屏蔽规则
* 自动屏蔽规则
* 会员权限
* 统计规则

可独立测试的业务逻辑优先放在 Core。

---

### FocusApp.Infrastructure

实现外部能力：

* SQLite
* Windows 进程操作
* 本地代理
* 系统代理配置
* Windows 启动项
* 云端 API
* 安全存储
* 日志

Infrastructure 提供能力，不决定业务规则。

---

### FocusApp.Contracts

定义 Desktop / Agent / Service 之间的通信协议：

* Commands
* Queries
* Events
* DTO
* ErrorResult

只包含可序列化数据，不包含业务逻辑。

---

## 4. 依赖关系

```text
Desktop ─────┐
             │
Agent ───────┼── Contracts ── Service ── Core
             │                  │
             │                  └── Infrastructure
             │                         │
             └─────────────────────────┴── SQLite / Windows / Cloud
```

核心规则：

* Core 不依赖其他业务项目。
* Desktop 不依赖 Infrastructure。
* Desktop / Agent 不直接操作数据库和屏蔽模块。
* Service 不引用任何 View、Window 或主题资源。
* Infrastructure 不反向引用 Desktop 或 Service。

---

## 5. 专注状态

Service 持有当前活动专注会话。

至少保存：

```text
SessionId
Mode
StartTime
EndTime
Status
TargetId?
CompletedTasks
BlockingEnabled
```

倒计时根据：

```text
EndTime - CurrentTime
```

计算。

不能依赖 UI 每秒递减作为真实时间。

### 开始专注

```text
Desktop
↓
5 秒准备倒计时
↓
Service 校验
↓
保存活动会话
↓
开启屏蔽
↓
发布 FocusStarted
```

### 结束专注

```text
自然结束 / 普通模式主动结束
↓
Service 停止屏蔽
↓
保存专注记录
↓
更新目标 / 任务
↓
清除活动会话
↓
发布 FocusCompleted
```

### 强制模式

强制模式状态必须持久化。

Desktop 或系统重启后：

```text
Service
↓
检测未结束强制专注
↓
恢复屏蔽
↓
Agent 唤起 Desktop
↓
Desktop 根据 Service 状态恢复页面
```

---

## 6. 屏蔽架构

### 网站

```text
应用请求
↓
FocusApp 本地代理
↓
域名匹配
├─ 黑名单 → 阻止
└─ 允许 → 正常转发
```

规则：

* 按域名及子域名匹配。
* 不解密 HTTPS 内容。
* 不支持具体 URL 路径。
* 应保存并正确恢复原 Windows 代理设置。
* 存在原代理 / VPN 代理时，不应无条件改为直连。

### 程序

Service 持续检查：

* 新启动程序
* 已运行程序

命中启用的屏蔽规则后执行终止。

当前版本不使用驱动级拦截。

---

## 7. 本地数据

SQLite 保存：

* 专注记录
* 当前活动专注
* 目标和任务
* 网站屏蔽规则
* 程序屏蔽规则
* 自动屏蔽规则
* 统计数据
* 应用设置
* 当前主题
* 同步元数据

原则：

* Service 是核心数据唯一写入口。
* Desktop 不直接打开数据库。
* 关键操作使用事务。
* 数据库升级使用版本迁移。
* 不得因为升级或异常静默删除用户数据。

登录令牌等敏感信息使用 Windows 安全存储，不明文保存。

---

## 8. 账号、会员与同步

### 云端负责

* 注册登录
* 邮箱验证码
* 找回 / 修改密码
* 设备管理
* 会员状态
* 云端同步数据

### 本地负责

* 专注
* 屏蔽
* 目标任务
* 本地统计
* 离线数据保存

云端不可用时，不得影响基础本地专注和屏蔽。

### 权限

VIP 权限必须由 Service / Core 再次校验。

Desktop 的锁定状态只负责 UI 展示。

主要受限能力包括：

* 强制模式
* 自动屏蔽
* 高级统计
* 数据导出
* VIP 主题
* 高级同步

### 同步

统一由 `SyncCoordinator` 处理：

```text
读取本地变化
↓
调用云端 API
↓
处理结果 / 冲突
↓
事务写入本地
↓
更新同步状态
```

各模块不得自行实现一套同步覆盖逻辑。

---

## 9. UI 与主题边界

Desktop 使用 MVVM：

```text
View
↓
ViewModel
↓
Service Client
↓
FocusApp.Service
```

规则：

* View 只负责展示。
* ViewModel 不直接创建 Window。
* 页面和悬浮窗口共享 Service 状态。
* 临时 Hover、Toast、展开状态只存在 UI。
* 会改变业务结果的操作必须转换成 Service Command。

主题通过 `ResourceDictionary + DynamicResource` 实现。

主题只能改变：

* 颜色
* 字体
* 控件样式
* 背景资源

不得改变：

* 专注逻辑
* 屏蔽逻辑
* 会员逻辑
* 数据逻辑

详细主题结构统一参考 `THEME_SYSTEM.md`。

---

## 10. 可靠性与测试

必须保证：

* 活动专注先持久化，再报告启动成功。
* Service 重启后可以恢复未结束专注。
* 屏蔽失败不会导致 Service 崩溃。
* 云同步失败不会破坏本地数据。
* IPC 断线后重新获取完整状态。
* 日志不得记录密码、验证码和 Token。

优先测试：

1. 普通 / 强制专注状态。
2. Desktop 关闭后的持续运行。
3. Service / 系统重启后的恢复。
4. 自动屏蔽规则。
5. 域名和程序屏蔽。
6. 目标、任务和统计一致性。
7. VIP 权限不可通过 UI 绕过。
8. SQLite 迁移和数据完整性。
9. Named Pipe 断线重连。
10. 云同步失败恢复。

---

## 11. 当前架构范围

当前版本不包含：

* 移动端
* HTTPS 内容解密
* 按 URL 路径屏蔽
* 驱动级拦截
* 实时多人协作
* 未经确认的复杂自动同步和冲突合并

后续需求变化时再扩展，不提前设计。
