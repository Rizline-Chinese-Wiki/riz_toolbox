# 本批范围

2026-10-23。完整底层B01–B19继续；用户“先继续代码，实测稍后”保持。没有安装、启动、停止或操作游戏。

本批实现菜单输入/相机归属账本及精确native backend，并接到生产launch coordinator：actual inactive setter后、activation前捕获当前GameManager、两组件/GO及normal/back锁，独立root7项，建立四个物理入口观察。记录本局显式取得的一份normal/back信用和相机/粒子activeSelf改动；完成首次场景后只释放自己的normal信用，commit前再次核对连续归属。提供必须接收同实际launch/registry/session返回owner的恢复方法。

新内部标量probe在每个调用线程及被抑制重入中成对运行，只记录托管指针值/计数，不读Unity/IL2CPP；immutable snapshot确保逻辑解绑后仍配对exit。typed main capture读取实际前后状态，用独立early-retained入口先于公开Before，保留原既有retained观察者的Before→retain顺序。所有上下文在菜单原生工作前固定预建32项；process owner在菜单root/native工作前登记，配额32。

外来SetActive即使重写相同值，也撤销本局恢复权；外来Unlock/UnlockAll消耗本局最后信用后，后续Lock属于新调用者。余额/指针替换、worker触及所属对象、漏掉的相关重入、无配对观察、未知native调用或free均保留责任；不写count、不盲目UnlockAll、不重试不确定native动作。不接触新菜单的后继对象，不复活已销毁对象。

**限界：**只追踪本局显式菜单操作，NormalMode getter自己取得的锁及其延迟Release仍按外来活动记录，尚未证明这些隐式信用和旧回调全部结束。nativeSetActive/锁原函数仍走既有managed输送，其SEH/C++跨CLR展开未证。probe/账本为受控engine检查，加上固定游戏10范围/282指令静态核对，不是本批四入口真实Dobby或游戏验收。相关自有native操作中的额外重入会保守进入unknown。

首次启动生产类内已接；Return的菜单方法有生产权限校验，但完整Return命令/活动资源退休/Loader组合仍缺。B01–B19不新增勾选；三个既有子代理额度中断，本批主代理实现和自查，没有独立子代理审计。
