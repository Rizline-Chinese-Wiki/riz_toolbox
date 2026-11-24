# 接下来怎样补齐引擎接入

2026-10-25。本轮已完成返回 ABI2、receiver/native owner 字段投影及 tracker/engine 独立代次身份账本；[最终完整构建](build-run.json)为25套1617/1617，[565源码与22包](README.md)冻结但未安装。当前是引擎基础通道和身份协议经过离线验证，生产调用链尚未闭合。以下是剩余实现顺序，不是已经完成的功能。

## 1. 证明最早安装时机

先实现[自有早期加载宿主合同](early-loader-design.md)：真实静态依赖Host→Importer→Proxy→helper/Dobby，覆盖Importer真实TLS、CRT、DllMain中的首次四目标调用，保留机器码写入、模块初始化和原调用的实际顺序。Recorder只用已初始化的定长存储和原子序号，子进程退出后再导出；不能在loader lock里等待工作线程或临时加载复杂依赖。

普通成功子进程必须证明四项全部Prepare/不可变Configure及实际Commit完成先于Importer最早目标调用，Execute完整EAX仍正确。每项Prepare/Configure、每次Commit前后分别注入故障；任何写尝试后失败都必须保留sticky Unknown并阻止Importer继续，不用假回滚或重试恢复Ready。迟到安装、重复初始化和已存在worker要有实际拒绝证据。固定Dobby的真实调用及导入闭包若不满足初始化限制，就停止采用该方案并调整加载架构。

该宿主只证明自有依赖的行为。随后才核验游戏真实依赖proxy、系统库转发、文件放置和模块唯一身份，建立适用于固定UnityPlayer的生产启动点；静态导入图和在il2cpp_init之前运行都不能单独证明四入口此前未执行。[安装研究](engine-installation-research.md)已经确认UnityMain2晚于UnityPlayer初始化。

## 2. 实现生产发布凭据及只读采用

用独立V4或等价强度记录核验固定UnityPlayer、GameAssembly、Dobby和四入口完整边界、原字节、ABI及所需字段布局。每行保存模块/hash/RVA/kind/slot/target/original/entry/epoch、发布前后字节、写序号与终态；不能把现有GameAssembly V3当作UnityPlayer入口授权。

生产CLR只核验并Attach已发布通道；旧helper缺ABI2、旧凭据、缺行、重复采用、模块/原字节/relay改变一律拒绝。覆盖逐写失败、并发读取Unknown、重复启动与所有行篡改，再以真实冷启动核对最早发布顺序。自有fixture与固定映像只读/隔离发布证据分别保存，不能拼成活体游戏证明。

## 3. 把已有身份账本接入实际coordinator

当前新增账本通过受控配对表和scalar provider连接实际节点账本。下一步在生产coordinator内借用已持有的receiver root，调用正式字段投影取得经核验的native owner，并在实际tracker发布Enumerator后、同步Start首执行前建立对应关系。managed Coroutine handle返回后只升级同receiver/owner/slot/tracker generation/Enumerator，engine node generation继续独立。

不能把managed receiver地址直接当native owner，也不能在native已释放node后回读。foreign嵌套、未知node、异常、worker、配对失败和溢出保持责任。验收要在同一条真实native→CLR→coordinator→三类tracker链中覆盖同步首执行、稍后handle、地址复用与旧代次晚到；再单独补实际IL2CPP对象、GC和场景切换证据。目前三类tracker的受控backend测试、合成metadata测试和native联合中的受控provider各有边界。

## 4. 收齐调度、内联清理及取消责任

[实际调度研究](scheduler-cleanup-research.md)已识别四入口之外的路径，需要分别实现可验证的精确观察及责任记录：

- 调度登记、pump消费前erase、重复登记再执行、普通删除cleanup；记录payload/node代次和物理执行窗口。
- manager销毁绕过普通删除函数的直接回收及cleanup；After不回读已回收记录。
- async及其他continuation/wait旁路的创建、交接、执行和最终清理；匹配结构形状不等于已经证明实际来源。
- Start引用计数低分支的内联完整清理；Start返回null、AL=0和Release返回0都不足以证明该责任完成。

各路径还需要自己的首次安全发布合同。完成这些观察后才能形成明确owner/run/slot/generation的取消完成凭据，将Stop请求、实际在途窗口退出、未来执行来源关闭及各消费者归还分别连接。provider、future、listener、根及媒体资源由各自责任完成后退出；缺任一来源继续Pending，不能从一次看似idle的快照授权统一释放。

## 5. 用完整运行链验收

在游戏实测恢复后核对冷启动、菜单、加载、暂停恢复、同谱重试、正常/失败结算、中断返回、再次开局与Loader重建。每次运行分别记录RunId、能力/对象代次、来源关闭、取消、节点/调度/消费者回收和晚到旧回调；验证窗口与偏好恢复。再完成完整分发、升级和干净环境矩阵。

在上述缺口补齐前，`EngineCancelled=false`、Pending保留、`RequestPlay`关闭。完整[B01–B19规格](../../../.docs/foundation-expansion/SPEC.md)持续有效，仅B03整项和B18a子项保持原有验收勾选。用户“先继续代码，实测稍后”的安排保持；本轮没有安装新包或操作游戏。
