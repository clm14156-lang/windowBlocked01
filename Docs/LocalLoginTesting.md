# 本地验证注册与登录

客户端默认请求 `http://localhost:5080/`。`FocusApp.Service` 是本地 Named Pipe 服务，登录 HTTP API 来自独立的 `E:\FocusTime_Server` 项目。`Start-FocusApp.ps1` 不会启动这个 API。

## 启动服务器

在客户端项目根目录的 PowerShell 中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-LocalLoginServer.ps1
```

脚本使用后端已备好的 `.tools\dotnet`、`.tools\pgsql` 和 Release API，启动仅监听本机的 API（5080）和独立 PostgreSQL（55432）。首次自动初始化数据库并应用迁移；后续启动保留账号。后台进程没有额外窗口，关闭当前终端不会停止它们。

所有本地数据、开发认证密钥、加密数据库密码、邮件和日志均在当前客户端项目的 `.codex-build\local-login-server\`，不使用既有数据库。数据库密码以当前 Windows 用户的 DPAPI 加密保存。后端位置不同时，传入 `-ServerRoot '实际后端目录'`。缺少 Release DLL 时，先使用后端自带的 .NET 10 编译：

```powershell
cd E:\FocusTime_Server
$env:DOTNET_ROOT = "$PWD\.tools\dotnet"
& .\.tools\dotnet\dotnet.exe build .\src\FocusTime.Api\FocusTime.Api.csproj -c Release
```

检查就绪状态：

```powershell
Invoke-RestMethod http://localhost:5080/health/ready
```

返回 `status: healthy` 才能验证账号接口。仅 `/health/live` 成功不能证明数据库可用。

## 客户端验证

1. 打开客户端“注册”，输入测试邮箱，例如 `manual@example.test`，点击发送验证码。
2. 在项目根目录运行 `.\scripts\Get-LocalLoginCode.ps1 -Email manual@example.test` 读取六位验证码。也可以从 `.codex-build\local-login-server\api\.artifacts\mail\` 打开对应的 `.eml` 邮件。开发模式不发送真实邮件，脚本会自动解码邮件正文。
3. 填入验证码，设置至少 8 位且包含字母和数字的密码，勾选协议并注册。
4. 切换到登录，使用刚注册的邮箱和密码。之前的模拟账号不属于这个独立数据库。
5. 可验证错误密码、退出后重新登录、重启客户端恢复登录和找回密码。发送验证码至少间隔 60 秒；同一邮箱每小时最多 5 次。

默认地址无需修改客户端配置。若曾自定义地址，必须在启动客户端的同一个 PowerShell 中设置，然后重新启动客户端：

```powershell
$env:FOCUSTIME_API_BASE_URL = 'http://localhost:5080/'
.\scripts\Start-FocusApp.ps1
```

API 端口被占用时可以用 `-ApiPort 5081`，相应将客户端地址改为 `http://localhost:5081/`。数据库端口可用 `-DatabasePort` 调整。

## 开发环境 VIP 测试账号

`vip-test@example.test` 通过真实注册接口创建，密码以当前 Windows 用户的 DPAPI 加密保存在 `.codex-build\local-login-server\vip-test-account.dpapi`，不写入源码。

由于正式会员授权模块尚未实现，后端增加了独立的开发测试配置：API 内容目录的 `.artifacts\test-memberships.json` 中，`DevelopmentMemberships` 将指定用户 UUID 映射为 `lifetime`。仅 `Development` 环境的已验证邮箱账号生效；同邮箱的其他 UUID 不会获得授权，Production 和 Staging 均不生效。登录、刷新和 `/api/v1/auth/me` 返回 `membership` 字段；客户端从服务器读取该状态，联网恢复时重新查询，离线缓存不授予 VIP。

测试配置和账号会随本地服务重启保留。这是用于验证客户端 VIP 功能的开发授权，正式会员表、支付及离线签名权益凭证仍待实现。

## 停止和排错

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Stop-LocalLoginServer.ps1
```

停止会保留测试账号和邮件。再次运行启动脚本即可恢复。失败时检查 `.codex-build\local-login-server\` 中的 `initdb.log`、`postgres.log`、`migrate.log`、`api.log` 和 `api-error.log`。
