# 793资源所有者采样包

793/793、25套回归、0编译警告/错误，18项安装hash匹配、270源码输入未变。AudioContent82，其余沿用本次完整构建的检查；原生fixture31261/61358项，inFlight=0。

新增opt-in原版InsideLevelParams只读所有者采样，借用现有ControllerOver观察器已经持有的root；一次/重入/线程/异常与日志大小边界见 `../local-resource-owner-20261021/README.md`。采样不拥有root，不创建wrapper或调用Release，也不开放LOCAL播放。代码存在与离线测试不等于实际音乐引用或虚方法派发已确认，实机结果另存runtime-resource-owner-20261021。

实机1335条完整日志已取得两种AssetRefReference<AudioClip/Sprite>对象的官方resolver Release映射，RVA B115D0，interface slot1→实现MethodInfo slot5；原版失败结果与真实开局关联正常。专用ResourceReference<AudioClip>仍unknown。游戏正常退出、marker已恢复；原游戏三个文件before/after相同。见[实机边界](../runtime-resource-owner-20261021/README.md)。
