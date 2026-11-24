# 既有原版资源所有者的只读采样

接线在已验证的 `NormalModeInLevelController.ControllerOver` 原有观察入口内：适配器已经从精确字段取得并root了InsideLevelParams，再同步借用该root调用 `CaptureExistingOwner`。不创建关卡/资源wrapper，不调用getter或Release，不改变结果或提交行为，不另装hook，不扫描所有对象。

仅在模块启动时存在 `BepInEx/RizToolbox/probes/local-resource-reference.enabled` 才启用，每个模块实例最多尝试一次。错误线程不读取owner且不消耗主线程机会；回调前先占用尝试标志，防重入或失败后重复访问。probe不保留或Dispose调用者root，借用范围随原有观察器完成。

报告先对最多64行、每行及header最多1800字符做完整预检，随后逐行记录 `LocalResourceReferenceProbe existing-owner` / `owner-row`。采集或日志异常均在probe边界被捕获；警告日志本身再失败也不向原关卡观察器传播。header/count与行index用于冻结日志完整性核验，日志限流造成缺行不能当作成功。

专项 `red.log`：78/82，新增4项因未实现而失败；`green.log`：82/82。验证默认关闭、一次/重入、调用者root仍有效且只由调用者释放、错误线程后仍可主线程采集、元数据/日志故障不抛出或重试、超限报告不会先写半份header/行。

这些是托管边界验证。实际closed音乐/封面实现类、官方virtual resolver返回的MethodInfo与entry仍须之后实机采样；已有Sprite metadata入口与方法名不能代替已有对象的接口派发证明。完整LOCAL原生音乐/封面消费仍未实现，不据本采样开放RequestPlay。
