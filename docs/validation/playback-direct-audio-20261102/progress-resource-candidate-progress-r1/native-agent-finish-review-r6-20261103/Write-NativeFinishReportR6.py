from pathlib import Path
import hashlib,json
from datetime import datetime,timezone

B=Path(r'D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102/native-agent-finish-review-r6-20261103')
U=B.parent
A=json.loads((B/'source-audit.json').read_text())
now=datetime.now(timezone.utc).isoformat()
def save(n,d):
 (B/n).write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def put(n,t):
 (B/n).write_text(t.strip()+'\n',encoding='utf-8')
def sha(r):
 return hashlib.sha256((B/r).read_bytes()).hexdigest()
put('scope.md',"""
# 范围与授权

- auth: 根代理本次明确授权的 U 域 native r6 收尾、保留失败与关机上下文、Hub/Bridge/Types bit256 只读审查。
- in_scope: 原 r6 helper 及冻结输入；原46模式证据复核；新增responsibility10模式逐项选证；仅补host-responsibility-closed；bit256的native产生与Runtime观察/完成边界。
- source_root: D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102/source。
- write_scope: 本证据包与一个全新host补测目录。此次未修改任何产品或测试源文件，未执行新构建。
- network_profile: offline。
- out_of_scope: 主目录合入；RootRuntime；latest Adapter deriveGraph/RetryNative顺序修正；managed firstlate/handofflate与mapped验收；Clock、SingleReturn opaque receipt/epoch、真实GCHandle释放的组合验收；生产授权。
- excluded_actions: 未访问game/UI/account，未启动游戏，未终止或重启既有进程，未派生子代理。
- contract: [IMPLEMENTATION-CONTRACT.md](artifacts/IMPLEMENTATION-CONTRACT.md)。
- freeze_scope: Runtime四文件以artifacts/source中的只读快照为审查对象。本域结论不替代其他域签署。
""")
put('timeline.md',"""
# 时间线

| 本地时间 Asia/Shanghai | 来源 | 事实 |
| --- | --- | --- |
| 2026-11-03 00:21–00:22 | r6原始构建及测试文件 | helper构建actual0；原46完整通过；新增10原跑9通过、host计数失败 |
| 2026-11-03 00:23:29–00:23:30 | fixture-r2 build.exit / manifest | 构建已完整actual0、passed true；不是未完成构建 |
| 2026-11-03 00:24 | 根代理任务上下文 | 低电关机打断更大任务交接/汇总；不把已完成native构建改标为中断 |
| 2026-11-03 07:33 | 根代理任务上下文 | 重启；旧进程已失效，不复用旧未完成managed包 |
| 2026-11-03 07:41:31 | host新补测manifest | fixture-r2显式原r6helper；actual0、217checks、0failures |
| source-audit observedUtc | 本次复核 | 原46/marker已过9不重跑；冻结失败、补测与源身份 |
""")
put('fixture-scope-proof.md',"""
# fixture-r2 与按模式选证

helper的7个输入在live、r6 frozen、fixture-r2 frozen完全相同；functional-matrix-r1可用冻结副本逐项相同，见source-audit.json。全部选择模式实际测试的helper均为ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c。

fixture-only-correction.patch已由脚本按精确白名单核验：只改closed分支ordinary frame预期host2/nonhost4、noJournal=ordinaryFrames+1，补originals counters断言。其他模式分支未变。runner-only-correction.patch只改host closed的noJournal5→3，nonhost仍5。

| 模式范围 | 选择证据 | 说明 |
| --- | --- | --- |
| 原46 | native-agent-retry-short-r6 | 原fixture+原helper，46/46、7969checks，未重跑 |
| host-responsibility-closed | native-agent-responsibility-host-r6-fixture-r2-resume-20261103 | 新fixture-r2+原helper，217checks、actual0 |
| responsibility-closed | native-agent-responsibility-short-r6 | 原nonhost已过；ordinary4/noJournal5与修正后的nonhost预期相同 |
| host/nonhost worker、seh、class、saturation共8项 | native-agent-responsibility-short-r6 | 分支与helper未变，各自actual0 |

这是逐模式组合的56项证据，不声称10个marker都在fixture-r2重跑。新originals counters断言仅在此次host补测执行，nonhost仍引用原实际通过证据。原host失败actual1、216checks/2failures完整保留，旧manifest未改写。
""")
checks=[
 ("BR-01","bit只在真实journal提交产生","PlaybackSourceContext.cpp:296-347","resourcePhysical要求ResourceEnabled、FreshBlocked和Start/SetClip/ProgressClip；requiresReceipt要求分类前armed。空row选中后，在同一owner锁内按knownArmed/noFault提交bit、JournalIndex和state1，再开放callback。耗尽则Unknown无bit。"),
 ("BR-02","真实TLS/current/CallbackActive/owner/token/fullmemcmp","NativePlaybackStockSourceHub.cs:173-198; PlaybackSourceContext.cpp:603-611","Before经ReadCurrentActual；native current限CallbackActive及kind/slot，SameCall固定owner/epoch/seq/token/receiver/caller/thread/depth；native verify再核TLS、CallbackActive、FindOwner、当前线程及全frame memcmp。"),
 ("BR-03","普通closedgate无journal不发Before","NativePlaybackStockSourceHub.cs:244-256; NativePlaybackStockSourceBridge.cs:143-169","例外同时要求资源profile、Dormant、非Armed、bit及三个role，再验证真正Before。普通Dormant无bit则忽略。"),
 ("BR-04","Returned固定已登记origin","NativePlaybackStockSourceBridge.cs:148-179; NativePlaybackStockSourceTypes.cs:149-164","先需_observations已有issued Before；回读actual并与私有origin完整SameCall匹配；physical只查owner+seq+token的state2row，不从后来FreshBlocked反推资格。"),
 ("BR-05","Unknown/Withdrawn不建立新权限","NativePlaybackStockSourceHub.cs:176-198,258-278; NativePlaybackStockSourceTypes.cs:166-175","新Before需要Known且Armed，Unknown非physical拒绝。正常Withdraw只允许已有返回/物理完成责任继续；VerifyAuthority再要求Known/Armed；Unknown physical投影不提供新source权限。"),
 ("BR-06","bit不授Clock/Fresh/Source","PlaybackSourceContext.cpp:640-646,773-806,847-853; NativePlaybackStockSourceTypes.cs:59-75","全部bit产品引用仅enum、native产生、Hub资格和Bridge origin检查；owner flags无bit，wire168不变。Healthy/EnterFresh/逻辑动作仍独立核owner/fault/FreshBlocked/gate/fences/initializer/resource；Types不公开可设bit属性。")
]
save('bridge-review.json',{"kind":"bounded-read-only-bit256-review","passed":True,"reviewedUtc":now,
 "method":"Exact symbol search plus manual dataflow verification; owned native dynamic cases do not claim managed execution.",
 "checks":[{"id":i,"title":t,"location":l,"status":"validated","explanation":x} for i,t,l,x in checks],
 "notCovered":["latest Adapter deriveGraph-before-RetryNative order","RootRuntime","managed Clock composition","mapped acceptance","production admission"],"productChangesByReviewer":[]})
