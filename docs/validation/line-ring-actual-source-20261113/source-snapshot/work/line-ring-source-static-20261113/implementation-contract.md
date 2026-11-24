# Line/ring source profile 静态实施合同

本批增加独立 `ISeekLineRingSourceProfile` 与 `NativeSeekLineRingSourceProfile`，组合原 Canvas profile 和既有冻结字段/body 证据，提供 actual source reader 所需的确切 class 与 getter MethodInfo。该文件提供只读元数据/代码准入；未构建、未执行 backend 测试、未打开游戏二进制，不能当作 actual capture 或当前源码执行验收。范围见 [scope.md](scope.md)，上游 actual capture 合同见 [下一 line/ring basis](../line-ring-basis-handoff-20261112/implementation-contract.md)。

## 入口与边界

```csharp
// src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceProfile.cs
internal interface ISeekLineRingSourceProfile : ISeekChartNativeProfile
{
    IntPtr InformationClass { get; }
    IntPtr LineControlClass { get; }
    IntPtr LineClass { get; }
    IntPtr LinePointClass { get; }
    IntPtr GroupClass { get; }
    IntPtr TransformClass { get; }
    IntPtr GetTransformMethod { get; }
    IntPtr GetParentMethod { get; }
    IntPtr GetPositionMethod { get; }
    IntPtr GetLocalPositionMethod { get; }
    IntPtr GetLocalRotationMethod { get; }
    IntPtr GetLocalScaleMethod { get; }
    void VerifyCurrent(Action guard);
}
// Constructor: NativeSeekLineRingSourceProfile(NativeRuntime api,
//     ISeekCanvasNativeProfile canvasProfile)
```

接口继承原 Chart/Progress/零参 VerifyCurrent。无 Hold 类型依赖，不构造 Hold profile，也不要求存在 Hold/Note 实例。构造保存原 Canvas profile；Chart、Progress、Information、Canvas、Transform 都以 canonical class token 固定，在每次复验前后核同一 token。它不重建 Canvas profile、不另建 NoteView、不创建根、不获取实际对象/数组、不执行 range/Ease/init/producer/getter/setter。

Windows x64、已映射模块 handle/path 与原固定 SHA-256 是生产准入条件。VerifyCurrent 复验当前 class、全部 exact declared read fields、MethodInfo/entry/完整签名/flags、完整 body hash；owner thread 和重入失败在 native 验证前拒绝。真实 metadata/body 失败撤销本 profile 的后续成功；caller guard 自身失败不把同 profile 的独立 caller 一并撤销。

## 已冻结证据子集

| 范围 | 完整必需字段/方法子集 | 本批数量 |
|---|---|---:|
| LevelInformation | lineControl | 1 字段 |
| LineControl | levelInformation、dynamicLines、lineGroups、_canvasControl、_nowTime、_sceneWidth、_speed、_gameSceneScale | 8 字段 |
| LineInformation | lineIsStatic、canvasIndex、startLinePoint、endLinePoint、endTransform、endLineInformation、linesGroup | 7 字段 |
| LinePoint | time、xPosition、easeType、canvasIndex、floorPosition | 5 字段 |
| LinesGroupInformation | judgeRingTransform、ringActive、lines、nowLineIndex | 4 字段 |
| LineControl 完整 body | SetDynamicLines、SetRing、Start、Update | 4 body，5728 bytes |
| Component/Transform 完整 wrapper | get_transform、get_parent、get_position、get_localPosition、get_localRotation、get_localScale | 6 body，448 bytes |

25 个 field 的类型/staticness/offset 原样取自 `geometry-static-evidence.json.exactFields`，通过主控提供的 `InspectLineRingSourceField(cls, name, type, offset, guard)` fresh 检查；10 个 body 的 RVA/length/hash 与 10 个 method entry 原样取自同一 JSON。五个既有 getter wrapper 在原 renderer profile 中已有相同 pin；get_position 和四个 LineControl body 在冻结 Hold geometry profile 中已有相同 pin。本批源码只复制静态值，不构造上述 profile。完整字段与 body 表、指令数和外部边保存在 [line-ring-source-static-evidence.json](line-ring-source-static-evidence.json)。

