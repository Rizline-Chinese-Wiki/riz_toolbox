# 当前底层组合的独立SDK插件验证

当前1171输入组合的SDK分发验证已通过：正式宿主构建W0/E0，两套由模板独立生成、从本轮引用包恢复并编译的插件在正式宿主中通过14/14。没有复用旧939组合的包或宿主测试结果。此结论只覆盖包、模板和托管宿主合同；完整SDK/B01–B19、父renderer、真实游戏Speed/Seek仍由各自范围另行验收，候选未合主。

授权范围为用户继续离线实现/验证SDK的任务及[项目合同](../.docs/foundation-expansion/SPEC.md)。通用技术报告，flavor=null；没有安全评估case、IOC或ATT&CK。完整1171冻结源已包含Distribution.Tests两个实际输入；runner另外列出它们核对，去重后仍是1171，不能计为1173。[输入口径记录](../work/sdk-current-integration-20261114/input-scope-reconciliation.json)保留这一说明，原归档及14项结果不变。原Pack/Test脚本、模板与readme另行冻结；[源码审查](../work/sdk-current-integration-20261114/source-review.md)。模板仅安装到新的custom hive，依赖只取本机原字节Microsoft.NET6缓存包，未改用户模板/永久环境、游戏、账号、Git或旧ZIP。

| Evidence | 观察 / 哈希 | Finding与Path |
|---|---|---|
| E-01 [新图及直接日志结果](validation/sdk-current-integration-20261114/run/result.json) | SHA e58b3fd60018dfa2c993eb8dbbb624bf54889f2ecc75a3e6a4d3747b921c9ba6；两个自然步骤退出0，14条不同PASS | F-01（n/a_re/design/validated/high）：两套独立插件在当前正式宿主验证启用/禁用、事件、能力恢复、结构化拒绝和故障隔离；P-01 |
| E-02 [原控制器监督](validation/sdk-current-integration-20261114/supervision/controller-exit.json) | 原supervisor15176等待controller35948实际0 | 原Popen.wait确认终态，启动工具0不当验证0 |
| E-03 [1263文件自包含归档](validation/sdk-current-integration-20261114/validation-summary.json) | SHA bb5cbd807f090b9dde1255c9386368279dbea9582cb4fccc6fb2109db28bb6f0；当前nupkg SHA 7932f5773d4813a18ed5b5c72a0774dc042cc199fa48ebf8b07f180d0a25cfa1 | 当前源码/包/模板/实际插件输出/宿主/依赖和日志可只读复核 |

P-01（callflow）：取受监督组合run-r2的精确1171冻结源（只消费字节，不借其尚未完成的验收）→冻结额外Distribution源和原helper→构建新正式宿主→本轮pack引用SDK→fresh cache与custom hive生成两个插件→独立restore/build→正式加载器与不同load contexts运行14项（E-01/F-01）→原Popen.wait终态（E-02）→归档内核验（E-03）。原14项和嵌入插件探针未削减；仅离线依赖feed、输出目录和构建编排有环境适配。首次启动前因父输出目录缺失失败，尚未创建controller；原失败chunk4a4041保留，随后补目录才真正启动a2fddf。

本批不提供生产native startup、engine/source、FullSeek或B01–B19整体验收。原939参考ZIP保持原字节，不覆盖为本批运行时包。

项目根目录下的只读复核命令，不访问原D盘运行目录：

```powershell
python docs/validation/sdk-current-integration-20261114/controls/publish_evidence.py --check-only docs/validation/sdk-current-integration-20261114
```

发布root72145实际0/ad358f，直接归档checker实际0/9ed1bd。本证据与组合95步及父producer72步分别保存，不能拼成完整SDK矩阵。