specs=[
 ("E-001","合同与本域范围","artifacts/IMPLEMENTATION-CONTRACT.md","Get-Content -LiteralPath './scope.md'","离线U域；未改主目录、RootRuntime、产品/测试源。"),
 ("E-002","r6构建完整实际0","artifacts/native-agent-build-r6/native-build-result.json","Get-Content -LiteralPath './artifacts/native-agent-build-r6/build.exit.txt'","actual0；helperSHA ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c。"),
 ("E-003","原46完整且未重跑","artifacts/native-agent-retry-short-r6/native-retry-result.json","Get-Content -LiteralPath './source-audit.json'","46/46，7969checks、0failures；stdout/stderrSHA及exit逐项一致。"),
 ("E-004","原marker9过及host失败","artifacts/native-agent-responsibility-short-r6/native-retry-result.json","Get-Content -LiteralPath './artifacts/native-agent-responsibility-short-r6/host-responsibility-closed.stdout.log'","host actual1、216checks/2failures；实际责任3/3/3、noJournal3；旧测试按nonhost误计。"),
 ("E-005","fixture-r2关机前已构建0","artifacts/native-agent-build-r6-fixture-r2/native-build-result.json","Get-Content -LiteralPath './fixture-only-correction.patch'","build actual0；10source/20file哈希实读一致，产品输入未变。"),
 ("E-006","新host显式原helper实际0","artifacts/native-agent-responsibility-host-r6-fixture-r2-resume-20261103/native-retry-result.json","& './artifacts/native-agent-build-r6-fixture-r2/ProgressResourceNativeFixture.exe' './artifacts/native-agent-build-r6/RizToolbox.PlaybackSourcePrototype.dll' host-responsibility-closed","217checks/0failures；Before/Returned/Receipt3/3/3、NoJournal3、TLS12。"),
 ("E-007","源身份及逐模式选择","source-audit.json","& 'C:/Python314/python.exe' './Freeze-NativeFinishR6.py' --verify","7helper输入一致；marker选证10项16837checks；总56项24806checks。"),
 ("E-008","冻结桥接边界审查","bridge-review.json","Get-Content -LiteralPath './bridge-review.json'","bit窄资格、真实current、issued origin、Unknown/Withdrawn边界，无Clock/Fresh/Source直接授权。"),
 ("E-009","native journal/TLS实现","artifacts/source/native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp","Get-Content -LiteralPath './artifacts/source/native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp'","bit仅真实空row提交；verifyCurrent完整memcmp；physical仅state2row。")
]
E=[{"id":i,"title":t,"source_type":"file","source_ref":p,"artifact_path":p,"content_hash":sha(p),"repro_command":c,"raw_excerpt":x,"observed_at":now,"linked_workitem":"n/a","supersedes":"none"} for i,t,p,c,x in specs]
save('evidence.json',E)
fspec=[
 ("F-001","r6原生按模式完整收尾",["E-002","E-003","E-005","E-006","E-007"],"source-audit.json:selectedMarkerCoverage","原46不重跑，保留marker9过，仅补host；选择56项actual0、24806checks、0failures。","无需产品修改；沿用精确helper与逐模式证据。"),
 ("F-002","host closed原失败来自测试计数",["E-004","E-005","E-006","E-007"],"ProgressResourceNativeFixture.cpp:437-457; Test-ProgressResourceNativeRetry.py:145","host实际Start前无audio/player根，ordinary2非4、noJournal3非5；窄fixture/runner修正后通过，原失败保留。","保留fixture-r2及原失败，禁止覆盖旧manifest。"),
 ("F-003","bit只代表已分配初始化物理责任",["E-006","E-007","E-008","E-009"],"PlaybackSourceContext.cpp:296-347; NativePlaybackStockSourceHub.cs:244-278","ordinaryDormant、worker/class故障、journal耗尽均不能借bit新建Before；SEH已有origin只保留物理退出责任。","维持native真实row资格及bridge实际current验证。"),
 ("F-004","exact origin完成且bit不授新权限",["E-008","E-009"],"NativePlaybackStockSourceBridge.cs:135-187; NativePlaybackStockSourceTypes.cs:149-175","Before需要实际TLS/fullframe；Returned需要issued origin；physical需要精确journal。Unknown/Withdrawn无新Before/Clock/Fresh/Source。静态审查不代签managed。","latestAdapter顺序修正及managed/mapped另以新freeze验证。")
]
F=[{"id":i,"title":t,"severity":"info","category":"design" if i in ["F-003","F-004"] else "other","status":"validated","evidence_ids":e,"location":l,"impact":x,"confidence":"high","repro_steps":["核引用Evidence及原始manifest/源码快照。","运行本包只读hash验证并查相应patch/逐模式选择。"],"remediation":r,"optional_attack":""} for i,t,e,l,x,r in fspec]
save('findings.json',F)
save('paths.json',[
 {"id":"P-001","title":"断电后native证据收尾","path_type":"solve","start":"保留r6构建/原46/marker原失败","goal":"按模式完整56项真实通过证据","steps":[
 {"action":"重核r6/fixture-r2构建exit和所有manifestSHA，确认已完成。","evidence":"E-002","finding":"F-001"},
 {"action":"核原46及marker9pass不重复执行，保留host失败。","evidence":"E-003","finding":"F-001"},
 {"action":"精确白名单核测试修正，用fixture-r2显式原helper仅补host。","evidence":"E-006","finding":"F-002"},
 {"action":"冻结逐模式证据及源身份。","evidence":"E-007","finding":"F-001"}],"residual_risks":["并非10marker在fixture-r2全部重跑。","owned native不等于managed Clock/opaque receipt验收。"]},
 {"id":"P-002","title":"bit责任从native row到退出","path_type":"callflow","start":"真实resourcePhysical Dormant入口","goal":"完成物理责任且无新source权限","steps":[
 {"action":"同owner锁选空journal并提交bit与state1row。","evidence":"E-009","finding":"F-003"},
 {"action":"Hub真实Before回读current及nativeTLS/fullmemcmp。","evidence":"E-008","finding":"F-004"},
 {"action":"Bridge登记issued observation，Returned比较私有origin。","evidence":"E-008","finding":"F-004"},
 {"action":"finally结束callback/TLS后发布state2row，精确owner/seq/token完成。","evidence":"E-009","finding":"F-004"}],"residual_risks":["静态review只签冻结四文件及相关native；Adapter/RootRuntime/managed/mapped另签。"]}
])
final_rows=[]
for row in A['helperInputs']+A['runtimeReadOnlySnapshots']:
 live=Path(row['live']['path'])
 final_rows.append({"path":str(live),"expectedSha256":row['live']['sha256'],"actualSha256":hashlib.sha256(live.read_bytes()).hexdigest()})