四个 producer 的 body pin 只证明当前代码身份，不能替代数值/副作用合同。冻结 externalBodyEdges 中 GetEaseWithIndex、LineInformation.SetPositions、LinesGroupInformation.Appear/Disappear/set_color、SetLineColor、无 metadata helper 及 Unity resolved internal calls 仍开放。外部边原先在整个 geometry 集中被 pin 的状态与本 profile 的十个 pin 分开记录，避免把 `targetPinnedBody` 当成本 profile 的闭包证明。

## Fresh ABI 与 caller guard

六个 getter 均使用主控提供的 `NativeRuntime.InspectLineRingSourceGetter(cls, name, module, guard)` 元数据-only 准入：引用结果需 exact canonical class/type/byref/storage；Vector3/Quaternion 需当前完整组件声明、Single storage、valueSize、offset/flags 与缓存布局一致，不能靠先前 Describe 成功接受热缓存后的 ABI 漂移。25 个 field 使用 `InspectLineRingSourceField` 对固定白名单、1 byte Boolean / 4 byte Int32/Single 和引用 ABI 作 fresh 检查；构造及零参验证使用本 profile 的 owner-thread 检查作为 guard，不调用 Runtime 的 private VerifyAccess。这两个 helper 的实现和 runtime 测试由主控负责，本目录的 file-only 脚本不执行或证明它们。

真实 profile 重载 `VerifyCurrent(Action guard)`，每个 class/field/method API、模块检查、body 可执行/可读检查和复制前后检查 caller；两个 fresh helper 接收同一 guard，供其逐 native primitive 检查。默认接口实现只给测试/自定义 provider 的零参 VerifyCurrent 加前后 guard。旧 `InspectMetadataClass`、四个 producer 身份所用的 `InspectDeclaredMethod` 和组合的 Canvas.VerifyCurrent 内部仍没有逐导出 guard；本批只能保证这些旧 API 的调用边界，不能把它们内部 callback 后的导出序列声称为完全关闭。

## File-only 复现与结果解释

从项目根目录运行以下命令，只读取固定的当前 C#/Markdown、冻结 JSON 与十个 asm 文本，输出本目录两个 JSON。脚本不加载游戏模块，不打开 JSON 中记录的 DLL/dump/metadata 原路径，不调用旧 `verify-geometry-profile.py`，不构建或执行 C#。

```powershell
python work/line-ring-source-static-20261113/verify-line-ring-source-profile.py
```

核对内容是原冻结文件 hash、25 field/10 method entry/10 fullbody pin 的逐项相等、JSON 指令字节连续性/完整 span/hash、asm 文本和 JSON 指令一致、getter/producer 原 profile pin 一致、接口签名与元数据-only Runtime API allowlist。结果见 [line-ring-source-profile-verification.json](line-ring-source-profile-verification.json)。`passed=true` 只指该限定文件复用验证；构建、backend 测试、runtime ABI、actual capture、engine effects 和完整 Seek 的执行结果均不由本脚本声称。

## Evidence → Finding → Path

| Evidence | 观察与固定引用 | 复现 |
|---|---|---|
| E-001 | 冻结 `geometry-static-evidence.json`，SHA-256 `1e1a0a2af774014f81c87bd3a4467fb7dca6338819ae82767c9440853603116b`；25 field/10 method/10 body 子集 | 上述 file-only 命令 |
| E-002 | 原 `geometry-profile-verification.json`、当前冻结 Hold geometry/renderer C# 与十个 asm 文本；全部输入 hash 见 readSet | 上述 file-only 命令 |
| E-003 | 新 profile 源码及本批 file-only verifier，确切 hash 见验证 JSON | 上述 file-only 命令 |

F-001（category=other，status=candidate，confidence=high，evidence_ids=E-001/E-002/E-003）：独立 source profile 可准确组合既有必需字段和完整方法身份 pin；限定结论只涉及冻结文本复用，不升级 runtime 事实。

P-001（path_type=callflow）：原 Canvas/profile identity → 25 个 source descriptor → 六 getter fresh ABI metadata → 10 个完整 body hash → actual reader 独立采集。前四步由 E-001/E-002/E-003 支撑源码合同；最后一步由 backend/capture 分支实现和测试，当前证据不声明其执行成功。Residual risk 为旧 inspector/Canvas 内部的 guard 粒度、未观察实际对象与 ABI、以及明确开放的 producer/Unity effects；后续仍需保持零 writer 与 CompleteSeekPermitted=false。
