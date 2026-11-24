# 895 包宿主启动检查

真实会话 `20261022T070251106Z-20615faf193240f4983bdd57dcae6df1`。主代理通过 `@oai/sky` 启动现有游戏启动器，观察标题、进入 MainMenu，点击游戏退出和确认按钮；随后 Get-Process 确认游戏进程结束。全程未按 Escape、未操作认证。

[runtime-smoke.json](runtime-smoke.json)及同名目录保存完整 **193 条连续事件：175 Info、16 Debug、2 Warning、0 Error**。两个 Warning 是实际设置页未就绪与曲库加载等待；原样保留。seq1 宿主启动、seq3 Unity tick 绑定、seq118–122 为 MainMenu 场景变化；18 项安装哈希与本轮 895 包一致。

本轮没有启用临时 metadata/resource probe，三个相关 marker 保持 Absent；没有安装 LOCAL 前置策略、构造 native 参数或启动 LOCAL 歌曲。此检查只证明新包宿主加载/菜单切换及正常退出，没有验收结算跳过、参数正向路径、重试或完整 B15。独立的 846 包原生 metadata GC 证据仍在[原目录](../runtime-local-metadata-20261022/README.md)，不改写成 895 的新实测。

在已停止的会话上复核，不启动/控制游戏：

```powershell
python docs/validation/runtime-local-session-smoke-20261022/freeze-smoke.py 'D:\SteamLibrary\steamapps\common\Rizline\BepInEx\RizToolbox\diagnostics\20261022T070251106Z-20615faf193240f4983bdd57dcae6df1'
```
