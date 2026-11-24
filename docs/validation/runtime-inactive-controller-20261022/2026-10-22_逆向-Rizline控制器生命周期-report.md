# Rizline 自有控制器未激活生命周期验证

2026-10-22；普通工程逆向记录，flavor=null。

## 结论与范围

固定 Windows x64 Unity 2022.3.62f2 游戏中，三参非泛型 Instantiate 可以把精确 NormalModeInLevelController prefab 克隆到宿主自建 inactive parent 下。本次 clone activeSelf=true、activeInHierarchy=false，已安装的 Awake 观察点没有记录调用，GameManager 的六回调和controllerSet保持原值。对象跨后续帧与真实IL2CPP GC存活并保持状态，再经显式Destroy和后帧Unity活性检查确认销毁，全部本探针root记录释放。来源prefab保持有效。

范围限用户授权本机游戏的宿主底层与自建探针对象；沿用[启动边界范围](../local-launch-20261022/scope.md)。GameAssembly SHA-256为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。没有修改原游戏三个文件，没有使用空Chart执行游玩。

## Evidence → Finding → Path

| Evidence | 位置 | 证明范围 |
|---|---|---|
| E-build | [953构建清单](../foundation-inactive-controller-20261022/build-summary.json)、[源码](../foundation-inactive-controller-20261022/source-manifest.json) | 实际构建输入、25套检查及原生fixture |
| E-install | [安装核对](../foundation-inactive-controller-20261022/installation-check.json) | 18项实际安装文件匹配 |
| E-native | [完整会话](20261022T103941290Z-8066fab3d0424801ac47156ba495cfd1/)、[runtime-proof.json](runtime-proof.json) | 775连续事件及三个生命周期阶段，文件哈希可复核 |
| E-failure | [数组失败会话](failed-array-contract/README.md) | 原准入误拒协变LevelCardControl[]，诊断包保留实际类型/维数 |
| E-stop | [退出](normal-exit-check.json)、[marker恢复](probe-marker-restoration.json) | 测试后进程停止和临时配置恢复 |

F-inactive（validated，E-native）：seq185/frame10508返回的controller、controller GO、parent GO与来源四身份不同，parentinactive、cloneinactiveInHierarchy且parentmatches。原生GC后同一图保持一致。只证明未激活阶段；没有实际激活一次的正向Awake时序或后续原版设置初始化证据。

F-globals（validated-limited，E-native）：clone前后及GC后，同一InLevelControl owner `2639647336800` 的controllerSet=false、六字段均null，快照逐元素相同。不能由null基线推出非空delegate Target调用、controller发布、恢复或消费者结束安全。

F-retirement（validated-limited，E-native）：frame10587请求Destroy时仍持有7root，frame10706确认controller、其GO、parent和两Transform均Unity-dead，再释放根。该证明不覆盖native heap/GPU或长期压力，也不覆盖激活后的DontDestroyOnLoad迁移与回调消费者。

F-array（confirmed，E-failure/E-native）：声明Object[]的发现函数实际返回来源卡片的协变数组。修复限制为Object[]或当前来源精确card[]/rank1，总扫描≤4096且各非空元素逐项核class/活性/场景/字段。14条resolver回归与新实机均通过；Workshop非空正向仍缺。

P-probe（callflow，E-build/E-install/E-native/E-stop）：验证固定构建→完整扫描发现快照→保留来源→全局baseline→绑定Awake并开窗口→新建inactive parent→三参Instantiate→精确身份/状态读取→后帧GC与复核→Destroy→后帧确认→释放自有与观测责任→菜单退出→冻结日志/hash→恢复marker。

## 复现与后续

构建/安装命令和包清单见[构建README](../foundation-inactive-controller-20261022/README.md)。需在游戏停止时安装并启用 `BepInEx/RizToolbox/probes/local-inactive-controller.enabled`，正常进入选歌页，探针按一秒最小间隔等待来源；最大180次。成功或失败均按日志记录，不尝试激活或盲回滚。退出后使用[freeze-proof.py](freeze-proof.py)核对完整会话，恢复marker。

下一步须组合完整owned本地资源图、启动前pending身份和结果隔离、精确参数注入/原版设置初始化，再验证Awake一次发布、全局条件恢复与native消费者结束。完整结果、重试与返回依赖这些证明；当前RequestPlay继续明确拒绝。原19项任务不由本探针代替验收。
