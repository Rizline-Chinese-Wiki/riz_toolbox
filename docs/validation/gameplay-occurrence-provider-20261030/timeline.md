# Timeline

1. 读取 CURRENT-CHECKPOINT 顶部、NEXT-FUNCTIONAL-GAPS、既有 Clock/RateProvider/RateParticipants 与固定消费证据。
2. 明确 post-origin 不能冒充 PlayScheduled 实参；采用独立 owned native stub，在内部记录同一 double 实参与正常 Returned，再由实际 NativeRuntime 调用消费。
3. 实现实际 rooted accessor、private native epoch/source/occurrence/pitch fence、具体 producer→provider→正式 Backend/Service，以及选中 pitch 恢复/读回和失败撤回。
4. `r1`：runner 硬编码的 pwsh 路径不存在，native 进程未启动；外层实际 exit 1，原目录保留。修为从本机 PATH 解析已安装 pwsh。
5. `r2`：native/managed/test actual exit 0，170/170、零警告/错误；688 输入与 8 owned overlay hash 前后相同，原目录保留。
6. 最后审查补 actual external pitch readback、native DSP callback 回退及 source DSP 回退撤回三项验证。
7. `r3`：native/managed/test actual exit 0，173/173、零警告/错误；688 输入与 8 owned overlay hash 前后相同。独立审查随后发现未覆盖的 worker Dispose 泄漏；该目录保留为旧源码结果，不能验收修复。
8. 2026-10-31 修复：provider Source 与 actual accessor 在任何 disposed/root-list 变更之前验证 owner thread；worker 调用拒绝且不发生 native free attempt。新增实际两层 worker→owner 重试测试，要求每次七个根最终恰好释放一次。
9. 使用 fresh `gameplay-occurrence-provider-r4-20261031` 构建和执行修复后源码：native/managed/tests actual exit 0/0/0，175/175、零警告/错误；688 个 snapshot 输入与八个 owned overlay 构建前后 hash 相同。随后独立 seal 重新核 688 输入及实际 AMD64 PE32+/六 exports；完整结果与哈希见 [evidence-index.json](evidence-index.json)。
