# 下一阶段实施与验收

前提：1515批已冻结；原生生产发布与 managed adoption 源码已实现，未安装和运行游戏。用户实测稍后的约束保持。不能把下一步计划写成已通过。

## 1. 实际 CLR 采用（B02/B07/B19）

隔离 managed 子进程用 DONT_RESOLVE_DLL_REFERENCES 映射固定游戏 PE，真实调用 producer，再执行生产 NativeFirstInstall.RequireReady→NativeRuntimeInvokeBridge.Adopt 与 Unity bootstrap Adopt/Attach。记录实际 native Info0→1、slot31、epoch、目标与 helper 身份；重复/错误身份拒绝，无二次游戏字节写入。零调用任何游戏地址，gameRuntimeVerified=false。保留已观察 Ready 与重入后的 Unknown 两个时间点。

另以独立真实 native fixture 覆盖 native→CLR→broker→ledger 回调及 worker、旧调用跨Attach、原异常和CLR故障；受控图不能替代 IL2CPP GC。

## 2. 冷启动时序（B02/B07/B19，支持B01）

用同一 session 记录 pre-init→Ready（三处发布）→原 init 恰好一次/返回或异常→managed Attach→首合法 tick/event。先离线 wrapper 实测 native original 计数、0返回、SEH/C++与重复初始化拒绝；后续获准游戏实测时再用固定新包证明实际首次执行前的合同。缺少证据不能宣称 coldboot accepted。

## 3. 全消费者退休（B05/B08/B11/B13/B15）

沿实际 executing 的三条 listener callsite追踪 leaf、provider、异步转交和连续 mutation；不清楚的来源保持 opaque/Pending。所有 future 来源已撤回或自然结束后，组合 audio、图 roots、globals、menu/result/settings 等每个 owner 的证据，顺序执行 VerifyConsumersEnded→Close→bridge/bundle/reservation/handler/coordinator退休，最后装配同新revision的 Retry/Return/Loader。

至少覆盖正常、失败、取消、局中 provider 撤回及20轮重开：原调用一次、Unknown不释放、条件式恢复当前基线、资源计数不单调增长。完整闭合前 RequestPlay 关闭；整项勾选仍须 SPEC 的真实游戏、版本和失败矩阵。
