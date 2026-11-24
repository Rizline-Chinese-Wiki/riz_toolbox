# 同次目标组校验的祖先去重

本批针对实际游戏主线程接管127秒的调用栈，消除同一次目标组校验里反复进入同一 Stock/Lower 祖先的路径。九个 Runtime 文件改动已构建；691项相关检查和重新链接后4个宿主场景通过。新预览包已安装25文件匹配，未启动游戏；尚不能声称实机卡顿改善或 Clock/Speed/Seek 通过。

范围为当前已授权的本地游戏兼容底层修复，普通工程报告，flavor=null。Adapter、Loader源码、SDK合同及原生源码未变；不涉及账号、外部服务或旧版本兼容。

| ID | 原件 | 事实与边界 |
|---|---|---|
| E01 | [上一包实际调用栈](../work/game-seek-20261115/composition-read-r1-capturing-a-stack.log)及[游戏结果](../work/game-seek-20261117/composition-read/composition-r1-game-result.json) | 同次目标验证通过 Lower/Engine/Time/pair 多次回到 StockTargets.Inspect，且 traversal 入口/出口重复；仅定位旧包，不验新代码。 |
| E02 | [当前源码冻结](../work/game-seek-20261117/target-family/source-freeze.json) | 1506项输入；相对上一最终冻结只改9个Runtime/Hooks文件。主README保护哈希保持。 |
| E03 | [新构建](../work/game-seek-20261117/target-family/managed-build-result.json) | Loader及依赖、Examples、Hooks三组Release和Hooks210检查均自然0。 |
| E04 | [当前播放链结果](../work/game-seek-20261117/target-family/checks-runner/checks-r1/result.json) | 当前DLL/相同helper上四进程338项；真实owned23-member group、3pair、原调用/退出、Replay/Resume及直接写入拒绝。 |
| E05 | [定向证据结果](../work/game-seek-20261117/target-family/supporting-checks/run-r1/result.json) | 五进程143项，正常独立验证和pitch-cache/time-cache/engine-getter/lower-time-registration破坏拒绝，恢复后同组仍失效。 |
| E06 | [重新链接的宿主场景](../work/game-seek-20261117/target-family/host-checks-r1.json) | 新Loader/Runtime精确字节上4/4受控实际宿主方法场景。避免用上一Loader结果验当前链接产物。 |
| E07 | [新包回执](../artifacts/delivery-20261117/target-family-preview-receipt.json) | 38成员/25运行文件完整读回，绑定E02–E06及未变helper的继承证据。 |
| E08 | [安装结果](../work/game-seek-20261117/target-family/install-observation.json) | 14:52:39安装25文件匹配；仅安装，未请求Steam或游戏启动。 |

F01（E01–E02）：原 `PlaybackSourceGroupTargets.VerifyCurrent` 分别调用 Lower 当前身份、Engine supporting、Time supporting和 Stock；pair完整验证中的成员、跳板及映像组合又回到同一祖先。Stock Inspect是IL2CPP声明元数据查询，没有执行游戏GetTime、音源getter或原调用，适合在这一同步调用内分担共同责任。

F02（E02–E05）：`TargetFamilyVerification` 只在实际 Targets.VerifyCurrent 的 using 中创建。入口和 finally出口各完整验证 Lower supporting（自带Stock），无Lower时验证Stock。中间各使用点核对同targets/lower/stock对象、线程、未关闭状态及Engine/Time的具体父对象，只复用共同祖先。Engine/Time自身函数、RF/UW、metadata、ScheduledPitch、wrapper/cache仍执行；pair完整映像、成员分支、relay、三种proof与跳板检查继续执行。scope显式穿过这些纯证据读取，不与既有Group traversal并用，不跨独立Read、原生getter、回调或original保存成功，不发行source或writer票。单独null路径保留完整检查。

F03（E04–E06）：新DLL上的播放链、拒绝、局部证据和宿主场景均通过。E05的owned fixture中Stock为null，所以它只验证实际本地证据和独立路径保留，不能证明真实Stock Inspect正向去重或性能改善。E04同样不执行Unity、stock foundation、正式Adoption或Clock；4个宿主场景是受控managed依赖，不代表实际游戏状态。

P01（E02–E05支持F02）：`Targets.VerifyCurrent → TargetFamily入口完整Stock/Lower → Lower完整pair/image/member/proof + Engine/Time本地证据 + targets元数据/标记 → finally完整Stock/Lower → scope关闭`。本次调用返回后所有独立验证重新检查，不保留成功缓存。

独立Sol只读复审确认9文件范围、唯一创建点、父链和完整退出责任，未发现必要修点。构建及执行命令、当前DLL/helper/夹具哈希、各进程退出和结果均在E03–E06；本轮没有重跑退休矩阵或原生构建。

新Runtime：1718784字节，SHA-256 `eab5f3d3b5e4a6ad1a8c37a7dc4d57a177299a50239db579a05640a36291933a`。新Loader：55296字节，SHA-256 `1596fa331703bd984ee876a824cc9eecc78b4f08f74a95d5021a2af93b1f345d`。

新包：[RizToolbox-0.3.0-target-family-preview-20261117.zip](../artifacts/delivery-20261117/RizToolbox-0.3.0-target-family-preview-20261117.zip)，37326854字节，SHA-256 `2eb7f95e45e89c3ec0af5b1f6103a786dca786acebe7c7d5bc836e54265eb1ba`。仍含[宿主控制与包变更提示](Host-control.md)。原生helper未变，原442项仅按相同字节继承，不计入本轮691项。

当前电脑操作接口不可用，因此本批只完成新源码、构建、定向检查、打包与安装。下一应验证本新包的实际Pause/Resume、真正Clock Ready和Speed/Seek；旧游戏日志不能验收新包。exact内部child仍缺独立实际成功观察，B01–B19整体与生产准入未完成。
