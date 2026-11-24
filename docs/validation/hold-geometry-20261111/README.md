# Hold 目标几何：2026-11-11

**状态：当前代码已接入并通过本批离线构建／回归，独立源码复核尚未完成；不签本批闭合，不开放完整 Seek。**

`RunValue.PrepareSeekHoldGeometry` 独立捕获实际 Hold、canvas、line、ring 及 Transform 父链，通过官方 SetParent／position／localPosition／localScale 接口执行显式几何步骤。reparent 之后重新读取实际局部坐标，再计算下一步。输出显式 `SeekHoldStateTargets`，本部件不写 Hold List、缓存或音符 flags，不重放 renderer 事件。

## 已执行的验证

| 项目 | 真实结果 | 证据 |
| --- | --- | --- |
| 六组件正常 ProjectReference 构建 | 退出 0，0 警告、0 错误 | [1-build-six.log](1-build-six.log) |
| 判定与几何 console 回归 | 196/196，新增 18 组 | [4-judgements.log](4-judgements.log) |
| 本批 Runtime ABI console 回归 | 51/51 | [5-renderer-abi.log](5-renderer-abi.log) |
| 判定测试构建 | 0 警告、0 错误 | [2-build-judgements.log](2-build-judgements.log) |
| Runtime 测试构建 | 1 条既有 CS0649，0 错误 | [3-build-runtime.log](3-build-runtime.log) |
| 输入与依赖归属 | 694 项输入前后相同，16 份测试 DLL/PDB 副本与本次六组件相同 | [build-test-result.json](build-test-result.json) |
| 独立源码复核 | 上游限流中断，补充复核仍待完成 | [source-review-status.json](source-review-status.json) |

测试覆盖非 identity 父级、实际 reparent 后读回、head/tail 边界、Single epsilon 舍入、尾 canvas 的 Single 加 0.5 再截断、零除／非有限／溢出、多实际实例及重复 occurrence、共享 Transform、完整 Single 数组原始位、坏 metadata、callback 撤销、最终 managed guard、重入、不可变 journal 和未知清理责任。

## 历史失败保持

- [red2](historical-red2.log) 是新测试写入后的真实编译拒绝；当时 access 未实现 ReadSnapshot／Dispatch。
- [green1](historical-green1-judgements.log) 为 195/196：最终 guard 测试依赖了被 Release 内联的小包装。修正注入选择器为 `InvokeCore`＋`DispatchParent`＋精确首个 end flag receiver，原“零分派”断言保持。
- [无选择器 Runtime 全套](historical-unfiltered-runtime.log) 为 **340/349，退出 1**。9 项要求 native initialization／bootstrap 夹具环境，而此次未提供；没有把它们略过后宣称全套通过。本批 51 项对应既有 `--renderer-abi-checks` 验证范围，不等于全部 Runtime 测试。

## 复现本批范围

使用 .NET SDK 8.0.418（符合仓库 8.0.100/latestFeature），从项目根执行到一个新的独立 artifacts 目录：

```powershell
$artifacts = "D:iz-build-cache-next\hold-geometry-reproduction-" + [guid]::NewGuid().ToString("N")
dotnet build src/RizToolbox.Loader/RizToolbox.Loader.csproj -c Release --artifacts-path $artifacts
dotnet build tests/RizToolbox.Judgements.Tests/RizToolbox.Judgements.Tests.csproj -c Release --artifacts-path $artifacts
dotnet build tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj -c Release --artifacts-path $artifacts
dotnet "$artifacts/bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll"
dotnet "$artifacts/bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll" --renderer-abi-checks
```

实际执行的绝对 argv、退出码、日志哈希及依赖哈希见 [build-test-result.json](build-test-result.json)；当前差异见 [changes.patch](changes.patch)。后续源码变化必须新建批次验证，不能沿用这些结果。

## 仍然不提供的保证

捕获的 basis 不等于目标时刻 canvas／line／ring 已重建。Transform callback／隐藏副作用保留责任，读取已写属性不代表它们已闭合；未知 free 不自动重试，无精确 inverse／原子 CAS 或外部 ABA 排除承诺。实际 Unity internal call、原生几何、renderer 延后 consumer、设备输入、Hold engine exit、Judge 初始化及完整 Seek 事务仍未验收。

未安装或启动游戏，未操作游戏 UI／账号，未改代理／模型配置，未修改 11 月 7 日冻结 ZIP，未提交推送。B01–B19 全项目目标继续，接手顺序见 [NEXT-SEEK-COMPOSITION](../../../.docs/foundation-expansion/NEXT-SEEK-COMPOSITION.md)。
