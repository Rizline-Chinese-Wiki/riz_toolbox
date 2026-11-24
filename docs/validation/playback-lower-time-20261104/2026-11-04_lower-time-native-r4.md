# Lower set_time 原生协议验证 R4

本批补全直接进入 AudioSource.set_time lower icall 时的识别、撤权和恢复协议。实现位于 D 盘的独立候选目录，主目录的接纳基线仍为 X952。三成员 Dobby 安装、23 个完整 Hook、正式 provider 和 SDK 恢复属于后续验证范围。

## E-001 原生实际终态

VS2019 以 /W4 /WX 编译成功，35 个独立场景全部自然退出 0，共 3597 项检查通过。实际 ABI 检查包括 Source frame 168 B、info 136 B 和独立 audio-interruption 144 B。源码、编译配方、产物、各场景 stdout/stderr、退出码与历史失败的 Facts 均已冻结。[完整终态清单](D:/riz-build-cache-next/playback-lower-time-r1-20261104/native-tests-r1/terminal-summary-r4.json)

该清单为 91,945 B，SHA256 为 `7572691366bb00b02af4c899388de5cf419659fe4e54e8e44be50c4d2ae3020e`。精确原生覆盖清单包含 12 个变化文件和 5 个相同输入，SHA256 为 `fd66c6a4f7dbc78b8d86f3dc7cd731be8200f3207e4f6770d27f1843556826ed`。[原生差异清单](D:/riz-build-cache-next/playback-lower-time-r1-20261104/native-source-r1/final-native-delta-r4.json)

## F-001 调用和权限的实际边界

lower-time 使用 role22、kind8、child flag2048。包装函数的真实尾跳转通过 helper 的实际 CALL 返回标签与 TLS 父 scope 关联唯一子调用，保留 wrapper、raw32 参数、线程、slot 和实际父子关系。包装函数与其唯一子调用只推进一次 Scheduling。

直接 lower 调用按实际 receiver 分类。同值、ABA、相关 worker、foreign receiver、观察者重入、原函数异常和物理退出均有独立场景。相关直接尝试在进入原函数前使旧权限失效，恢复依赖新的实际 Start 和已完成的物理责任。144 B typed interruption 记录实际 Scheduling stamp，恢复继续通过私有 coordinator 和原生门检查。

## F-002 重复调用漏报的修补

R3 的 duplicate-child 场景证明，一个 exact child 内再出现相关 direct 调用时，Scheduling 已额外推进，但包装函数结束时没有检查调用窗口的三个计数，因而漏报 Unknown。

R4 在 EndLowerTimeWrapperOriginal 增加 Writer、Source、Scheduling 与窗口快照相等的检查。上述反例和其余 34 个场景在新源码下均自然退出 0。R1 中夹具成功码及 policy 头初始化错误、R3 的错误 receiver 和探针用法均保留原件，修正后的结论由新进程产生。[R3 反例与 R4 修补归属](D:/riz-build-cache-next/playback-lower-time-r1-20261104/native-tests-r1/terminal-summary-r4.json)

## P-001 后续接入路径

固定静态 profile → 原生 role22 与唯一 child → 原调用前撤权 → 真实物理退出 → 新 Start 与私有恢复门。本批实际验证覆盖原生协议阶段，Dobby Prepare/Commit 的计数均为 0。完整 Progress 与 Unity backing 动态替换、三成员映像封存和正式 SDK 接入继续由各自的新验证范围验收。[合同与四阶段边界](D:/riz-build-cache-next/playback-lower-time-r1-20261104/contract-r1/contract.md)

## E-002 Managed 家族草案的回归

第三成员的 closed preflight、共享 retained owner 和原跳板/UW 候选代码已加入独立 managed 草案，共9份 Runtime 文件差异。新五项目构建自然退出0，已有两成员路径的 owned install 回归为11项检查、Prepare/Commit 各22、最终 roots=0。另一个实际负例脚本验证合法旧 frame 可接受，同时拒绝 role22/kind8 和旧 role 附带2048位的 frame。完整23成员协议接线以后再接纳新 child。[Managed 终态与精确输入](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r1/terminal-summary-r1.json)

Managed 终态清单 SHA256 为 `22ea1d16f9b51c17b21869e214072e425ad8279340b45688e0d0b171731c6a5d`。该阶段使用新构建验证已有路径，第三成员仍待新 owned 两DLL的真实文件身份、3成员 Dobby 和实际 OS unwind 验收。
