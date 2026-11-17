# 专注时光 Windows 安装程序

在仓库根目录运行以下命令。编译需要项目使用的 .NET SDK 及 Inno Setup 7。

```powershell
.\installer\Publish-Installer.ps1 -Version 0.1.3 -InnoCompiler 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

固定输出目录为 `.codex-build\installer\dist`。脚本会重新发布 Desktop、Agent 和 Service，校验同名文件，打包 Windows x64 自包含安装程序。省略 `-InnoCompiler` 时会查找项目临时工具目录和系统常用安装位置。

内测环境地址就绪后，把 `-ApiBaseUrl` 指向以 `/` 结尾的 HTTPS 基址，并核对发布包的会员信任配置。没有指定地址的包沿用开发客户端的本机默认地址。

客户端默认安装到系统 `Program Files` 下的 `FocusApp`。自定义安装可选择已挂载的本地 NTFS 或 ReFS 卷，并自动创建缺失的上级目录。新目录或空目录可直接安装。已有其他文件时，安装器会确认后使用其 `FocusApp` 子目录。安全检查拒绝网络位置、虚拟盘符、目录链接、无访问权限或过长的路径，以及无法设置目录权限的文件系统。

Windows 服务和卸载程序安装到系统 `Program Files` 下仅管理员和系统可写的 `FocusApp Service`。旧版可在原客户端目录覆盖升级。升级会迁移已有服务，并在安装失败时恢复旧服务路径。要更换客户端安装位置，请先卸载旧版。升级或卸载前，从各 Windows 会话的托盘退出专注时光。卸载保留本地记录、设置和未同步数据。

在干净的 Windows 11 x64 机器上验收以下流程。

- 首次安装和自定义目录，核对快捷方式、`FocusAppService` 和普通用户启动。
- 安装中取消，核对服务、程序文件和快捷方式的清理结果。
- 覆盖升级和安装失败恢复，核对服务路径、旧版本状态和本地数据。
- 使用网站屏蔽后退出并卸载，核对原代理设置、服务移除和数据保留。

对外发布前检查受支持的运行时、安装包签名和实际 HTTPS 环境。
