# 下一步：实际销毁完成与资源退休

1360已把Cutout/Fade的factory/named Start/Move观察接入生产result capture，初始两协程继续来自1343。这里的自然结束仍只针对已观察记录，不能用作全run消费者结束。以下均为待实现要求。

## 先建立active失效观察

Cutout的实际MoveNext@3C5900最终请求Destroy控制器GO；不要再次按“尚未激活clone”路径盲Destroy，也不要以请求已发出作为实际失效。新增专门active责任层，必须关联同launch/registry/session、真实activation/Awake/参数和六globals、actual Cutout自然结束及native窗口drain，随后跨帧核确切controller/GO与音频图Unity失效。

现有termination graph通常要求live controller/info/progress。活动控制器按已验证Cutout路径失效时，应进入专门的退休阶段取证，不把预期失效反复当作可重试初始化，也不能仅删掉live检查从而允许任意失效graph继续操作。失效前仍须保留五门保护和实际receiver归属；unknown/failure继续保责任，不回退stock。

## 结束未来来源并回收独立责任

汇总实际initial tracker、terminal tracker、result UI回调、Judge outer/inner/Hold、Progress/Timeline/Play和六globals/UI/Unity事件等来源。逐个撤销将来的入口或取得actual已结束证据，再redrain，最后先撤分类后再次drain才能free。菜单锁/相机和globals仅条件恢复自己的值，后继保留；不能用UnlockAll、场景名或registry.Close替代来源结束。

当前terminal累计16条记录不复用。对已自然结束、native完整退出的观察根，设计先撤身份→redrain→独立free→确认后复用的有界路径；未知free保留原记录且不重试，不能把旧ID继续当作新slot。复用context/role时覆盖worker与逻辑detach、原始地址复用和新factory发布组合；完整producer关闭之前依然要继续观察新记录，不能把一次ObservedEnded锁为整局永久结束。

完整失效/来源结束验证器从真实owners取得证据，不能接受controlled backend/调用方拼bool。销毁与根free按独立责任清理，失败仍继续可安全的其余清理；先释放结果/图及producer观察责任，最后bundle、reservation、bridge/handler/coordinator槽。工厂、globals和registry原有要求保留。

## 最后组合命令与验收

Return由实际已完成首次scene/Returning session取得菜单来源，所有旧初始化/终止回调退出后才取新normal信用。Retry创建新revision lease、bundle、Chart/JudgeData和run代次；旧wrapper不复用，旧game TextAsset不作LOCAL来源。把Loader的完整host/bridge/handler/sink/coordinator根构造接上后，再按失败边界开放RequestPlay。

验证至少包括完成/失败/提前返回、Cutout期间返回或重试、同步首Move中的子协程、原对象多帧才失效、后继globals/menu、worker/重入、未知Destroy/free、16槽回收与地址复用、长循环；离线和游戏证据分开。用户“先继续代码，实测稍后”继续有效，不能自动把本批offline视为恢复实测。
