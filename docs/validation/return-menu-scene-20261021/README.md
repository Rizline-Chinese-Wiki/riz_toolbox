# 返回主菜单场景

实际会话 `20261021T094900103Z-5d3bca7a402a4aec8cefcda43ebf42a7` 中，MainMenu→LevelList 会正确撤销 LOCAL 窗口和 generation2 选择；返回时原游戏的实际场景名为 `FakeMainMenu`。旧后端只允许 `MainMenu`，因此窗口不能重建，菜单输入上下文与导航也未覆盖此返回页。停止时的原始935条连续记录保存在 `../runtime-canvas-catalog-20261021/escape-stopped-partial-20261021T095412Z/`，它是游戏仍在运行时的部分快照。

修复统一识别已观察的 MainMenu/FakeMainMenu；仍必须经过原有 GameManager.mainMenuControl、已加载活跃组件、父Canvas、渲染模式、相机及Raycaster核验，没有按场景名直接创建假目标。GetMounts 返回实际场景名；对象/场景身份变化仍创建新挂载代次并撤销旧窗口、控件、焦点和输入队列。

新增行为回归使用真实 UiInputService/PluginHost/InputUpdateEdgeCollector，并让测试后端使用生产的场景输入分类：返回页丢弃旧场景排队输入及模态焦点，旧控件失效；仅后续新鲜Menu输入能派发；未知场景拒绝Menu输入但仍保留Global动作。`red.log`为77/78（该项期望1实际0），修复后`green.log`为78/78。

这证明托管代次和输入分类，不证明真实返回页父Canvas解析及重新显示。原生重挂载与偏好恢复需新包实测。用户实体Escape已停止Computer Use，本阶段不继续发送GUI输入；游戏未退出时不安装或恢复运行中的临时资产。

后续用户明确允许恢复实测。788包已安装，session `20261021T100328262Z-0637fccb72724092a5d1f3bfe13a0319` 又暴露探针的迟到场景通知问题：先创建sceneGeneration0窗口，随后公共GameSceneChanged清空句柄却没Dispose；同一实际场景内UiInputService不会再次清理该窗口，因此稳定ID一直Conflict。完整325条/0Error失败记录在 `../runtime-return-menu-20261021/initial-scene-window-conflict-red-20261021T100535Z/`。0Error不等于探针功能通过。

专项直接编译真实LocalCatalogRuntimeProbe/ValidationProvider源码，接正式PluginHost/EventHub/UiInputService，模拟“原生场景先识别、公共通知迟到”。`late-notice-red.log`为78/79（应释放0窗口，实际仍1）；显式Dispose后再丢弃窗口句柄，`late-notice-green.log`为79/79，下一帧同ID可重新创建且无WINDOW REFUSED。TMP探针采用相同显式释放修复；其独立示例构建0警告/错误。完整修正版另存foundation-return-menu-final-20261021，不把源码修复补证788会话。
