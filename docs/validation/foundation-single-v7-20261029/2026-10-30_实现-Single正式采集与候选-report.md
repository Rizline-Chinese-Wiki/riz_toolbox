# Single 正式采集与候选冻结

本批实现固定构建的 `UnityEvent<Single>.Invoke` 原生发布、同步执行列表及三个不同 ABI 的逐监听器采集，并让 C# 从实际加载的私有发布者采用这些入口。公共错误合同、N2 有值请求与时间单位转换、N4 资源持续绑定一并进入当前冻结源码。25 套托管检查、两个独立插件、原生映像发布和候选安装检查全部通过。真实游戏首次准入、非空监听器派生效果的完整结束、Speed/Seek 原生事务以及 N3 游戏专用外观仍未全部完成。本批没有游戏运行或真实安装。

## 范围与冻结身份

用户授权继续桌面 `riz_toolbox` 的 PC Mod 公共底层实现，代码优先，性能后置，真实游戏实测稍后。这里的 `flavor=null`：普通二进制适配和工程验证，不涉及恶意软件或攻击结论。

固定输入为 `D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll`，SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。原生 helper SHA256 `30ec8ed7245432ff81490cc346fb13d03dca060bb0ef9575f90e2a3078779bde`。当前唯一 publication getter 为 V7，总包 2528B，包含偏移 2112 的 416B Single section；26 operation、64 次实际写入、22 source、83 个导出。

候选 `artifacts/foundation-single-v7-candidate-r2-20261029` 的 24 载荷、31 证据逐文件匹配 manifest。其完整来源为冻结 core-r1，而后续工作区中的订阅生命周期修改没有混入候选。冻结结果不能验证这些后来编辑。

## 实现变化

1. 正式首次发布覆盖 Single 的七个精确 source span，分别保留真实原调用返回 PC。完整 647B 原函数、四 RF/xdata/pdata、动态 4096B allocation、三 RF 和完整 36B unwind 参与封印。每段实际尝试和成功分别计数，部分发布进入永久 Unknown 并保留已产生的责任。
2. C# 的 `NativeFirstInstall` 独立重建全部 4096B，核七个 patch、relay、RF、xdata、CC padding 及 OS 注册，再采用当前私有 owner。复制的 DTO 不能授权发布、配置或代替 Ready。
3. 托管桥调用同步 `ReadCurrent`，核物理父、native sequence、managed token、raw float bits 和三种独立 ABI。event/list/invokable/object[] 先取得实际根，再进行元数据访问；实际 backing array 和全容量元素也纳入持有责任。逐监听器结果与实际创建的子 occurrence 关联。
4. 本批完整快照包含 N2 有值请求的准备、提交、确认及独立恢复读回，固定构建的只读谱面坐标↔秒转换，以及 N4 的作用域持续绑定、目标重建与资源撤回。

## Evidence

证据 ID 与完整 SHA256 以 [evidence-index.json](evidence-index.json) 为准。下表目录均位于 `D:/riz-build-cache-next/`。

| ID | 实际结果 | 冻结目录 |
|---|---|---|
| E-core | Audio 990、协调器 28、receiver 10 | `foundation-single-v7-core-r1-20261029` |
| E-full | 25 套、2233/2233；含 Gameplay 60、Visual 97、Hooks 210、Single 42 | `foundation-single-v7-full-r1-20261029` |
| E-release | 六 DLL、0 错误、1 条既有 generated nullable 警告 | `foundation-single-v7-release-r1-20261029` |
| E-clr | 同最终 helper 两个独立进程、554 检查；真实 Single observer Attach、128 容量、重复采用和 CLR GC 后仍已知空闲 | `foundation-single-v7-adoption-r2-20261029` |
| E-sdk | 引用包生成两个独立插件并经正式宿主加载，14/14 | `foundation-single-v7-distribution-r1-20261029` |
| E-native | strict build、83 EAT；自有 ABI 31261、实际 Dobby 自有函数 61358 检查 | `single-v7-native-a-r3-20261029` |
| E-profile | 固定 PE 的完整 Single 函数、四 RF 和七 span | `single-v7-profile-a-r2-20261029` |
| E-mapped | 182 独立映像进程、256673 检查 | `single-v7-mapped-a-r4-20261029` |
| E-partial | 997 Unknown 检查及七个真实部分写入子进程 | `single-v7-unknown-a-r4-20261029` |
| E-sources / E-refs | fresh 13/1625434 与 9/21991；按已改变的 retirement 源码重编 | `single-v7-sources-a-r1-20261029` / `single-v7-references-a-r1-20261029` |
| E-retirement / E-admission | 18/669 与 4/359 | `single-v7-retirement-a-r1-20261029` / `single-v7-admission-a-r3-20261029` |
| E-native-single | 自有真实 Single 调用、42 modes / 33792 检查 | `single-v7-single-event-a-r1-20261029` |
| E-package-negative | 32/32；变异、还原及候选不变核对 | `single-v7-package-negative-r1-20261029` |
| E-install | 26/26；临时目录安装、恢复、旧 helper 与篡改拒绝 | `single-v7-package-install-r1-20261029` |

