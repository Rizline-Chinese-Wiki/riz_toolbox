# 七个物理 source 前缀的独立 native 验证

2026-10-31，final r14 实际 **14 进程/36675 检查、112 Prepare/112 Commit** 全部 exit 0；独立 **3 PE/PDB、5 COFF、9 fixture body** 核验通过。原型 Context V1 未改。游戏方法与 DllMain 未执行，未安装或启动游戏。

实现范围是七个各自精确入口前缀和受控原正文：两个 ALLOC20 Pause、Double1 PlayScheduled、MOV-save/PUSH-RDI/ALLOC20 set_clip、带 XMM6/晚 RDI 的 Replay、带 XMM6/晚 RBX 的 Resume，以及真实 `[self+42]` 比较/JE/晚 RBX 的专用 Progress 对象。Replay/Resume/Progress fixture 的 RF 为 **3/3/3**，游戏固定原件为 **5/5/4**；本包不声称完整生产正文等价。

- [正式报告](2026-10-31_Playback-seven-native-targets-report.md)
- [范围](scope.md)与[阶段记录](timeline.md)
- [Evidence → Finding → Path](evidence-index.json)
- [逐角色 actual Dobby/前缀/展开对照](target-proof.json)
- [原件与输入索引](artifact-index.json)
- [报告完整性封存](report-integrity.json)

只读复核（不重新执行 14 个进程）：

```powershell
& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-targets-20261031/Verify-Report.py'
```

本包不计入根任务的 9 成员 broker 或新源码整合矩阵；那些凭据由各自报告保存。下一批仍须独立处理实际 stock occurrence、连续 writer reservation、正式 provider/Clock 与跨局责任。
