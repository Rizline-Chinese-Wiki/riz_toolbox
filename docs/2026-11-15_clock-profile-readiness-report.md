# Clock profile 迟到就绪与 LowerTime 隔离候选验证

本轮完成独立候选的完整1174份托管/资源输入与1份Build.ps1辅助输入验证；相对原1172+Build仅5项产品/测试差异，新增Cache与ReadinessChecks两个文件。ROOT完整1160及Build仍冻结，已准备的1172主整合控制未修改；本1174候选尚未进入新的main链。native FreshSource另区工作不在本批范围内。

solution构建55条既有warnings、0 error。首次r1的9项focused检查自然exit0，后续Gameplay因遗漏4个原定命名native fixture环境输入得到155/184、原进程exit1；失败原件保留。r2仅补入这4项子进程fixture输入，复用同一构建字节，8个步骤全部自然exit0。Gameplay184/184、Runtime383/383、Module31/31，共598个普通用例；focused9已包含在Gameplay184内，不能另计为607。

Availability cache正向覆盖迟到transport、失败构造重试、晚attach、原profile保留、missing/替换owner失败关闭及错误线程拒绝。实际GameplayNativeBackend测试在provider就绪后重新探测，准确拒绝非stock fixture取得固定mapping语义；已有capture的ClockSourceIdentity在同次先Refresh并更新错误，旧probe快照不变、raw/DSP仍可用。它不证明正式stock mapping正向执行。

新combined Runtime/Sdk字节下，5项LowerTime owned机制case分别1336/910/892/883/887 checks，均23 Prepare/23 Commit，含实际采用original的OS unwind offsets 0/5/6/16/18。normal的18组wrapper/child与raw32输入、guard13拒绝、release-inflight及两种SEH路径的期望均满足；异常case预期的sticky fault仍保留。它们是owned机制证据，不能提升为stock、producer/router/Adapter/SDK全链通过。

| Evidence | 固定事实 | Finding / Path |
|---|---|---|
| E-01 归档source与原base重建视图 | 完整1175文件、45个solution项目、精确5diff；原base全部1173输入核未变；两独立review保留 | F-01：源范围与修复实现可追溯；design/validated/high；P-01 |
| E-02 r1 result及3份原始日志 | solution W55/E0、focused9原wait0、Gameplay155/184原wait1；原exec session39219/completion d66972实际exit1 | F-02：原遗漏fixture失败未改写；other/validated/high；P-01 |
| E-03 r2 result及8份原始日志 | 598普通用例与5项owned机制case，8子进程原Popen.wait0；原exec session51754/completion 0d9b7a实际exit0，root只读df9c65实际0 | F-03：选定离线回归通过；design/validated/high；P-01 |
| E-04 products/fixtures/lineage | 六组件DLL按r2运行前pin及结束fixity核同一字节，附PDB/测试直接依赖；4命名回归fixture、5 owned fixture、Astra原报告/summary/来源pin与原失败均归档 | F-03：产物与fixture边界可复核；P-01 |

P-01（callflow）：迟到hook provider → Cache.Refresh保留原owner、验证/attach → 同次Probe/RequireMapping刷新 → 固定profile仍拒绝不受准入fixture → focused9与三个普通套件 → 同combined Runtime/Sdk的owned LowerTime5 → 原执行终态与本归档。E-01至E-04支撑F-01至F-03；residual_risks：未执行stock GameAssembly/UnityPlayer profile、未合ROOT、未运行全27套或完整SDK、未操作游戏；ProductionAdmission保持false。

ZIP：[clock-profile-readiness-evidence.zip](validation/clock-profile-readiness-20261115/clock-profile-readiness-evidence.zip)，SHA256 `4d188b1a8362874cba9ec4f6fdd2867b56e43af092a3ca83e8a842f4d28dc382`。ZIP成员名、完整清单/hash、原base重建视图、9项实际PASS名、所有接受步骤自然wait0、六输出fixity及限定范围由发布脚本内部archive-only检查核验；发布当时root独立archive-only工具检查尚未执行，该历史状态保留在旧ZIP、summary及publication receipt中；本次root直接ZIP-only检查已实际exit0（completion `9fe75d`），见[独立检查sidecar](../work/clock-profile-readiness-next-20261115/root-archive-check.json)。历史原C:/D:路径仅作身份字段，checker不跟随读取，不执行DLL/EXE。

在仓库根目录运行只读复核：

```powershell
python -B work/clock-profile-readiness-next-20261115/publish_evidence.py --check-only docs/validation/clock-profile-readiness-20261115/clock-profile-readiness-evidence.zip
```

归档内`controls/publish_evidence.py`可复制到任意目录后用相同`--check-only`参数检查ZIP，不依赖原项目或D盘来源。报告使用docs-generator的最小Evidence→Finding→Path结构，flavor=null。
