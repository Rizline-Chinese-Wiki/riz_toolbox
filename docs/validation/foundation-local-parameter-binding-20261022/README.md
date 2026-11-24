# Pending参数注入与真实结果单次捕获

2026-10-22至23，跨日批次。**1081/1081、25套完整离线检查通过，已暂存、未安装。** Runtime130、Hooks69、AudioContent271；较1038新增43组检查。当前游戏安装仍是1021，用户要求“先继续代码，实测稍后”持续有效，本批没有游戏输入、安装、退出或终止操作。

本批增加两个真实NativeRuntime步骤：把同一pending claim已收养的参数注入其exact inactive controller，以及为已提交的exact controller取一次真实ControllerOver结果。它们尚未连接完整激活/终止handler，**不开放RequestPlay、不宣称LOCAL已可玩**。

## 本批实现

- `NativeInactiveControllerParameterBinding`先校验平台/构建、实际pending claim与同bundle、已绑定controller，再独立保controller/params两根，通过精确MethodInfo调用SetInsideLevelParams@36B0E0。核五引用、标量位模式、父子inactive关系和LevelInformation仍null。不hook共享setter地址，不调用有UI/协程副作用的GetInsideLevelParams，不猜speed=1或offset单位。
- `NativeLocalResultCapture`在真实ControllerOver@3BEA60之前消耗registry唯一claim，返回对象在runtime临时清理前保根，核exact metadata并复制实际字段/getter值，再把结果root交registry。捕获失败保原保护，禁止重调/stock fallback。没有真实开局来源时，copy.RunId/ChartId保持null、AssociatedWithObservedStart=false，不能从捕获本身伪造生命周期关联。
- Runtime新增内部`InvokeVoidWithPublication`、`ReadWithPublication<T>`、`InvokeValueWithPublication<T>`。receiver、reference guards、boxed输出与owned参数在现有32-call/129-root/128-input固定记录中独立清理；value调用在native入口前预留boxed输出容量，校验declared/actual类型。旧普通API契约不因新接口而自动扩大。
- 结果backend读取/解码发生任何未确认异常后保持粘性责任，不能在claim前反复重试。成功保住独立controller root后，后续调用使用该根，不继续依赖调用方的borrowed handle。

## Evidence → Finding → Path

| Evidence | Finding | 后续路径 |
|---|---|---|
| [最终日志](build.log)、[摘要](build-summary.json) | 1081项/25套、native fixtures31,261/61,358通过；完整增量0编译诊断 | 新包尚无游戏验收，不能推断干净构建零警告 |
| [Runtime130定向](runtime-value-targeted.log)、[Audio271定向](audio-bounded-read-targeted.log) | 新runtime实际实现配合受控native导出；binder用真实registry+受控backend；capture加真实backend的preclaim读free失败fixture | fixture不证明目标游戏metadata/对象/patch/消费者结束 |
| [暂存](staging-check.json)、[源码](source-integrity-check.json)、[归档](source-archive-check.json) | 18项新暂存/337项新输入匹配，源码ZIP逐项核对 | 保留确切包边界，后续新源码不改写本批输入 |
| [旧安装](previous-installation-check.json)、[原游戏](original-game-check.json) | 18项实际安装仍匹配1021及其326输入归档；三个原游戏文件不变 | 本批安装与实测待用户安排 |
| [独立审查](independent-review.md) | 修复preclaim临时root责任遗漏与borrowed controller继续依赖 | 完整结束接管与native退休仍未完成 |
| [终止入口静态证据](../local-result-ownership-20261022/README.md)、[退休证据](../local-consumer-retirement-20261022/README.md) | LevelOver上游可接管；Progress终止尾部仍有Timeline；ClearDontDestroy不清注册列表、外置delay不会随旧controller自动结束 | 实现精确终止上下文/必要static ABI、never-active取消与active消费者结束ledger |

最终build.log SHA256 `f0a54dc026cfd8a22c9f6816eeeea36764f2307085af01c8801c8562bb0305ed`。337输入源码ZIP SHA256 `9a052716a100ebe3115fef77e9bb37c0e10215d9921b2b48ec247aaa7484eeec`。源码归档不包含完整依赖和工具链，不代表B19干净环境复现。

## 离线复核

```powershell
dotnet run --project tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj -c Release
dotnet run --project tests/RizToolbox.AudioContent.Tests/RizToolbox.AudioContent.Tests.csproj -c Release
./scripts/Build.ps1 -NativeOutputDirectory 'D:\RizToolbox-build-cache\local-parameter-binding-20261022'
python docs/validation/foundation-local-parameter-binding-20261022/summarize-build.py
python docs/validation/foundation-local-parameter-binding-20261022/summarize-build.py --staged
```

命令从仓库根运行。`--staged`分别验证本批当前暂存/输入，以及旧1021安装/归档，不会安装或停止游戏。工作树继续修改后，不得用它宣称本批源码仍是当前代码。

## 未完成的真实接线

首结果Start前exact receiver归属仍缺。新的静态证据提供另一条具体路线：在exact LOCAL LevelOver前接管并由宿主显示结果，同时覆盖相关Progress.Update终止上下文；现有四门没有这些新增入口。不能只skip整个Progress.Update，因为它还负责时间同步、音乐、fade和终止状态。

设置初始化、激活/Awake正向、六globals发布/条件恢复、原生终止/重试/返回、hook在途及外置协程排空、消费者结束验证和实际完整游玩仍缺。仅controller Unity-dead、scene名字变化或registry.Close不能证明结束。已激活局的消费者verifier尚无真实成功分支。

binder的setter异常一律保守认定责任未确认，即使某次IL2CPP exception的临时free实际已成功，也不靠后续no-op推断成功，因此可能长期保留两根和配额；边界明确限32owner，未来可用有类型的native/cleanup outcome再细分。此处不是“所有异常已可恢复”的声明。

中间`runtime-void-targeted.log`的三个失败来自把fixture“先移除handle再抛错”误计为活根，修正断言后通过；`build-before-bounded-read-fix.log`不包含后续读接口修复，不代替最终包。全部日志保留。定向编译仍有FakeNative CS0649警告，不增加任务勾选，无commit/push。
