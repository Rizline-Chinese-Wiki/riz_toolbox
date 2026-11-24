# 验证顺序

1. 读取 checkpoint、Runtime bridge、N2 publisher；确认旧 native-r1 source-snapshot、PE/proof、联合 12/119 和 ownership 4/15 原件存在。native-r2 没有完整 manifest，保持排除。
2. 在全新 D 盘源码与 fixture 目录续接 `SafeCandidate`，加入有限 double 超出有限 Single 转换域及真实 native SSE overflow 两项实际机器用例。
3. native-r3 严格构建与两项 2/19、联合 12/119、ownership 4/15 均通过；随后根任务发现直接比较 `float.MaxValue` 会误拒仍能舍入为有限 Single 的 double。
4. 改为 double finite → 实际 Single cast 的 finite bits → clip clamp，保留 `SafeCandidate`。新增 `double.BitIncrement((double)float.MaxValue)` 正向托管/native 对照。
5. fresh native-r4、定向托管 r2 编译；同一 r4 helper 通过定向 3/25、联合 12/119、ownership 4/15，共 19/159。旧联合及 ownership 二进制在新 helper 上实际重跑；其目标源码/PDB 当前一致性再次检查。
6. 独立核验 10 exports、实际 Dispatch 原调用恰一次、XMM0 bits 与 SEH unwind；核两对 native PE/PDB、两份 C++ 对象实际源码校验和；托管三套各五对 PE/PDB 及目标源码/fixture 的 portable PDB SHA-256 全通过。
7. 冻结 95 项源码、PE/PDB、COFF、原始结果和构建证据；归档 manifest 与报告索引。旧 r1、incomplete r2、strict-Max r3 以及证据 runner 的失败脚本/诊断保持原样，不作为最终通过包。
