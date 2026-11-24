# 2026-11-13 开发预览打包交接

所有脚本已经准备；**本目录的准备工作没有运行构建、测试、打包、验包或游戏**。旧 2026-11-07 ZIP 保持原字节。旧 879 输入的 300/300 回归属于旧图；下面的 881 最终候选做 focused 验收，并不声称在 881 字节上重新执行完整 300 项。

## 同一次 881 六组件编译采集

`work/line-ring-endpoint-integration-20261113/integrate_validate.py` 的 `six-release` 调用已经接入 `D:/riz-build-cache-next/delivery-20261113-r1/Capture-FinalSixInputs.targets`。实际 C 工作区源码直接编译到 `D:/riz-build-cache-next/line-ring-endpoint-integration-20261113/main-r1/artifacts`，Csc 参数、输入哈希写到同 scope 的 `csc-inputs`。随后 solution 构建及 focused 测试必须证明 `productsAfterSix == productsBefore == productsAfter`，以及 capture 文件始终同 SHA。若字节不同，保留失败记录并以新 scope 重跑采集与 focused 验收，不把两次产物拼接。

最终六组件的 authored 数量以真实 Csc/PDB 文档和六个 `src/RizToolbox.*` 源目录逐项枚举；881 是 solution 输入数，不是六个 PDB 文档数。采集目标要求 .NET SDK 8.0.418、net6.0、便携 SHA256 PDB、实际 Csc argv、六项目与无额外 profile 注入。

## 881 focused 结果成功后依次运行

以下命令从 `C:/Users/71957/Desktop/riz_toolbox` 执行。每个 JSON 输出仅新建一次；出错保留现场，不能覆盖原结果。

```powershell
$python = 'C:/Python314/python.exe'
$graph = 'D:/riz-build-cache-next/line-ring-endpoint-integration-20261113/main-r1/metadata/graph/result.json'
$focused = 'D:/riz-build-cache-next/line-ring-endpoint-integration-20261113/main-r1/result.json'
$endpoint = 'C:/Users/71957/Desktop/riz_toolbox/work/line-ring-endpoint-integration-20261113/application-record.json'
$delivery = 'D:/riz-build-cache-next/delivery-20261113-r1'
$native = 'D:/riz-build-cache-next/delivery-native-20261107-r1/native-delivery-files.json'
& $python -B 'work/delivery-20261113-preparation/Create-FinalSixMetadata.py' --focused-result $focused
& $python -B 'work/delivery-20261113-preparation/Create-FinalSixReview.py' --graph $graph --endpoint-receipt $endpoint --focused-result $focused --output "$delivery/final-six-review.json"
& $python -B 'work/delivery-20261113-preparation/Review-NativeAbiReuse.py' --final-graph $graph --native-manifest $native --output "$delivery/native-abi-review.json"
$review = "$delivery/final-six-review.json"
$abi = "$delivery/native-abi-review.json"
$reviewSha = (Get-FileHash -LiteralPath $review -Algorithm SHA256).Hash.ToLowerInvariant()
$nativeSha = (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash.ToLowerInvariant()
$abiSha = (Get-FileHash -LiteralPath $abi -Algorithm SHA256).Hash.ToLowerInvariant()
& $python -B "$delivery/Package-Delivery.py" --six-review $review --six-review-sha $reviewSha --native-manifest $native --native-manifest-sha $nativeSha --native-abi-review $abi --native-abi-review-sha $abiSha --execute
& $python -B "$delivery/Verify-Delivery.py" --execute
```

新 ZIP 固定为 `artifacts/delivery-20261113/RizToolbox-0.3.0-sdk-runtime-20261113.zip`。打包器逐项锁定最终 Csc 源、冻结副本、六个 DLL/PDB、SDK ref、五项 native 字节及 ABI 复用证明；保留 SDK reference-only、23 个安装 runtime 路径、模板和五示例离线编译、许可证和 Speed/Seek/生产关闭声明。验包器从**新 ZIP**独立解包，重复 QUICKSTART 两组 restore/build，核对每个归档成员、runtime SHA、SDK ref、target profile 源字节，不读取或启动游戏。

若 native ABI 脚本发现旧 helper 的导出、签名、布局、版本、大小或目标 profile 有改变，它会中止复用；此时先重建受影响的 native 阶段并产生新 manifest 与新审查，再改打包输入。脚本无法把变化的 ABI 标为兼容。
