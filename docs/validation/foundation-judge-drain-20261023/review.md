# 主代理自查

2026-10-23。主代理实现与复核；现有三个子代理此前均因额度报错，本轮没有新独立子代理审计。

- 核对 C++ `RizJudgeStatsV1` 六个字段与 C# Sequential 0x30 布局；GetStats返回0成功，配置返回值也遵守既有协议。NativeJudge helper的 Dispatch 原已在 original、ExitObserver 和退出清理期间保持 inFlight；源文件未改。本批只增加 C# 读取与 broker/Adapter 接线。
- 统计中的 Entered/Exited/InFlight 是独立原子读取，未将 Entered-Exited 当成一致瞬时快照；只用实际 InFlight，并对无效、溢出或故障保持unknown。未配置是未知。native及managed回调故障、broker context故障、读异常都不能在后续零计数时恢复known。
- ReadExactActivity只查询精确既有 address、ABI、MethodInfo、class和返回类；外层还必须已拥有native transport。未安装返回unknown，不创建另一个patch。native绑定共享新增同method/class要求，拒绝别名。
- 内层Judge/Hold的managed计数包围整个capture/original/after/release/finally；native外层读helper的真实计数。逻辑解绑只撤sink，物理原调用仍可从entry读取，不将解绑当作消费者结束。
- LOCAL的准入、终止读值、已关闭图的撤身份前后drain与绑定停止均包含新Judge活动；unknown或非零保留根。受控fixture显式声明/转发自己的Judge活动，生产接口默认unknown，未给缺接线的backend伪造成功。
- Runtime新增用例使用正式Dobby、生产broker/bridge/helper、真实C++original/nativecatch/CreateThread。阻塞worker/解绑后在途为1、退出为0；normal及原SEH/C++路径context释放时仍在途，异常kind/payload保持。该用例在137/137定向和完整1298/1298均通过；它不是游戏元数据验证。
- 两次过程失败均保留：Audio旧Legacy测试wrapper漏转发；Runtime新测试回执字段布局错误。修正后定向通过，再做完整构建；未放宽生产unknown或回滚判断。
- 390声明输入及18暂存文件按manifest冻结校验，1021旧安装/旧326归档和3原游戏文件单独核对；3新增/11修改/0删除对照1287冻结输入。只有B03/B18a勾选。

仍缺菜单状态/锁连续归属、所有未来回调来源结束、真实controller/音频图销毁、globals/bundle/coordinator释放及完整Retry/Return和Loader组合。新drain不能替代这些退休前提，也不证明managed inner Judge/Hold的native original异常跨CLR展开。未安装、未触碰游戏前台或进程。
