# 范围与授权

`auth`：用户授权实现 PC MOD 兼容底层，要求多代理协作，并明确“先继续代码，实测稍后”。本批继续源码、构建、离线 fixture、磁盘静态核验和文档工作。`network_profile`：本地文件与离线 Windows x64 测试；没有操作游戏 UI、安装新包、启停游戏或访问游戏网络。

`in_scope`：组合既有 bundle/registry/四池预留/参数 setter/初始化桥为一次性生产启动协调器；实现受控 clone 激活、一次 Awake/六 globals 核验、真实首次场景转场及异步完成回执；接入实际游戏速度/偏移读取与 setter 基线核验；实现精确缓存结果的宿主 UI sink；补充有意义的失败/归属/回调结束回归，冻结输入、暂存包与静态证据。

固定目标为 Windows x64、Unity 2022.3.62f2，GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。静态依据为 [Awake/结果](../local-launch-20261022/README.md)、[初始化顺序](../local-initialization-order-20261023/README.md)及[首次转场](../local-launch-scene-transition-20261023/README.md)。本批场景证据复核仅重新读取 PE 的 7 个有限范围与指令；不将 evidence.json 列出的全部辅助文件都声称为本脚本重新验证。

`out_of_scope`（本批尚未完成，仍属于总任务）：菜单相机/粒子/输入锁与动画回调交接，实际活动局消费者结束及完整 globals/controller/bundle/协调器退休，完整返回/重试和新局建图，Loader/模块组合根与用户启动准入，完整游戏实测，以及 B01–B19 其余全部验收矩阵。协调器本身在生产类中已调用真实组件，但 Loader 尚无其构造/调用点；`LocalCatalogService.Selection.RequestPlay` 继续明确 OperationUnsupported。结果 sink 接受类型化命令回调，不代表命令已经实现。

完整构建为 1266/1266、25 suites。Runtime136/Hooks99/Audio420 是离线证据；native helper 31261/61358/1747 及 native-only 18 次调用各自单列，全部不替代游戏验收。没有对 1266 包另跑一个独立 native ABI runner，1216 的独立 runner 仅证明其历史包。新的激活、scene、settings、sink fixture 使用受控后端，commit pump 并非完整 Begin 的游戏执行。

383 声明源码输入与 18 暂存发布文件已归档；依赖、工具链和干净重现仍是另外的证据。当前安装仍 1021 四门包，旧 18 安装文件与 326 源归档及三份原游戏文件分别重新匹配。不新增 B 勾选，不缩小完整兼容底层的总目标。