E-mapped 的 182 进程为原有 41、全部 source span 共 53 字节逐字节篡改、80 个动态 block 边界样本及 8 个保护变更。全部 4096B 逐字节完整性比较由生产实现及独立重建完成；80 个故障样本不等于穷尽 4096 次篡改。

E-partial 使用实际生产 `FirstInstall.cpp` 加有界 compile-time seam，与同 helper 构建的 21 个真实 OBJ 链接；它不是向生产 DLL 注入故障。七个子进程分别在真实成功写入第 1–7 段后触发重入，保留 outer writes 36–42、Single complete 1–7、mask 1–127 和全部资源。原始 result、manifest 及其 Base64/SHA256、源码及 OBJ 来源完整嵌入 E-mapped。没有声称覆盖每一种 OS 写入失败。

E-clr 执行的是实际发布者读取和 observer 采用，映像目标未被调用。E-native-single 执行自有目标并核 `ReadCurrent`；新托管 42 项包含受控 frame、实际 NativeRuntime 根和非托管列表夹具，尚不能解释成真实游戏的端到端回调通过。

## Findings 与 Path

| Finding | Evidence | 结论及状态 |
|---|---|---|
| F-publication | E-native、E-profile、E-mapped、E-partial | validated(native mapped)：七段正式发布与实际部分进度参与同一永久 owner。生产 Unknown 不因还原字节而消失。 |
| F-adoption | E-clr、E-full | validated(owned/controlled)：当前 C# 布局、独立内存核验及真实只读采用匹配最终 helper。128 条保留记录与 32 层嵌套深度已区分。 |
| F-capture | E-native-single、E-full | validated(分项)：同步当前 frame 与三 ABI 采集、根先行及逐调用事实已接线；真实游戏 metadata/回调及非空 effects 结束仍待完成。 |
| F-package | E-package-negative、E-install、E-sdk、E-release | validated(synthetic)：同冻结来源候选通过生成、独立验包、模板插件与临时安装恢复，不能声明真实游戏已安装或可发布。 |

P-publish，`path_type=callflow`：固定 PE/body/RF → helper 私有配置及真实代次 → 动态 block/RF 完整核验 → 七段实际写入 → 完整 Ready 或保留 Unknown。对应 F-publication。

P-capture，`path_type=callflow`：私有 V7 owner → 只读实际采用/Attach → 同步 ReadCurrent → 物理父与 token 核对 → 对象全部取根 → 精确列表/三 ABI 元数据 → 逐调用与子 occurrence。分项证据支持 F-adoption/F-capture；尚缺的端到端事实没有由受控布尔值代签。

P-distribution，`path_type=artifact`：core-r1 冻结 → 25 套与六 DLL → 同最终 helper 的 CLR proof → 候选 r2 → 独立变异拒绝及临时安装恢复。对应 F-package。

## 重现与后续

原生精确命令、边界样本及工具来源见 [P3 native 记录](../../../.docs/foundation-expansion/TWEEN-SINGLE-MID-NEXT.md)。托管同步源的独立重现命令见 [托管报告](../foundation-single-managed-source-20261029/README.md)。整个批次的原始构建命令与参数保存在相应 D 盘目录；对后续 live 源码重跑将产生新批次，不能覆盖本批结果。

失败原件保留：首轮容量断言混用了 32 嵌套深度；修为实际 GetInfo 的 128 后重新验证。打包 r1 使用旧 retirement 来源数 12 而拒绝，更新为实际 14 后产出 fresh r2。较早 Unknown fixture 的私有符号链接问题也保留在对应目录。

下一批继续 run-owned 回调订阅撤回、未完成的具体效果结束/转移、N3 音符/轨道/相机/后处理语义属性及实际 Speed/Seek 事务；真正游戏首次准入和同候选整局验收按用户恢复实测安排。完整底层目标保持未完成。