same=all(r['expectedSha256']==r['actualSha256'] for r in final_rows)
save('review-source-final-check.json',{"observedUtc":now,"passed":same,"rows":final_rows})
if not same:raise RuntimeError('Reviewed source changed before signoff')
rows=A['selectedMarkerCoverage']['casesDetail']
table='\n'.join('| '+r['mode']+' | '+r['selectedRun']+' | '+str(r['checks'])+' | 0 |' for r in rows)
bridge='\n'.join(str(k+1)+'. '+t+'：'+x+' 位置：'+l+'。' for k,(i,t,l,x) in enumerate(checks))
put('2026-11-03_native-resource-r6-and-bit256-report.md',"""
# Native resource r6 收尾与 bit256 只读桥接审查

本域收尾通过：原46保留7969checks完整实际通过证据；新增10个responsibility模式保留已过9项，仅以fixture-r2和显式原r6helper补host-responsibility-closed，实际退出0、217checks。逐项选择合计 **56模式、24806checks、0failures**。这是逐模式选证，不是全部重新执行，详见[fixture-scope-proof.md](fixture-scope-proof.md)。

原host计数失败与关机上下文保留。fixture-r2经落盘核实于00:23:29–00:23:30已经完成actual0；本次没有新构建，没有将已完成构建改标为中断。

bit256窄范围只读审查未发现阻塞项：bit只在known armed/noFault的真实初始化物理责任成功分配journal时产生；Before仍需真正current/TLS/CallbackActive/owner/token/fullmemcmp，Returned仍需issued origin。普通closedgate无journal不收；Unknown/Withdrawn无新权限；bit不直接授Clock/Fresh/Source。

## 范围与身份

范围见[scope.md](scope.md)，合同见[IMPLEMENTATION-CONTRACT.md](artifacts/IMPLEMENTATION-CONTRACT.md)。flavor=null；本地代码与证据收尾无IOC/ATT&CK适用项。

| 项 | SHA256 / 结论 |
| --- | --- |
| 实际测试helper r6 | ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c |
| 原fixture r6 | d655568e6f1c0bf0778995f989ac5a6e40381c4f03c4bac57412160fa33831b7 |
| 补host fixture-r2 | 0043a132e7909111452ad9f913edbee271004be73fc024d890134b56b8233c24 |
| live/r6/fixture-r2 helper输入 | 7/7相等；functional可用冻结输入亦相等 |
| Source / Resource wire | 168 / 112 bytes，Source4 / Resource1 |
| 此次产品与测试源修改 / 新构建 | 无 |
| 此次新执行 | host-responsibility-closed一项，runner及fixture observed actual0 |

两构建各10source/20file均按manifest实读SHA/size；所有选择测试stdout/stderrSHA、summary及exit逐项一致。桥接冻结范围为Hub、Hub.ProgressResource、Bridge、Types四文件。[source-audit.json](source-audit.json)及[review-source-final-check.json](review-source-final-check.json)记录精确身份，[hash-manifest.json](hash-manifest.json)覆盖本包全部文件（除自身）。

## 原失败与模式选择

旧host实际责任已是Before/Returned/Receipt3/3/3、NoJournal3。它在Start前没有audio/player根，旧fixture/runner错误要求ordinary4/noJournal5，导致actual1、216checks/2failures。修正只在closed测试分支将host预期改成ordinary2/noJournal3，nonhost仍4/5，并补originals counters断言。产品输入未变。

| marker模式 | 选择的实际运行 | checks | actualExit |
| --- | --- | ---: | ---: |
"""+table+"""

marker选择计16837checks，加原46的7969为24806。原[失败manifest](artifacts/native-agent-responsibility-short-r6/native-retry-result.json)仍passed=false，新[host补测manifest](artifacts/native-agent-responsibility-host-r6-fixture-r2-resume-20261103/native-retry-result.json)为true。未声称其他9项在fixture-r2重跑，也未声称新originals计数断言已在nonhost执行。

## 桥接核对

"""+bridge+"""

## Evidence → Finding → Path

[evidence.json](evidence.json)有E-001范围、E-002构建、E-003原46、E-004旧失败、E-005fixture-r2、E-006补host、E-007身份/选证、E-008桥接审查、E-009native实现。[findings.json](findings.json)有F-001原生收尾、F-002测试缺陷、F-003真实journal资格、F-004exact-origin及权限边界，均只签本域。[paths.json](paths.json)记录P-001断电后选证与P-002native row/current观察至物理退出，每步关联E/F。[timeline.md](timeline.md)区分原始落盘、根代理报告的关机/重启及新补测，没有把未观测系统事件当成原生日志。

## 独立复核与限制

在本报告目录只读验证整个包：

~~~powershell
& 'C:/Python314/python.exe' './Freeze-NativeFinishR6.py' --verify
~~~

可直接用归档owned fixture及原helper复现host；此处是复现入口，收尾没有重复执行该模式。

~~~powershell
& './artifacts/native-agent-build-r6-fixture-r2/ProgressResourceNativeFixture.exe' './artifacts/native-agent-build-r6/RizToolbox.PlaybackSourcePrototype.dll' host-responsibility-closed
$LASTEXITCODE
~~~

本包只签native owned fixture与冻结bit256桥接静态边界，不签RootRuntime、最新Adapter deriveGraph-before-RetryNative顺序修正、managed firstlate/handofflate、mapped元数据/联合模式、Clock或SingleReturn opaque receipt/epoch组合验收。未访问game/UI/account、未执行游戏、未授生产权限、未改产品或测试源、未终止既有进程、未派代理。

范围、实际退出、哈希、旧失败、逐模式覆盖、手工代码验证、E/F/P及独立复核入口均已记录。
""")
print(json.dumps({"reportWritten":True,"reviewedSourceStillMatches":same,"evidence":len(E),"findings":len(F),"paths":2},ensure_ascii=False))
