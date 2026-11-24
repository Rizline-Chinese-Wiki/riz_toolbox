# 当前启动候选的独立 SDK14 验证

complete1172 当前启动候选的 SDK14 已通过：正式托管宿主构建 W0/E0，两套由本轮模板独立生成、从本轮 SDK 引用包恢复并编译的插件通过原 14/14 检查。supervisor34952 对 controller15816 的原始 `Popen.wait` 于 2026-11-15 05:02:28 UTC 记录自然退出0；两个实际步骤也均自然退出0。launcher 的退出码没有代替原控制器退出码。

范围来自用户继续 SDK 验证的授权及[项目合同](../.docs/foundation-expansion/SPEC.md)。这是通用技术报告，flavor=null。1172 项完整冻结输入已包含 Distribution.Tests 的两项输入；它们的角色重叠不增加源码总数，去重后为1172，不能计作1174。辅助 Build、原 Pack/Test 脚本、SDK readme 和完整模板另行冻结。[源码审查](validation/sdk-startup-candidate-20261115/controls/source-review.md)和[差异](validation/sdk-startup-candidate-20261115/controls/source-diff.json)记录相对旧1171组合新增1项、变更6项；本轮未借用旧运行结果。

| Evidence | 观察与 SHA-256 | Finding / Path |
|---|---|---|
| E-01 [本轮结果](validation/sdk-startup-candidate-20261115/run/result.json)及[直接日志](validation/sdk-startup-candidate-20261115/run/sdk-distribution.log) | `4813c991852dd9cf54d75777cbe693c769df980b867bca14a0e643efc8380a95`；两个自然步骤0，14条不同PASS，完整冻结输入未变 | F-01：本轮包、模板、两个独立插件和正式宿主合同通过；n/a_re / design / validated / high；P-01 |
| E-02 [原始监督终态](validation/sdk-startup-candidate-20261115/supervision/controller-exit.json) | controller15816 / supervisor34952；actualControllerExitCode=0；原控制器与监督脚本哈希一并保留 | F-01 的原始终态依据；P-01 |
| E-03 [归档哈希索引](validation/sdk-startup-candidate-20261115/validation-summary.json) | `854f8c21176bc05d5b8f554b8e248af3e802d4af4a1e3942574f221e9818ad25`；1266文件；本轮SDK包 SHA `7932f5773d4813a18ed5b5c72a0774dc042cc199fa48ebf8b07f180d0a25cfa1` | 源码、模板、生成项目、实际插件DLL、正式宿主、日志及控制可在归档内核验；P-01 |
| E-04 [精确启动基线](validation/sdk-startup-candidate-20261115/controls/base-startup-controller-exit.json) | supervisor34372 原始等待 controller43772 自然0；其结果与本轮1172输入及基线哈希逐项绑定 | 输入来源可追溯；P-01 |

P-01（callflow）：精确启动基线1172冻结源（E-04）→本轮正式宿主构建→原Pack脚本新打SDK包→新的custom hive与cache生成AuthorOne/AuthorTwo→独立restore/build→正式加载器在不同load contexts执行14项（E-01/F-01）→原始等待终态（E-02）→归档内校验（E-03）。原14项及嵌入探针未削减；环境适配限离线feed、新输出目录、审计/构建编排、隔离cache及五个dotnet首次运行开关。后台控制及CLI显式使用 `CREATE_NO_WINDOW`。

四个net6.0.36框架包已按实际冻结feed核对SHA-256与大小，记录在[依赖固定凭据](validation/sdk-startup-candidate-20261115/dependency-feed/fixity.json)。包原字节未复制到本归档；归档可独立复核保存的证据与依赖pin一致性，不能独立完成离线重建。原D盘运行、旧归档和ZIP保持原字节。

项目根目录的只读复核命令不读取原D盘运行目录，也不启动构建或游戏：

```powershell
python docs/validation/sdk-startup-candidate-20261115/controls/publish_evidence.py --check-only docs/validation/sdk-startup-candidate-20261115
```

发布原session实际0（98e7f9→7c1692）；直接归档checker实际0（c3b626）。[限定验收凭据](../work/sdk-startup-candidate-20261115/acceptance-receipt.json)记录相同哈希。此处仅接受独立SDK14候选；未合主、未部署。完整SDK、B01–B19、engine/source/FullSeek以及真实游戏Speed/Seek仍按各自证据验收，`sdkGoalComplete=false`。
