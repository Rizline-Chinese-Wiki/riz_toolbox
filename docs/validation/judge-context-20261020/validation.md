# 判定调用上下文：原生离线验收

日期：2026-10-20。范围为本仓库 Windows x64 C++/MASM helper，不代表游戏判定服务已接通。游戏主线程安装、broker 的单一 patch 所有权、12 条原版调用源的类型和字段校验仍是接线前置条件。

## Evidence

- E-native：`fixture-result.json` 是已执行的原生 fixture 输出：15 类场景、31,261 条检查、269 次原函数、268 次 Enter/Exit、3 次异常结束、最后 inFlight=0。包括原生 SEH、C++ 异常、重入、禁用期间配对退出、四线程调用和观察者异常隔离。
- E-image：`native-proof.json` 由 `inspect-helper.py` 读取真实 PE 导出、异常表和实际机器码产生，包含 MASM Entry 的 unwind 记录、非易失寄存器保存位置及 C++ Dispatch 的异常处理元数据。`exports.txt`、`unwindinfo.txt`、`native-manifest.json` 保留原始输出及 DLL 身份。
- E-source：源码位于 `native/RizToolbox.JudgeContext/`，精确 frame/config/stats 布局、导出签名和生命周期约束见该目录 `README.md`。

复现环境是现有 VS 2019 Build Tools x64 cl/ml64。仓库根目录执行：

```powershell
./scripts/Build-JudgeContext.ps1
```

## Finding

F-entry（validated，离线）：入口在进入 C++/CLR 代码前复制原调用者 GPR/XMM、入口 RSP 和返回地址；原函数的 RCX/EDX/XMM2/R9 参数按原始位模式转发一次。SEH unwind 和退出回调在真实原生 fixture 中执行，观察者异常未掩盖原异常。

F-publication（未完成）：helper 的 Configure 必须先有真实原函数 trampoline。Dobby 发布 patch 与返回 trampoline 之间存在安装窗口，不能把“未配置时 FailFast”的桥直接当成已解决的 broker 接口。当前 DLL 未加入正式安装包，也未在游戏中启用。

## Path

E-native/E-image → F-entry → 后续由唯一 broker 安全发布 native entry，固定本体 SHA 和返回 RVA，再进入保留实际 run/chart/NoteInformation 的注册表。未知调用源不产生语义事件；原函数仍须一次转发。只有真实内部 NormalMode.GetJudge 被调用后才能生成判定 DTO，外层 no-op 不可伪造判定。

还缺：发布窗口的实现和对抗测试、C# 接线、12 条实际调用路径实测、自然结算关联，以及时间偏差单位的完整映射。当前证据不验收完整 B02/B07/B11。
