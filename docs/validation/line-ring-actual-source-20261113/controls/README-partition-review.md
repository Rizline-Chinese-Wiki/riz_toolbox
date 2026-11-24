# 独立分区恢复静态审查

审查时间：2026-11-13 03:40:14 UTC。范围只覆盖 `Program.cs` 的旧 case selector、`recover_partitioned.py` 的恢复与通过门槛，以及其 runner 变更证明。`scopeComplete=true`，本范围 `blockers=[]`。

| 亲审文件 | SHA-256 |
| --- | --- |
| `tests/RizToolbox.Judgements.Tests/Program.cs` | `006503ee79cbebcad913be258101cf41515ffd3540ce05571e738249c799c686` |
| `work/line-ring-source-validation-20261113/recover_partitioned.py` | `290f0b25380d7ada3baed04849886b1a99edb0ee9a05df855915be13df3a2ba6` |
| `work/line-ring-source-validation-20261113/verify_runner_change.py` | `7c41fe754bee46620597c33898f1cc1621371e2d264b19591fbf461623bc16f3` |
| `work/line-ring-source-validation-20261113/runner-only-change.diff` | `31f117cdc1ef0e91ee3f0d1ed45104eaff86395d985915bb5f6a98324a4c1ea7` |
| `work/line-ring-source-validation-20261113/runner-change-review.json` | `d4b2b93b4b5bcdabbbc3270c5ab7b9730e274c6384cc1b39e721b8e66299e5a2` |
| `work/line-ring-source-validation-20261113/original-program-before-partition.cs.txt` | `3de9fe476e21fa376c5cf523632188c7f858a3ce1925c4641057b6c06c5f24e9` |

原 871 项输入清单为 `D:/riz-build-cache-next/line-ring-source-20261113/root-r3/source-r1/inputs-before.json`，SHA-256 为 `19912511141dce411612029ef2b2f165382c6f793f7959e816fbc3f0b46f4c6e`；其中 `Program.cs` 项为 `3de9fe476e21fa376c5cf523632188c7f858a3ce1925c4641057b6c06c5f24e9`。我只读重算了保存的旧 Program 文件 SHA，并用 `git diff --no-index` 核对当前文件：差异仅在旧 case 列表/单例 selector、usage 文本及复用原筛选表达式；该文件的测试体未变。`verify_runner_change.py:14-24` 的逆还原逻辑也以原 SHA 作精确门槛。

静态代码门槛：`recover_partitioned.py:229-244` 要求 300 个唯一序号与名称、旧日志前 256 个 PASS 及第 257 个 RUN 对齐，并逐 ordinal 启动独立子进程，每组要求自然 exit 0、唯一同名 RUN/PASS 与 1/1。`recover_partitioned.py:245-265` 将 ABI 51/51、Audio 1001/1001、static 6/6、24 份依赖副本、六个生产 DLL/PDB 的 12 项字节比较及输入图不变纳入 `passed`；`acceptedThisBatch` 保持 false。旧 source16 与 basis16 保留为旧图/旧程序集证据，脚本声明未由新程序集重跑。

**产物哈希的时间边界：**原 `final-r1/result.json` 的 `completedSourceRun.originalProducts` 仅预记录 Sdk、Core、Runtime、Adapter 四项。Hosting、Loader 的 DLL/PDB 在恢复脚本 `:172` 从原 artifact 目录现场读取，然后在备份及重建前作为基线；它们不是旧运行期间预先记录的历史哈希。脚本比较的是重建后 12 项与恢复前现场基线同字节。该判断参考的 `run_validation.py` SHA-256 为 `61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4`，原 `final-r1/result.json` SHA-256 为 `5f7b15c11b93f55fac329bee752604f40ed77e4567a48c7f61582b41f1ce721b`；仅检查了其中相关代码行及字段，没有全面复审旧运行。

本审查只读；未构建、运行测试、执行恢复或检查在途及后续结果。它不代签 SDK 目标、完整 Seek 或发布验收。
