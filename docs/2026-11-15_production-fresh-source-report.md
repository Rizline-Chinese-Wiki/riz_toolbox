# FreshSource opaque native 票与工厂的独立离线证据

本轮是独立FreshSource候选，不与clock-readiness的1174图混验。归档完整1450份源码/项目/资源/测试输入：1173份lowerTime基线、268份ROOT/native来源、6个新产品文件、3个harness文件。产品补丁只有8文件：2个既有native Context修改、6新增；原base与ROOT/native来源逐hash未变。ROOT托管、Build、README、旧ZIP与checkpoint未由本发布更改。

实现增加注册于实际GetTime/source owner及三watch的opaque FreshSource票，沿原NextBootstrapEpoch域签发、单次claim；诊断Info、watch.Ordinal或binding数字投影不能替代同一活票。Busy保留同一candidate，worker拒绝先于phase变更，已观察图失败sticky关闭；撤销票与source/GetTime排空、exact-owner三watch退休分别完成后才释放roots。

Adapter build W0/E0；新native helper顺序5个原Popen.wait步骤全0（cl /W4 /WX、ml64、link）；最终harness build W38/E0，warnings来自既有FakeNative字段。owned lifetime/release/old-helper三个原进程自然0，原始checks分别51/51/17、拒绝计数10/8/2；真实23 Prepare/23 Commit，Busy回调1/2/0、SourceEpoch1/1/0、最终roots均0、ClockReads均0。旧helper case确认协议缺失在roots/watch stage之前拒绝。

动态正例仅保留player/source/progress/clip四个owned receiver根。正式stock七根factory仅编译和独立源码审阅，未执行；未接入Router、Clock publisher或正式provider，未签production admission，未运行游戏。Unity backing双采样及未被hook/采样观察的A→B→A仍不获连续lifetime证明，`unobservedBackingAbaCovered=false`。后续publisher须持有/消费同一capture/native ticket并另建baseline/clock准入，不能把数字binding喂给旧fixture路径当权限。

| Evidence | 实际范围与事实 | Finding / Path |
|---|---|---|
| E-01 完整source、inputs/final-inputs、product.patch | 1450精确输入；原1441份来源重建视图；8产品diff；两独立审计保留并核最终review指纹 | F-01：独立源码/产品边界可追溯；design/validated/high；P-01 |
| E-02 build/native/checks原JSON与日志 | Adapter W0/E0；native5 wait0；harness W38/E0；owned51/51/17及原始计数未汇总替换 | F-02：四根owned协议限定验证；design/validated/high；P-01 |
| E-03 新/旧native与managed完整绑定字节 | 新helper、旧helper拒绝对照、wrapper/lower/dobby、实际加载4个managed DLL、harness直接输出/依赖及native构建产物均带hash/size | F-02：实际运行依赖可离线fixity核验；P-01 |
| E-04 原失败与原exec observations | native-r1 Windows.h/exit2；checks-r1 loader路径、r2 fence分类及r3项目路径失败原件保留；对应工具终态见下文 | F-03：历史失败未改写；other/validated/high；P-01 |

P-01（callflow）：实际host提交run → 正式七根factory登记（仅编译/review）→ native两门联合签同域opaque票 → 同票claim/持续验证 → 独立撤销/排空/退休。E-02/E-03仅对其中四根owned native协议实例执行正/负例；F-01至F-03不提升七根stock工厂执行、Router/Clock/provider、完整SDK或生产结论。

原执行工具观察（由Astra/root提供，归档`work/tool-provenance.json`，原控制字节并列保留）：build_r1.py: session78672/91b14e/exit1; native_r2.py: 原同步工具/3b08ef/exit0; checks_r4.py: session9444/a35bb1/exit0。其中build_r1整个工具exit1保留前序native失败，不能把Adapter child40904自然0改写成controller0；native_r2为原同步完成，没有虚构session。更早失败轮未补造无记录的tool identity。这些是已发生的原工具终态，未来独立checker结果未预填。

新ZIP：[production-fresh-source-evidence.zip](validation/production-fresh-source-20261115/production-fresh-source-evidence.zip)，SHA256 `441a60a5a663aa066dbf488ed7a48f5a8200a90738f7d72ca48df8e51ea260de`。发布时内部ZIP-only检查核成员名/完整hash清单、1450源集合、8diff、原失败、原wait0、原始case计数、实际绑定与所有false范围；root随后独立执行ZIP-only，实际exit0、chunk062b20；回执见`../work/production-fresh-source-next-20261115/root-archive-check.json`。ZIP内独立检查false保留创建时历史。checker只读取ZIP自身，历史C:/D:字段不跟随读取，不执行DLL/EXE，未复制GameAssembly/UnityPlayer游戏PE。

仓库根目录只读复核：

```powershell
python -B work/production-fresh-source-next-20261115/publish_evidence.py --check-only docs/validation/production-fresh-source-20261115/production-fresh-source-evidence.zip
```

ZIP中的`work/publish_evidence.py`可复制到任意目录后以`--check-only`读取ZIP，不依赖live来源。本文采用docs-generator最小Evidence→Finding→Path结构，flavor=null。
