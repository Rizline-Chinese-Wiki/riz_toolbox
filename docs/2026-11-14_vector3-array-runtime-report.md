# 原生 Vector3 数组接口：正式离线接入

Runtime 已正式接入 IL2CPP UnityEngine.Vector3[] 创建与数据发布接口。新 901 输入主图通过 67 项专项，随后在同一构建产物上通过 Runtime 默认回归 356/356。两组验证有重叠，分别记录，不将数量相加。

本批没有启动或安装游戏，没有覆盖既有预览 ZIP。LineInformation.SetPositions、原共享 scratch 的读写、实际 renderer、Ring 初始化、Hold 引擎、DSP schedule 和完整 Speed/Seek 仍未完成；完整 Speed/Seek 保持关闭。

## 实际能力

`CreateVector3ArrayWithPublication` 使用 Runtime 自有的 12-byte XYZ 类型，验证 canonical CoreModule Vector3、准确 Single 字段布局、array rank、element class 和 stride。数组 header 从真实 empty-array 导出结果推导，数量、byte length 和 object size 使用 checked 运算交叉核对。

调用先将输入复制为自有 raw bytes，保留负零等有限 Single 位值，避免原生或 guard 回调修改调用者数组。输出以 pinned root 保留，完整复制后先向原 publisher 转交根，再释放临时 empty-array 根。publisher 抛出或清理结果未知时沿用原 NativePublicationCall 责任，不以重试或替换 owner 掩盖未知状态。

正式接入包含三个新增 C# 文件和 Runtime.Tests Program 替换，898→901。Program 采用隔离测试中实际编译的 scoped route；原候选中仅增加 registry 的 Program 副本不作为实际执行入口。Build.ps1 和动态 consumer 源码保持本批接入前字节。

## 验证结果

| 项目 | 结果 |
| --- | --- |
| 六生产组件 Release | 0 警告、0 错误 |
| 解决方案 | 0 错误 |
| 新 Vector3 接口 | 7/7，含非空不等 XYZ、负零、输入变更、metadata/storage 拒绝、worker/guard 撤销、publisher 和 cleanup 责任 |
| Single 数组相邻回归 | 3/3 |
| Runtime ABI | 51/51 |
| 实际动态 consumer | source25–30 六项通过 |
| 主专项合计 | 12 个自然退出步骤、67 项专项；旧 300 只列清单 |
| 默认 Runtime 补充回归 | 同一新 Runtime/测试 DLL 上 356/356，自然 exit0，stdout/stderr 和产物 hash 单独保存 |
| 归档 | 1002 个文件、34,305,195 bytes，包含 901 输入、原始日志、控制、产物、依赖、fixture 与补充回归 |

主控制进程31208由原 Windows handle observer24080保持到实际 exit0。默认 Runtime 补充回归的 root exec session82107也实际 exit0；该补充没有单独的 kernel handle witness，记录没有将两种观察混用。

本批未执行完整 source30、完整 Judgements 默认集合或完整 SDK 矩阵。此前 898 的 155 项独立接受，仅作为源码祖先；隔离数组 61 项也未代替本批正式 67 项。

## Evidence → Finding → Path

范围见 [scope.md](../work/line-vector3-array-case-20261114/scope.md)，flavor 为 null。以下证据可由归档内检查器在不读取原 C:/D: 源码或运行目录的情况下复核。

| Evidence | source_ref | SHA256 | 复核方式 |
| --- | --- | --- | --- |
| E-001 | [验收摘要](validation/line-vector3-array-runtime-20261114/validation-summary.json) | `3fe60eef9ecabc489c2ec4e575c56212f59f9ab397e9b7c47e73c1d1cd188cc9` | 下方 archive-only 命令 |
| E-002 | [主执行记录](validation/line-vector3-array-runtime-20261114/run/result.json) | `c46b01f9db32ba8bff4ee57dba8582c66e560b927610191a725281692f32bac1` | 同命令逐项核 argv、RUN/PASS、退出与产物 |
| E-003 | [原句柄记录](validation/line-vector3-array-runtime-20261114/run/controller-handle-exit.json) | `687af74e833331195e75b2241d57e52f6e91b179042b87e54d6ab80b855e4be2` | 同命令核原 PID、控制脚本、结果和实际 exit0 |
| E-004 | [默认回归记录](validation/line-vector3-array-runtime-20261114/supplement/result.json) | `ca8f325a30c369fa6344e9d2cd79b284dd9e0702cb921bb7ad455cdaeae7f6e0` | 同命令核 356 个实际 PASS 和同一测试/依赖字节 |
| E-005 | [实际接口源码](validation/line-vector3-array-runtime-20261114/compiler-inputs/src/RizToolbox.Runtime/NativeRuntime.Vector3Array.cs) | `b5d6c3734391cc09e2a8fa432611357b0acb52e91d57fcd9115c88cfe2440d4f` | 同命令校验源码并阅读实际 export/copy/publication 路径 |

F-001：status=validated，severity=n/a_re，confidence=high，evidence_ids=E-001/E-002/E-005。位置为 NativeRuntime.Vector3Array.cs：真实 typed 数组创建、准确数据复制和原 root publication 已进入正式 Runtime。

F-002：status=validated，severity=n/a_re，confidence=high，evidence_ids=E-002/E-004。位置为 Runtime.Tests scoped/default 路由与真实 export fixture：新增注册在专项和默认 Runtime 流程均通过；错误 metadata、撤销与未知清理路径保留准确责任。

F-003：status=candidate，severity=n/a_re，confidence=high，evidence_ids=E-001/E-005。位置为后继 renderer writer：创建新数组不证明原 scratch 共享关系、line 保存的数组引用/count、Unity consumer 和完整 Seek 已闭合。

P-001，path_type=callflow：原线程校验 → 自有 XYZ bytes → canonical Vector3/array 元数据核对 → empty-array header 观测 → pinned 输出与完整复制 → 原 publisher 接收 root → 临时根清理或原未知责任保留。该路径支持 F-001/F-002，保留 F-003。

## 时间线与复核

- 2026-11-13 23:33：首次隔离构建 W1/E0，被严格 gate 拒绝，未执行测试。原日志保留；随后补充实际 rooted-target 撤销检查，未屏蔽 warning。
- 23:37：新隔离构建 W0/E0，7 Vector3、3 Single、51 ABI 实际通过。
- 2026-11-14 00:15：在已接受的 898 图上接入四文件，原合入记录保存 freshMainPassed=false。
- 00:25：901 主图 12 步骤/67 项自然通过，原 handle exit0；随后默认 Runtime 356/356 自然通过。
- 归档发布和实际 archived CLI 的独立复核均 exit0。归档器在发布前修正了原目录绑定，保留旧工具；归档查询对原路径只做字符串归一化，不访问原 C:/D: 文件。

```powershell
python -B docs/validation/line-vector3-array-runtime-20261114/publisher/publish.py --check-only --archive docs/validation/line-vector3-array-runtime-20261114
```

下一步从 `work/line-renderer-next-20261114/HANDOFF.md` 接原 shared scratch 和精确 SetPositions(array,count) writer。最新续接以 [.docs/CURRENT-CHECKPOINT.md](../.docs/CURRENT-CHECKPOINT.md) 顶部为准。
