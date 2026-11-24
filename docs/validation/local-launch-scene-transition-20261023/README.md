# LOCAL 首次转场：静态依据与实现边界

2026-10-23。固定 PC 样本、离线有界读取，`runtimeAcceptance=false`。没有启动游戏、修改游戏文件或执行任何 native 方法。已有 [Awake/结果范围](../local-launch-20261022/README.md)及 [初始化范围](../local-initialization-order-20261023/README.md)继续保持各自证据边界。

**完成 bundle、setter 和受控 Awake 后，仍须真实切换到关卡场景。** 激活 `NormalModeInLevelController` 不会自行创建关卡场景的 `InLevel.LevelControl.Start`。本批实现单次真实转场及完成凭据；相机、粒子、TouchBlock/BackLock 与完整返回/重试仍待接线，不能据此开放用户启动入口。

## 可核对的依据

[evidence.json](evidence.json)冻结输入、七个 native 范围的字节与 SHA-256；[disassembly.txt](disassembly.txt)供逐指令核对；[metadata-excerpts.txt](metadata-excerpts.txt)保留精确字段、方法声明与原行号。`ScriptMethod` 折叠符号及生成签名不是实际 native ABI 验收。

| 位置 | 静态事实 | 对实现的约束 |
|---|---|---|
| 菜单 `3C73B3` → `3C749C` | 参数 setter 的虚调用先于 `SceneManager.StartNextScene@402170` | 已持有完整 LOCAL 图时不用重新调用 stock 资源 LoadLevel 协程 |
| `3C7481..3C749C` | 实例来自 `GameManager.Instance.sceneManager(+20)`，目标来自 slot `1E79DF0`，时间常量 `17C4C30=3.0f`，等待 predicate 与 trailing MethodInfo 为 null | 精确实例 MethodInfo 调用 `StartNextScene(string,float,Func<bool>)`，参数采用 `Level/3/null` 候选；实际字段与异步发布均须读回 |
| Workshop `3D7A17`、前序结果 `3D8606` | 使用相同目标 slot、3 秒和 null predicate | 跨三条路径的一致静态候选，仍不代替字面量解码或实机验收 |
| `4021AC..4021EA` | `_async(+58)` 非空时直接返回；否则写目标、时间、predicate 后启动协程 | void 正常返回不证明转场发生 |
| `404180`、`4041D2..404205`、`404228..40424F` | 设置 lock；拒绝空目标或同当前目标；创建 LoadSceneAsync 并先禁止激活 | 进入前必须捕获 loaded origin、精确 idle manager；调用后必须保留真实新 AsyncOperation |
| `404451..40448A`、`4044BF`、`40412E..40413D` | 等待加载进度后允许激活；null predicate 无额外等待；最终恢复时清 async、清 lock | 完成凭据要求真实 operation.isDone、新 loaded Level handle、async=0、lock=false |
| `3C7411/3C7455`，Workshop `3D7990/3D79D4` | 停用 manager 的 mainCamera(+40) 与 mainParticle(+48) GO | 这是原版转场的额外所有权，本批没有盲目写入 |
| `3C755D/3C7659`、`3D7AD7/3D7BD3` | 安排 3 秒 SimpleDelay 和 active-scene predicate 的 SimpleWaitUntil | 存在后续回调消费者；不能把方法返回当作输入平衡或全部消费者结束 |
| `3B1B64`、`3C8000..3C8076` | delay action 尾转 `TouchBlockRelease@3827E0`；另一个 predicate 读取 active scene name 并与同一个目标 slot 比较 | LOCAL 必须自己负责 TouchBlock 与转场动画/回调，不能凭空发送一次 release |

## 字面量限制

构建场景表直接确认 index 6 为 `Assets/Rizline/Level/Level.unity`。目标 slot 的磁盘编码为 `A0001E01`，对应 string index 3840；现有恢复 metadata 的该条 length/offset 超出其 literal-data 范围，不能从该文件独立解出字符串内容。`evidence.json.sceneLiteral.text=null` 明确保留此限制。实现中的 `Level` 同时来自构建场景表与既有运行时场景观察，但本静态范围不冒称完成 literal 文本解码。

## 实现与尚缺工作

`NativeLocalRunSceneTransition` 在激活前独立 root 当前 manager/scene manager、捕获实际 origin，激活后执行一次精确转场；有界 root/参数槽保留所有异常责任，不重试未知 native 调用。完成读取不制造 RunStarted，不构成 controller、协程、globals 或资源消费者退休证明。

原场景的暂存 parent/transform 会随 origin 卸载失效。`ReadAfterSceneTransition` 仅在同 session/controller 的真实转场凭据完成后允许这两个暂存对象失效；三个持久 clone 对象、原 DDOL scene、Awake 与 globals 的精确证据仍不可缺。初次激活的五对象核验没有放宽。

仍缺相机/粒子状态的条件交接与恢复、输入锁平衡、返回目标/选择恢复、活动局消费者退休及完整重试。需要后续实机验证；用户已要求实测稍后。

在仓库根目录复现（依赖本机已有工具，不安装依赖）：

```powershell
& C:\Python314\python.exe docs/validation/local-launch-scene-transition-20261023/inspect-scene-transition.py
```
