# Rizline LOCAL 启动边界静态报告

日期：2026-10-22。任务类型：普通逆向，flavor=null。完整技术报告与原始入口表见 [README](README.md)；授权范围见 [scope.md](scope.md)。本报告只覆盖父任务分派的有限静态设计，`runtimeAcceptance=false`，不代表 B15 或完整底层完成。

## Evidence

- E-launch：本目录 [boundaries.json](boundaries.json) 保留17个有界范围、原始字节、地址及固定输入哈希；[metadata-excerpts.txt](metadata-excerpts.txt) 保留dump原行号。
- E-result：继承 [metadata bridge boundaries](../local-metadata-bridge-20261022/boundaries.json)，包含真实结果读取、Start内联保存/成就和两条TextAsset加载路径。
- E-clone：[instantiation-thunk.json](instantiation-thunk.json)，只新增`5A91B0..5A92C0`的272字节读取；未解析关闭泛型MethodInfo。
- E-broker：[source snapshot](hook-broker-source-snapshot.json)，冻结五个dispatch方法和原源文件哈希，观察时间`2026-10-22T04:52:46.763093+00:00`。
- E-check：[static-claims.json](static-claims.json)，43个精确指令锚点和五个original调用体检查通过。仓库根目录运行`C:\Python314\python.exe docs/validation/local-launch-20261022/verify-static-claims.py`可复现有限校验；不加载样本。

## Finding

F-commit，evidence_ids=[E-launch,E-clone]，confidence=high，status=confirmed-static：Awake发布六个manager回调与controllerSet，并注册持久对象；stock克隆后才设置参数。实现必须在克隆/激活前建立pending LOCAL会话和保护门，不能把克隆当无副作用准备。

F-isolation，evidence_ids=[E-result,E-broker]，confidence=high，status=confirmed-static/source-baseline：结果Start内联原版保存与Steam路径，现broker不支持前置跳过original。仅After观察或拦ProcessLevelMetadata不足以隔离；LOCAL完整播放尚不能放行。

F-route，evidence_ids=[E-launch,E-result]，confidence=high，status=confirmed-static：两个stock重试重新走原版/Workshop资产加载，Back由metadata.From决定场景。LOCAL应使用宿主pinned revision与每次新Chart的统一launch管线，返回目标来自进入时的本地快照。

## Path

P-local，path_type=implementation-proposal：完整资源/metadata/Chart准备 → 安装宿主前置门 → 注册pending session → 克隆提交 → 精确参数注入 → 局内 → 结果Start跳原方法 → 原ControllerOver结果只取得一次 → 自有结果面板 → 宿主重试或返回 → 场景与Unity销毁完成后释放旧run资源。各步骤的具体候选对象、精确托管签名、RVA及失败责任见README。

动态验证尚缺：clone/Awake精确时序、前置skip、LOCAL实际播放、真实结果面板与按钮、返回/重试场景和锁平衡、最终资源清理、原版存档/Steam零调用。静态证据只支持实施这些步骤，不支持声称已在游戏中成功。
