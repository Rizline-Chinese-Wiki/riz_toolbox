# 本地 Mod 开发启动器

适用于本项目固定 Steam 构建 25977441，需要本机已有 .NET 8 运行时和已登录的 Steam。它是单独的本地开发工具，不属于当前 17 文件游戏安装清单。

在仓库根目录构建、写入本机路径并执行不启动游戏的 8 项检查：

```powershell
./scripts/Build-GameLauncher.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Test
```

随后双击 `artifacts/game-launcher/RizToolbox.GameLauncher.exe`。配置位于旁边的 `riztoolbox-launch.json`，启动结果写到 `last-launch.json`。请先退出已有的 Rizline 实例。

本机实测发现，Steam 创建的游戏进程继承了 `DOORSTOP_DISABLE=TRUE` 和 `DOORSTOP_INITIALIZED=TRUE`，导致代理 DLL 存在但 CLR 和宿主未加载。启动器仅为新游戏子进程移除四个旧 Doorstop 初始化环境键，指定本游戏 SteamAppId/SteamGameId 与游戏工作目录。它保留 Steam 的正常账户和游戏授权检查，不修改全局环境、Steam、游戏二进制或 Doorstop 的持久配置。

准备启动前会检查原版 GameAssembly 的 SHA-256；未知构建拒绝启动。`last-launch.json` 仅证明创建了哪个进程，宿主是否实际初始化须核对该次新诊断 session 和实际界面。2026-10-20 的一次实际启动已恢复宿主，但不代表全部平台或 Steam 启动方式均已验收。
