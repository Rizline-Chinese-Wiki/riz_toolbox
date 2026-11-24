# MUG Pause／Replay／Resume 的原生对象隔离

2026-11-02 17:08。本批在独立目录 `D:/riz-build-cache-next/playback-player-control-isolation-r1-20261102/`，基于旧五 AudioSource leaf 隔离后的 L/first-sdk-r5 冻结880输入。主树尚未合入直接六入口和本批；生产Speed/Seek仍关闭，没有安装／启动游戏、调用游戏方法或操作UI／账号。

## 已证问题和实现

固定 L/n1 Source helper（SHA `9bad179177053b2e1fd8db4b5bb80ec277b7d6e21d82ba2e7cc8c9514450c523`）仍把另一个MUG播放器的Pause、Replay、Resume记入当前owner。`counterexample-r2` 的active-watch和early-foreign均实际exit1：969个真实original全部执行，同时错误地产生969个Before、969个entered／exited、Scheduling从1变为970，worker令owner flags=6。每场只有隔离断言失败；新的fixture没有把正常fence初值或observer作用域误当合同。

新helper在同一native锁内先按active／pending七根watch的精确player匹配，再产生journal、fence、thread fault和managed观察。正确class、共享AudioSource或正确／空／错误MI都不授予另一个player本owner的权限。无当前watch时不由这个增量发行前置捕获。相关worker在原函数前进入Unknown，抑制owner-only managed观察，保留真实物理责任。

被筛掉的Void0／Single1调用仍按原receiver、raw float bits及MI恰好转发一次。最初n1只加分类、漏加转发的实际失败也保留于 `native-checks-r1`；n2补上转发。fixture原先误设Scheduling初值、并在original中读取callback-only权限的草稿均保留，最终r2按实际初始值和callback作用域核对。原函数SEH只签真实Abnormal＋PhysicalExit，本层不伪造管理层sticky Unknown。

SourceV4／168B frame／136B info、PlayerWatchV2／120B和19个物理成员保持。它不补Progress／Start接收者、未知lower writer、完整source generation或生产首次准入。

## 实际验证

| 范围 | 实际结果 | 本批目录内原件 |
|---|---|---|
| 固定旧helper反例 | active及early两进程exit1，各仅隔离断言失败，969个原函数执行 | `counterexample-r2/test-result.json` |
| 新native编译 | `/W4 /WX /EHa /fp:strict /guard:cf`，actual0 | `n2/native-build-result.json` |
| 三入口foreign、early、current、pending、worker、class、original SEH、observer SEH | 20进程6735，全actual0 | `native-checks-r2/test-result.json` |
| 新helper下六direct及constructor/watch/first回归 | 94进程15556，全actual0 | `direct-watch-regression-r2/test-result.json` |
| 新helper下旧五AudioSource隔离 | 27进程7767，全actual0 | `legacy-regression-r1/test-result.json` |
| 新helper下旧八role实际Dobby | 14进程36675，全actual0，仅历史无watch profile范围 | `original-eight-r1/test-result.json` |
| 当前托管fresh build | 0 warning／0 error | `managed-checks-r1/start-build.result.json` |
| 实际19成员组foreign原函数及其Pause／SetTime／SetPitch／Schedule children、NativeRuntime、first hold | 3进程78，全actual0，未运行SDK | `managed-checks-r1/test-result.json` |

Source helper SHA `4afd7545419f5b35d7d2bbae2983d008d795261599fe44893f5d95e7ed59ce71`，Single helper SHA `46a5a74212a8b995f95c4478ac9ac9c0bed47489765b3bbb8de27758191852dd`。fixed owned fixture仍是625837，不重编它取得旧CALL授权。

正式SDK正常Pause／Replay／Resume连续性和foreign调用后权限保留还须实际终态；旧 `formal-r1` 的两个进程因桌面端重启缺席且没有退出结果，不计成功。新L `actual-terminal-continuation-r11` 包含本批两个模式，使用本批n2四inputs与当前实际构建DLL，L/n1的成功不签本批n2。

## 长测进程的保存

实际查询发现Codex父进程的Windows Job限制为 `0x2800`，含KillOnJobClose；普通隐藏子进程也会随父端关闭。单次breakaway尝试仍处于外层job，r10拒绝启动任何测试，其失败现场保留；带startup flags的WMI启动返回21也保留。随后用Windows管理服务启动无控制台的 `pythonw.exe`；r11 driver与八个初始dotnet均实际 `inJob=false`，实际PID／creation FILETIME／command落盘。没有修改Codex job或系统启动项，没有安装服务。

独立driver只对已确认缺席的旧句柄续缺项，持有每个新子进程的Popen句柄并记录真实exit／stdout／stderr／末行JSON；不会把旧日志、超时或PID复用当终态。r11当下运行不代表验收通过。

完整generation、Progress／Start receiver隔离、未知inline／icall／Playable、任意Seek、effect及B01–B19继续独立列缺。
