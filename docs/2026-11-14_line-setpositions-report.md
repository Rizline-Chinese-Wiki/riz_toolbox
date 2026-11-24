# 精确 SetPositions 入口离线接入验收

2026-11-14。本批主树 913 输入已接受。完整 SDK/B01–B19/真实 Speed/Seek 仍未完成；本批未安装或启动游戏，实际 loaded GameAssembly/body admission 和 Unity renderer 效果仍未验收。

## 新能力

[Runtime 入口](../src/RizToolbox.Runtime/NativeRuntime.LineSetPositions.cs)只绑定 LineInformation.SetPositions(Vector3[],Int32) 的精确声明、MethodInfo2022/模块/RVA 0x3fb160，验证 canonical Vector3/数组 rank/12-byte stride、Int32 和 Void ABI、原 line/array 根、实际容量与存储权限。它使用 il2cpp_runtime_invoke 的 exception-out 传输，保留原根与 entered/returned/未知临时清理责任。不会选用一参数重载，也不直接调用 generated 函数指针。

[正式 profile](../src/RizToolbox.Adapter/Gameplay/NativeSeekLineSetPositionsNativeProfile.cs)绑定固定 image fingerprint 和完整 288-byte wrapper SHA；当前编译已验收，其真实游戏模块放行仍待后续实测。beforeEntry 先发布责任，再做最终 profile/source/root/storage 核对。方法元数据出口之前即核 MethodInfo 可读范围；worker 在回调前拒绝。

核对前一候选时发现 profile 首个 guard 先于重入 gate；v4 改成先占用 gate 再回调。新回归证明同步重入在第二次 guard 前被拒绝，调用方撤销释放 gate 而不粘性 fault。v3 及其八项旧结果保留，未用来验 v4；主树实际合入 v4 五个 C# 文件。Build.ps1 另只修正既有 source 范围注释 19→30，不改实际范围。

## Evidence → Finding → Path

| Evidence | Finding | Path |
| --- | --- | --- |
| [正式新主图结果](validation/line-setpositions-runtime-20261114/run/result.json)，SHA `f1efcca1703aad1491635ca373d07fa571aa222a9d3d23d253aa6f96570746f4` | 六 Release W0/E0、solution 0E（55 项既有测试警告）、5 个自然步骤通过 | 主树 913 新产物 |
| [九项专项日志](validation/line-setpositions-runtime-20261114/run/setpositions9.log) | 八项 synthetic-image 原生导出/invoke seam 与一项仅 managed profile gate 通过 | 原数组/显式数量、ABI/RVA、不可读 MethodInfo、容量、回调漂移、字段保存后 exception-out、worker、精确 pin free 失败、guard 重入/撤销 |
| [默认 Runtime 日志](validation/line-setpositions-runtime-20261114/run/default375.log) | 375/375 自然通过 | 九项专项与默认回归重叠，分开记录，不相加 |
| [原 reader 七项](validation/line-setpositions-runtime-20261114/run/reader7.log) | 原数组前缀/尾部/恢复、外部目标隔离、异常与未知清理保持通过 | 原 reader/source/owner 的新产物回归 |
| [root 终态](validation/line-setpositions-runtime-20261114/run/root-exit.json) | 原 session45588 实际 exit0/chunk5c65f9 | 不另称 kernel handle witness |
| [归档摘要](validation/line-setpositions-runtime-20261114/validation-summary.json)，SHA `41cf05aa4db2cf4f6040570a383a82efdf82de6c0a498793e84ec6b246a9f20f` | 1004 文件/39,709,791 bytes；913 输入、完整日志/控制、六生产 pairs、两测试直接输出/依赖、fixture、静态片段、实际 shadow payload 和 v3 历史均冻结 | 原子发布 exit0/c1f9de，直接归档脚本 archive-only exit0/ca48a4 |

managed gate 用例仅通过不执行构造器的 profile 和始终抛出的 guard 验证回调边界，始终在 module/body 操作前停止；没有冒充真实模块放行。其余八项运行 production signature/invoke 路径及 unmanaged IL2CPP fake exports，synthetic SEC_IMAGE 只供模块/RVA 内存检查，未执行其映射机器码。新主图的九项不能称真实游戏九项。

根目录复核命令：

```powershell
python docs/validation/line-setpositions-runtime-20261114/control/publish.py --check-only docs/validation/line-setpositions-runtime-20261114
```

归档复核把源码、原始 PASS 名称、产物、controls、root 终态、shadow/main payload 和静态字段/完整 wrapper 记录重新绑定到固定 SHA；不读取原 C:/D: 运行路径，拒绝 archive 内 symlink/reparse point。

## 尚待实现

这个入口尚未接进原 reader 的 saved `_positionsPlan@0x88`、count@0x90、`lineRendererPlan@0x48`/nested plan 观测与有序 consumer。下一步从同一原 reader 和原 endpoint/scratch owner 接入这些字段、正式 provider 与精确 dispatch，验证两条不同 Line 共享同一 scratch、不同 count 的尾部保留、重复 occurrence、cull 不写、inactive plan、字段已发布后抛出和选定恢复。

不能为每条 Line 新建数组、假定容量 101、把 cull 变成 count0 或调用整段 producer。实际 entered/未知效果必须留在同一 owner/journal，恢复仅处理可识别的本工具修改，不声称撤销 Unity 外部 consumer。原 [renderer 交接](../work/line-renderer-next-20261114/HANDOFF.md)仍有效。Ring 初始化、Hold engine、DSP/pitch 与完整时钟事务仍未完成；913 未重新打包，原 README 和 881 预览 ZIP 保持原字节。
