# 本轮音频/内容证据范围

范围由用户的本地 Rizline PC Mod 兼容底层任务授权。仅对已提供游戏与 dump 做只读静态核对；本轮没有启动游戏、电脑操作或改写原始游戏二进制。

| Evidence | Finding | Path |
|---|---|---|
| [静态反汇编](static-boundary.json) | 已知构建中有 NormalMode → WorkshopLevel.LoadChart 的真实编译直调及后续参数构造 | 可以把它作为固定构建受控解析挂点的静态依据；仍需实机会话确认执行和效果 |
| 同证据的 shared-setter-rejected | 原候选 setter 仅共享字段写入/屏障，不证明会经过独立 setter 挂点 | 不发布该候选能力 |
| [Hooks 检查源码](../../../tests/RizToolbox.Hooks.Tests/Program.cs)及统一构建日志 | 引用返回 ABI 原调用一次、异常/空返回直通、精确类型、重入/错线程/在途撤回的离线路径通过 | 原生委托 seam 回归，不代替游戏执行证据 |
| [AudioContent 检查源码](../../../tests/RizToolbox.AudioContent.Tests/Program.cs)及统一构建日志 | PCM、预算、三持有者、读回、播放根、内容撤回、图实例、当前图关联和审计清理通过 | 后续逐项补真实音频/谱面/延迟和完整曲包入口 |

在仓库根目录复查现有本地二进制，命令只读：

```powershell
C:/Python314/python.exe ./docs/validation/audio-content-20261020/inspect-boundary.py `
  'D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll' `
  ./artifacts/workshop-static-recheck.json
```

脚本严格拒绝未知 GameAssembly SHA-256；输出工具版本、样本哈希、指令和定位，不输出游戏资源内容。统一构建证据在 [本轮构建目录](../foundation-unified-20261020/)，不能把静态直调或 stub 测试记为 B15 完整验收。
