# 游戏启动 admission 证据包

r2 最小正规数版本完成限定离线验证与23项自有包安装；最新游戏启动被native stage145/row14先行阻断，managed Loader未进入，SDK启动未接纳。总目标和CompleteSeek保持未完成。

本目录可整体携带，ZIP包含1172项当前冻结输入+辅助Build、r1两项source差异、当前six DLL/PDB及Examples、自有23项包字节、实际测试/原wait/安装/有限启动记录、精确冻结控制器与overlay。没有游戏二进制、用户配置、账户日志、备份、全量工具包或bin/obj树。

```powershell
python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip
```

可加`--expected-sha256`与独立保存的`archive-receipt.json`对照。checker仅读ZIP，不解压、不构建、不安装、不执行DLL/EXE或历史控制器，不访问Root、D缓存、游戏或网络。完整历史重跑需要匹配原Windows工具链/输入，不是该checker的功能。

入口：`summary.json`、`case/report.md`、`case/scope.md`、`case/timeline.md`、`case/evidence.md`、`case/findings-path.md`。hash-index覆盖ZIP中除自身外全部成员；receipt在ZIP外固定ZIP、index和checker SHA。`origin`仅记录历史出处。`tool-observations.json`是父任务报告的tool chunk出处清单，明确不伪造外部工具对象。

r1包含884个普通用例；r2只包含469个普通用例(Runtime383/Module31/GameCamera55)及8项专项，专项已包含在Runtime中。r1其余套件未用于认证v2。准备期manifest保持原样，后续实际完成情况见result和原wait。新native后续修正不在该冻结包内。
