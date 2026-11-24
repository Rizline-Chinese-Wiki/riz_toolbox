# Progress.set_MusicPlayer writer 交付

已知字段 setter 纳入同 epoch 私有 stock group，每次 actual store 前无条件推进 source fence 并撤旧 Clock/source/prepared/writer authority。保留原14完整body/file/DIR64/foundation守卫，新增setter后15body。仅在 [scope](scope.md) 的自有 SEC_IMAGE 执行，flavor=null；完整 source generation、未知 lower writer、任意 Seek、生产首次准入和完整 B 仍未完成。

源码791文件，相对standard785改20文件（14修改、6新增，产品src改10），见[清单](changed-files.json)与[补丁](progress-player-writer.patch)。独立git apply --check/apply均actual0，791文件逐字节一致。相对r3仅Build-PlaybackHandoff.ps1增加2TU及entry/end导出，其余790文件相同，见[对应表](r3-source-correspondence.json)；不声称两套全源相同，补丁不含sln。

| 模式 | checks | actual exit | 证据 |
|---|---:|---:|---|
| normal | 433 | 0 | 原 r3 终态 |
| player-same-runtime | 46 | 0 | 原 r3 终态 |
| player-same-native | 47 | 0 | 原 r3 终态 |
| player-aba | 58 | 0 | 新续跑终态 |
| player-null | 47 | 0 | 新续跑终态 |
| player-throw | 47 | 0 | 新续跑终态 |

总6进程678检查。normal经正式Module/Service/两插件SDK完成真实native pitch与唯一Clock联合speed transaction。五个player模式在Before实际store前验证Clock.TryGetTime、旧独立source/writer、真实两participant的Verify/Commit/Restore拒旧票。ABA有2次Before/2original/2Returned、source1→3；same/native/runtime/null/异常同样立即撤票，旧地址恢复不复活旧票，不造SourceGeneration，不自行清旧publication后重发。每attempt原调用一次；异常保留SEH事实，由native finally/physical journal验证实际退出。

真实静态profile为0x401700的13B `488951384883c138e95328e6ff`，无coveringRF，tail0x263f60语义名/ABI未证。role9独立leaf分支要求完整file13B、逐byte OS lookup无image/dynamic RF、Dobby实搬8B并续回target+8；自有R11 thunk空unwind在指令边界0/4/8/18实际OS核对。旧八source仍用原RF/xdata/chain；IsProgress仅旧ProgressPlay分支。Source wire严格V2/160/136，writer子协议V1/224；9source+GetTime=10physical、pitch alias为一个入口。

实际执行对应5托管PE/PDB、480源文档；native两组各3PE/PDB和8COFF，导入/导出视图在native-proof.json/imageSurveys。第一组是实际固定执行输入；第二组是补好的仓库脚本fresh build0。后者fixture SHA123ebd8b98339d35d3622ab31fa42a024229ff40994bd058713142c90a747000，与固定准入68de93fbfec24e15bdb6cd02969896d431e21ab5d424b5653954ff5279dd9931不同：仅证recipe/TU/leaf/DIR64/符号，不获固定caller profile准入，未正式provider消费；独审实际normal probe已拒绝。未增哈希白名单或caller自报pin。

| Evidence | source_ref / artifact_path | SHA256 | repro_command |
|---|---|---|---|
| E-profile | D:\riz-build-cache-next\playback-progress-player-writer-stage-r1-20261101\inputs\static-profile.json | ec1b9ea8582f8a8f69ef8c7185e36363422bc562e042fe57933147c342bd4062 | 离线读取固定profile，无游戏方法调用 |
| E-run | D:\riz-build-cache-next\playback-progress-player-writer-continuation-r1-20261101\execution.json | 689e7af54679692cd8c21f38d38074953e27fcb10e46026f3fe8037845664bd2 | 各execution*/command.json精确dotnet命令 |
| E-symbols | D:\riz-build-cache-next\playback-progress-player-writer-continuation-r1-20261101\symbols\test-result.json | cdce0efd5b0d08f5c9269b05bb50132096f8174fac53dbabcfdaa897fc1734c4 | continuation/symbols-command/command.json |
| E-native | native-proof.json | 0360d9f3dcb3b0508f62154615d332a6837ce56e2966368015116f56cf534728 | `C:/Python314/python.exe -B prove-native.py`，fresh目录；只调用自有version export |
| E-patch | patch-verification.json | 89b213a64b8dc30d3512475aae213b47297ee81fd0ffac267457911a41df0cc3 | patch-check/patch-apply-command/command.json |
| E-build | native-build/build-result.json | d95e16746112cb9283e54de20c82f99adbaec293ae2d0f05a58bc828e34541a0 | native-build-command/command.json |
| E-source | r3-source-correspondence.json | 1153e7d55369e73bd3916fc4bb52ab4ab3ae1c8793e228dc5199b534805fe59c | manifest与source-snapshot.zip逐文件核验 |

F-writer：severity=n/a_re；category=other；status=validated；confidence=high；location=ProgressControl.set_MusicPlayer/field0x38；evidence_ids=[E-profile,E-run,E-symbols,E-native,E-patch,E-build,E-source]。impact=此已知setter每次attempt在store前结束旧来源消费权，保留原参数/调用/异常责任。repro_steps=按E-run固定输入运行六模式后按E-symbols/E-native核符号来源。remediation=n/a。

P-writer：path_type=callflow；start=自有native或NativeRuntime裸setter；goal=旧Clock/source/prepared/writer在store前失效。1.私有十成员及leaf签证（E-native，F-writer）；2.Enter无条件source+1、Router Before撤旧publication（E-run，F-writer）；3.实际两participant与旧票拒绝（E-run，F-writer）；4.原store/tail一次及独立Returned/Abnormal/finally（E-run，F-writer）；5.冻结来源与独立patch应用（E-symbols/E-source/E-patch，F-writer）。residual_risks=完整generation/未知writer/真实游戏/production/任意Seek/完整B未完成。

Timeline：r1 native/managed0后normal旧组数量断言失败；r2 normal/same-runtime0后为加强实际pre-store prepared检查主动结束same-native，原exit4294967295保留；r3三已完成子进程0，但parent与其余三项无终态，不记actual0；本continuation仅补ABA/null/throw，actual0后symbols0；delivery仅补仓库recipe并actualbuild0、独立apply0及791源核验。此前失败/中断和原日志均保留。
