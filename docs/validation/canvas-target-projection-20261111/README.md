# Canvas 目标纯投影：2026-11-11

**本批完成数值投影和新冻结图回归；指定纯算法源码复核已完成且无定位缺陷。实际target canvas/line/ring环境捕获与写入仍未完成，不开放完整Seek。**

`NativeSeekCanvasTargetPlan.Capture` 完整有界复制显式CanvasMove/keypoint数据，在受控interval/ease回调前后检查managed guard。按actual数组槽保留重复/乱序引用，不用Move.index排序。strict time<target反向选点，fallback首点；floor按Single区间乘value后加预计算FP；X先Single差，再Double1e-6阈值，raw progress经ease后Clamp，按原始mul/add/width顺序。local position先floor*speed*capturedDown，再逐component加X*capturedRight。

MaxCanvases/MaxKeypoints/MaxTotalKeypoints为65536/65536/1048576，恰好最大与超额均有数值测试。数据readonly/records immutable；同线程guard/interval/ease回调重入在nested任何回调前拒绝，异常退出复原gate。空顶层moves零行且不调计算回调，per-move列表必须nonempty。

## 真实执行

- 六组件及判定构建W0/E0，判定 **252/252**，其中新增16组pure numeric案例。
- Runtime ABI **51/51**；完整AudioContent默认套件在已验证自有native夹具环境下 **1001/1001**，均自然退出0。
- **798**项输入前后不变，**24**份DLL/PDB副本与本次六图匹配。Runtime构建保留既有CS0649，AudioContent构建现有测试警告以原日志为准。

详见[build-test-result](build-test-result.json)、[5-judgements](5-judgements.log)、[6-renderer-abi](6-renderer-abi.log)、[7-audio-content](7-audio-content.log)。完整音频不是995+6拼接：新DLL默认套件实际执行1001/1001，原995/1001与六项单列补验仍保留在[广域回归](../foundation-conformance-20261111/README.md)。

## 来源与范围

标准.NET PEReader仅File.OpenRead读取固定DLL，核验Double epsilon的bytes为8dedb5a0f7c6b03e（1e-6）、One为Single1，[constants.log](constants.log)中明确没有mapping/执行。[原生计算语义](native-semantics.md)列出strict scan/range/floor/lerp/setter RVA。

这不是actual backend/RootOwner，类型无IDisposable；回调和值由调用方提供，未知转换或ease不替代为identity/theory。Static downVector实际值及身份、actual CanvasMove/keypoint/Ease源根、目标time/canvas/line/ring重建、Transform callback consumer及exact inverse仍须后续实现。纯投影不得签TargetEnvironmentRebuiltKnown。

本批未启动/安装游戏，未改代理/账号/全局PATH/旧ZIP，未提交推送。构建使用的native fixture是框架自有测试代码，不是游戏Unity运行。全B01–B19目标保留，性能优化后置、历史SDK兼容不补。

独立源码复核完整范围与限制见[source-review.json](source-review.json)，实际执行证据仍单列，不能把任一份扩大为实机或target environment验收。
