# 证据时间线

所有项目均为 2026-10-31 本地已保存证据。原测试结果没有保存统一 wall-clock 起止时间，下面按修订顺序登记；不推造执行时间或将其混算。

| 顺序 | 记录 | 状态与解释 |
|---|---|---|
| 1 | native-r1、diagnostic-r1、mapped-r1/r2 | 早期隔离实现/探针；mapped-r1 的 build.log 保存 C3536/C2440/C2664 编译失败。mapped-r2 仅有 exit.json 与日志，保留历史形态，不替代当前七模式结果。 |
| 2 | machine-r1 | buildExitCode=0，actualExitCode=1，1038 checks / 29 failures；实际单步 CONTEXT 只有28，原 TF 采样未覆盖127个边界。 |
| 3 | machine-r2/r3、native-r2、mapped-r3 | 历史阶段证据；部分为成功，但源修订/测试范围不同，均不作为本次完整当前结果。 |
| 4 | native-r3 | 当前正式 helper 源与归档101项；buildExitCode=0；EAT精确85；原 ABI fixture 被构建但 fixtureExecuted=false。 |
| 5 | mapped-r4 | 当前7个实际进程全部exit0，2365 checks；副本映像真实Prepare/Commit，游戏方法/init/DllMain调用均0。 |
| 6 | machine-r4 | 当前自有机器测试2226 checks / actualExitCode=0；28 Prepare/Commit、127实际单步 CONTEXT、28原body SEH。 |
| 7 | relocated-seh-r1 | 当前自有 normalized original 内部+4处实际读取0x10导致一次AV；17 checks / actualExitCode=0。 |
| 8 | proof-r1 | 本项离线审查actual shell exit0；5组PE/PDB、31 COFF、73条PDB模块绑定、115源复核归档、9项core-r3相关源同hash；14个产物目录297文件全hash。审查本身不执行任何被审查的PE。 |

完整路径、内容哈希和当前/历史角色见 [evidence-index.json](evidence-index.json)。proof-r1 的精确 UTC 观察时间保存在 D 盘 `evidence-review.json` 的 `observedAtUtc` 字段。
