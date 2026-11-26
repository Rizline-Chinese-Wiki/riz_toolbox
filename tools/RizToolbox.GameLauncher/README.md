# 本地开发启动器（RizToolbox.GameLauncher）

用来绕开 Steam 启动参数导致的 Doorstop 失效问题，仅用于本地开发测试（不包含在常规发行的 17 个游戏安装文件里）。

## 为什么需要它

实测从 Steam 直接启动 Rizline 时，游戏子进程有时会继承 `DOORSTOP_DISABLE=TRUE` 和 `DOORSTOP_INITIALIZED=TRUE`，导致虽然代理 DLL 在目录里，但 CLR 和 RizToolbox 宿主根本没加载。

这个启动器只做一件事：启动新的游戏子进程时，**仅对该进程**清掉这 4 个旧的 Doorstop 环境变量，并设好游戏的 `SteamAppId`/`SteamGameId` 和工作目录。

- 保留 Steam 正常的账号与正版授权检查；
- 不改系统全局环境变量、不动 Steam、不改游戏文件，也不改 Doorstop 的配置文件。

## 使用前提

- 仅支持固定的 Steam 构建 `25977441`（启动前会校验原版 `GameAssembly.dll` 的 SHA-256，版本不对会直接拒绝启动）；
- 本机已安装 .NET 8 运行时，且 Steam 已登录。

## 构建与运行

在仓库根目录构建、写入本机路径，并跑完 8 项不启动游戏的离线检查：

```powershell
./scripts/Build-GameLauncher.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Test
```

之后先关掉正在运行的 Rizline，再双击 `artifacts/game-launcher/RizToolbox.GameLauncher.exe` 启动游戏。

- 配置文件：同目录的 `riztoolbox-launch.json`
- 最近一次启动记录：同目录的 `last-launch.json`

> 注意：`last-launch.json` 只能说明成功拉起了哪个进程；宿主是否真正初始化成功，还是要看当次生成的诊断日志和游戏内实际表现。
