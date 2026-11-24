# 同包独立 production ABI runner

主任务使用已暂存 Runtime/SDK、已编译 native fixture/helper 与冻结测试源，在独立目录 `D:\RizToolbox-build-cache\initialization-unwind-independent-20261023` 构建并执行 runner；没有生产项目引用，不重建或改写其 bin/obj。执行 exit0，[build.log](build.log)为 0 warning/error，[fixture-result](fixture-result.json)为 passed、realDobby、nativeWorker，`gameRuntimeVerified=false`。

原命令如下；输出目录已存在，脚本按设计拒绝覆盖。重新执行须换成新的目录名：

```powershell
Set-Location 'C:\Users\71957\Desktop\riz_toolbox'
& .\scripts\Test-InitializationAbiFixture.ps1 `
  -RuntimeAssembly 'C:\Users\71957\Desktop\riz_toolbox\artifacts\package\BepInEx\plugins\RizToolbox\RizToolbox.Runtime.dll' `
  -NativeDirectory 'D:\RizToolbox-build-cache\initialization-native-unwind-final-20261023\initialization-abi' `
  -NativeHelperPath 'D:\RizToolbox-build-cache\initialization-native-unwind-final-20261023\RizToolbox.JudgeContext.dll' `
  -DobbyPath 'C:\Users\71957\Desktop\riz_toolbox\.tools\bepinex-789\BepInEx\core\dobby.dll' `
  -OutputDirectory 'D:\RizToolbox-build-cache\initialization-unwind-independent-20261023' `
  -SkipNativeBuild
```

[fixture-manifest](fixture-manifest.json)记录 7 个实际执行输入：Runtime、SDK、测试源、native fixture、Dobby、typed helper 和 runner DLL；这些内容另快照到 [inputs](inputs/)，SHA 见 [evidence-integrity](evidence-integrity.json)。[package-match](package-match.json)把 Runtime/SDK/helper 三份连接到本批暂存 manifest，把测试源连接到 371 输入 manifest，并核对归档 native fixture/helper。文档复核已重新计算实际路径与快照的全部 7 项 SHA。

其中 Runtime SHA 为 `4674418feea8e0e3dccf91eb02d175fb38fc8e88eb34c9d144a1d86f3ce155a1`，helper SHA 为 `805be452ded4333d4ae4986d1428d1b6ee0ad3356ef5153142aa7eba40b1fccc`。完整 JSON 包含其余输入，不靠当前 artifacts 的文件名认定同包。

Run 执行真实 Dobby 和生产 binding/helper，保留 full-width pointer、引用/null、Play EDX poison、Tooltip 浮点位型、worker/递归和 callback-null-abort；新增 ordinary/LOCAL × Start/getter × SEH/C++ 8 条原异常路径，检查原 code/typed payload、After 语义、sticky fault、TLS 与完整 drain。它证明这两个自建 C++ originals 经生产 transport 的行为，不证明游戏 prologue/metadata、Play/Progress/Timeline original unwind、actual activation 或 LOCAL 游戏成功。七输入快照也不等于完整运行环境或独立 clean-room 复现包。
