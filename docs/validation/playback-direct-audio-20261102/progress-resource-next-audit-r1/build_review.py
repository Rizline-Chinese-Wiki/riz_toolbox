"""Build an evidence-bound review, without changing product or running tests."""
from pathlib import Path
import hashlib,json,re,datetime

OUT=Path(__file__).resolve().parent
REPO=Path(r'C:\Users\71957\Desktop\riz_toolbox')
STAMP=datetime.datetime.now(datetime.timezone.utc).isoformat()
def sha(b): return hashlib.sha256(b).hexdigest()
def save(name,x): (OUT/name).write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def ref(p):
    p=Path(p); b=p.read_bytes()
    return {'path':str(p),'sha256':sha(b),'length':len(b)}

source_paths=[
 '.docs/CURRENT-CHECKPOINT.md','.docs/foundation-expansion/NEXT-FUNCTIONAL-GAPS.md',
 'src/RizToolbox.Runtime/NativeRuntime.PlaybackHooks.cs',
 'src/RizToolbox.Runtime/Hooks/PlaybackSourceGroupTargets.cs',
 'src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceTypes.cs',
 'src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceBridge.cs',
 'src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceHub.cs',
 'src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceHub.ProgressWatch.cs',
 'src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceHub.Initialization.cs',
 'src/RizToolbox.Runtime/Hooks/NativePlaybackStockOccurrenceProducer.cs',
 'src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.SourceOwner.cs',
 'src/RizToolbox.Runtime/Hooks/NativePlaybackProgressPlayerLeafProof.cs',
 'src/RizToolbox.Runtime/Hooks/PlaybackSourceGroupLease.cs',
 'src/RizToolbox.Adapter/Gameplay/NativePlaybackActualSource.cs',
 'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockCodeProfile.cs',
 'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockRootedSource.cs',
 'src/RizToolbox.Adapter/Gameplay/NativePlaybackInitializationTracker.cs',
 'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.cs',
 'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.Initialization.cs',
 'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h',
 'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp',
 'native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc',
 'native/RizToolbox.PlaybackSourcePrototype/ProgressControlIsolationFixture.cpp',
 'native/RizToolbox.PlaybackStockFixture/ProgressPlayerWriterFixture.asm',
 'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressControlIsolationChecks.cs',
 'tests/RizToolbox.Gameplay.Tests/NativePlaybackStockMetadataChecks.cs',
 'docs/validation/playback-progress-player-writer-20261101/static-evidence.json',
 'docs/validation/playback-progress-player-writer-20261101/next-source-writer-report.md',
 'docs/validation/playback-direct-audio-20261102/stock-stop-integration-progress-r1/scope-index.json',
]
rows=[]
anchors={
 'src/RizToolbox.Runtime/NativeRuntime.PlaybackHooks.cs':['var methods = new','set_MusicPlayer','RequireReference'],
 'src/RizToolbox.Runtime/Hooks/PlaybackSourceGroupTargets.cs':['SourceRoleCount','ProgressMusicPlayer =>','Distinct().Count()'],
 'src/RizToolbox.Adapter/Gameplay/NativePlaybackActualSource.cs':['internal void VerifyGraph','<MusicPlayer>k__BackingField','get_clip'],
 'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp':['const bool progressControl','auto* fence =','auto ForwardIgnored','EntryRef1'],
 'native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc':['RelevantProgressWatchLocked','bool ProgressRelevantLocked','bool ProgressGraphMatches'],
}
for rel in source_paths:
    p=REPO/rel
    if not p.is_file(): raise FileNotFoundError(p)
    row=ref(p); row['relativePath']=rel
    text=p.read_text(encoding='utf-8-sig'); lines=text.splitlines()
    row['anchors']=[{'line':i,'text':line} for i,line in enumerate(lines,1)
                    if any(term in line for term in anchors.get(rel,[]))]
    rows.append(row)
save('source-manifest.json',{'schema':1,'observedAt':STAMP,'scope':'selected read-only sources; not a whole-tree freeze or new validation of current product',
    'repository':str(REPO),'rows':rows,'applicableAgentsFilesFound':[],
    'checkedAgentsLocations':['C:/AGENTS.md','C:/Users/AGENTS.md','C:/Users/71957/AGENTS.md','C:/Users/71957/Desktop/AGENTS.md',str(REPO/'AGENTS.md'),'recursive repository AGENTS.md search'],
    'productFilesWritten':0,'frozenInputsWritten':0})

profile=(REPO/'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockCodeProfile.cs').read_text()
hookpart=profile.split('HookBodies')[1].split('ReadOnlyBodies')[0]
hook_rvas=[int(x,16) for x in re.findall(r'new Body\("[^"]+",\s*(0x[0-9a-fA-F]+)',hookpart)]
bodies=json.loads((OUT/'fixed-bodies.json').read_text(encoding='utf-8'))
byr={int(x['rva'],16):x for x in bodies['functions']}
def at(rva,pc): return next(i for i in byr[rva]['instructions'] if int(i['rva'],16)==pc)
checks=[]
def check(name,ok):
    checks.append({'name':name,'passed':bool(ok)})
    if not ok: raise AssertionError(name)
check('exact full set_Clip body',byr[0x4016f0]['bytes']=='488951304883c130e96328e6ff')
check('set_Clip hash',byr[0x4016f0]['sha256']=='bb8036f89480f121c1bd96fd6bcd7c739d4d45f2bb58f562a0fb49b974aab390')
check('file runtime-function table excludes all 13 setter bytes',not byr[0x4016f0]['rfOverlap'])
check('old MusicPlayer leaf is separate RVA',byr[0x401700]['bytes']=='488951384883c138e95328e6ff')
check('set_Clip tail reaches barrier thunk',at(0x4016f0,0x4016f8)['operands']=='0x180263f60')
check('barrier first tail',byr[0x263f60]['bytes']=='e9eb170900')
check('barrier second tail',byr[0x2f5750]['bytes']=='e9cbb30200')
check('exported GC write barrier stores reference and converges on same terminal',byr[0x284100]['bytes']=='488bca498bd0e905080700' and byr[0x2f4910]['bytes']=='488911e908c20200')
check('GC terminal is atomic address-derived marking with RET',any(i['mnemonic']=='lock cmpxchg' for i in byr[0x320b20]['instructions']) and byr[0x320b20]['instructions'][-1]['mnemonic']=='ret')
check('Start retained Progress loads resource reference at +30',at(0x404990,0x404ad8)['bytes']=='4c8b4630')
check('Start gets resource before actual set_clip call',at(0x404990,0x404aee)['mnemonic']=='call' and at(0x404990,0x404b01)['operands']=='0x181511a20')
check('Start downstream AudioSource.set_clip has null MI',at(0x404990,0x404af8)['bytes']=='4533c0')
check('WaitUntil predicate also reads reference +30',at(0x400f60,0x400f91)['bytes']=='4c8b4330')
check('current HookBodies has 19 physical entries including GetTime',len(hook_rvas)==19 and len(set(hook_rvas))==19)
check('no current physical HookBodies member aliases new setter',0x4016f0 not in hook_rvas)
decl=json.loads((OUT/'metadata-excerpts.json').read_text(encoding='utf-8'))
check('setter is shared across 35 dump declarations',decl['sharedSetterDeclarationCount']==35)
check('Progress reference field is generic resource reference at +30',any('<Clip>k__BackingField; // 0x30' in x['text'] and 'IResourceReference<UnityEngine.AudioClip>' in x['text'] for x in decl['excerpts'][1]['lines']))
xrefs=json.loads((OUT/'direct-reference-scan.json').read_text())
check('no direct E8/E9 setter reference found in executable sections',not any(x['targetRva']=='0x4016f0' for x in xrefs['references']))
save('static-verification-result.json',{'schema':1,'passed':True,'checks':checks,'checkCount':len(checks),
    'validationKind':'static file assertions only; no native/managed behavioral fixture run',
    'functionalValidationComplete':False,'productionAdmission':False,'newLongTests':0,
    'dllMainCalls':0,'gameMethodCalls':0,'gameProcessesStarted':0})

contract={
 'schema':1,'id':'N2-progress-clip-resource-continuity-r1','decision':'recommend one bounded next increment after current T/formal-r2 closes',
 'evidenceStatus':'static reviewed; product implementation and behavioral acceptance not executed',
 'prerequisite':'Finish and seal original T/formal-r2 SDK18 scope before opening a new implementation branch; never restart live driver4104',
 'currentBaseline':{'frozenInputs':894,'productInputs':482,'nativeHelperSha256':'75a41e3e715e7025386a75a993e369e8e22aef3e66eb5de9becd5899a0424490','sourceManifest':'source-manifest.json'},
 'candidate':{'name':'ProgressClip resource-source continuity primitive','imageSha256':bodies['image']['sha256'],
    'declaringImage':'Assembly-CSharp.dll','declaringType':'InLevel.ProgressControl','method':'set_Clip',
    'returns':'System.Void','isStatic':False,'parameters':['IResourceReference<UnityEngine.AudioClip>'],
    'field':'<Clip>k__BackingField','fieldOffset':'0x30','fieldType':'IResourceReference<UnityEngine.AudioClip>',
    'rva':'0x4016f0','length':13,'bytes':byr[0x4016f0]['bytes'],'bodySha256':byr[0x4016f0]['sha256'],
    'abi':{'return':'void','RCX':'self','RDX':'reference value, including null; preserve pointer bits','R8':'incoming MethodInfo, preserve exactly, including null'},
    'sharedDeclarationsInDump':35,'fileRuntimeFunctionCoverage':False,'barrierPath':['0x263f60','0x2f5750','0x320b20'],
    'existingPhysicalAlias':False,'proposedNewRole':'ProgressClip (append 19; do not renumber 1..18)','expectedCounts':{'sourceRoles':19,'physicalEntriesWithGetTime':20,'metadataDeclarationsWithPitchAlias':21}},
 'semantics':{
    'effect':'Only Progress resource reference changes. Current AudioSource.get_clip and player/source graph do not immediately change.',
    'positiveBenefit':'Observe this known reference writer, preserve its original semantics, invalidate dependent future-resource capture credentials, and let a later actual Start consume the new reference and issue fresh capture/source authority.',
    'notAnObservedCurrentPlaybackBug':True,
    'currentClock':'Retain when it binds only the existing seven-root current AudioClip graph. Current ticket withdrawal remains at actual Start/set_clip or another existing source boundary. A ticket that explicitly depends on old resource continuity must expire at the setter Before.',
    'futureCapture':'Before the original store, advance a private resource revision for the exact watched Progress identity and invalidate all future-capture/initializer/source-open candidates that bind its preceding revision.',
    'noBlanketSourceFence':'Do not automatically advance owner.Source/Writer/Scheduling for a reference-only setter; that would conservatively revoke independent current-audio authority without proving a current-source change.',
    'interruptedCapture':'If a matching setter occurs during a resource-dependent initialization/capture, keep that candidate closed until its actual native physical exits drain, then recapture under a fresh revision; zero-inflight alone is not a completion credential.',
    'sameValueAndABA':'Every relevant observed entry advances resource revision, including same pointer, A→B→A and writes that later throw; pointer equality never revives a prior resource credential.',
    'newSource':'A setter occurrence never issues new current audio identity or generation. Only the existing actual Start→set_clip→readback/handoff path may issue a fresh source.',
    'oldCurrentTicketRule':'If implementation elects to make current Clock explicitly depend on resource revision, document that larger contract and prove its benefit; it is outside this minimal recommendation.'},
 'classification':{
    'reuse':'Reuse native ProgressRelevantLocked / RelevantProgressWatchLocked under the same owner lock as in-flight acquisition; borrow actual independently rooted four-root host or seven-root source Progress watch.',
    'identity':'Exact active/pending watched Progress pointer plus actual class; class tampering of matched current pointer faults closed, not foreign transparency.',
    'sameProgressTransition':'Resource revision continuity must survive active/pending watch replacement of the same retained Progress; a copied watch record or fresh ordinal must not reset it.',
    'differentProgress':'A setter of a different Progress or any unrelated shared-body class is foreign even if it shares current AudioSource. No resource/current fences, token, journal, initialization state or callback changes.',
    'incomingMi':'Installation resolves and seals the exact declared MI/type/RVA. Runtime relevance never requires incoming MI equality or non-null; the actual shared body writes current self+30 regardless of R8.',
    'original':'Exactly one original store and exactly one original barrier tail, same RCX/RDX/R8, void result and SEH propagation. Foreign original uses n3 ForwardIgnored TLS barrier and finally restoration.',
    'worker':'Matching worker entry records resource invalidation and faults via existing native-only worker path, without managed callback. Existing owner/group Unknown invalidates current native Clock and all authority dependent on that group, even when the setter alone would not change current audio. Owner-thread reference-only semantics do not override real group Unknown. Preserve roots until native physical responsibilities prove release; a narrower worker fault domain is unproved and outside this increment.'},
 'privateProtocol':{
    'resourceState':'Add an orthogonal private Progress resource revision credential. Bind issuer/owner epoch/retained Progress watch identity/resource revision/actual current reference to capture credentials; no public SDK values grant authority.',
    'wire':'Keep existing SourceV4/PlayerWatchV2/ProgressWatchV1 layouts intact where possible. Add a separately versioned private resource-continuity accessor/credential if needed; do not smuggle the revision through unrelated fence fields or public DTOs.',
    'roots':'A capture credential retaining a resource object past the observed call must own a real independent ResourceReference RootHandle, or prove it never dereferences an unretained pointer. ArgBits alone is diagnostic. Release transient roots only on owner thread after actual physical responsibility drains.',
    'sourceOpen':'Compare the captured resource revision again under the same native source-open lock and consume one issuer-owned credential. A late old owner retirement must not clear a newer resource state.',
    'noAutoResolve':'The hook must not call get_Resource, load assets, set AudioSource.clip, or invoke game methods to infer the resource; actual Start remains the consumer.'},
 'metadataAndProof':[
    'Resolve exact instance method and backing field +0x30 from current IL2CPP exports; validate non-byref constructed generic reference type and actual declaring class. Existing RequireReference hardcodes IL2CPP_TYPE_CLASS=18; do not apply it blindly to GENERICINST=21.',
    'Do not trust script.json C signatures for hidden self; dump omits self on many methods. RCX/RDX/R8 is established by body and supported native instance-reference ABI.',
    'Pin complete 13B body and E9 tail plus file-backed no-RF exclusion independently of existing offset38 setter. No caller-selected offsets, tail or body hashes.',
    'Implement only two issuer-owned exact leaf profiles, or a separate ProgressClip leaf proof; existing MusicPlayer prefix38 proof cannot accept prefix30 without explicit new seal.',
    'New actual Dobby relocation/thunk/unwind evidence must prove first eight bytes and +8 continuation, empty frame at every relevant boundary, RX/RO metadata and sticky mutation rejection. Static similarity is not execution proof.',
    'One new physical member at4016F0; 401700 remains separate. Preflight/commit/shared full-executable+DIR64 seals must account for the new privately issued patch, preserving unknown-hook refusal.'
 ],
 'implementationFiles':[
    {'path':'src/RizToolbox.Runtime/NativeRuntime.PlaybackHooks.cs','change':'Exact ProgressClip method+field+constructed reference metadata receipt.'},
    {'path':'src/RizToolbox.Runtime/Hooks/PlaybackSourceGroupTargets.cs','change':'Append role/profile, pin13B, include one physical member; keep historical fixture profiles explicit.'},
    {'path':'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockCodeProfile.cs','change':'Add exact hook-body pin without exempting any other executable span.'},
    {'path':'src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceTypes.cs','change':'Append role19; no reinterpretation of existing roles or public SDK.'},
    {'path':'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h','change':'Append matching role; define separately versioned private resource credential only if native compare is required.'},
    {'path':'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp','change':'Ref1 role validation, Progress classification, orthogonal resource revision Before, exact original/SEH/TLS forwarding, no blanket current-source fence.'},
    {'path':'native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc','change':'Reuse active/pending identity and atomic open; resource state continuity across same-Progress watch handoff.'},
    {'path':'src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceBridge.cs','change':'Support new exact Reference1 role.'},
    {'path':'src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.SourceOwner.cs','change':'Select Ref1 and exact prefix30 leaf proof, not the hardcoded prefix38 proof.'},
    {'path':'src/RizToolbox.Runtime/Hooks/NativePlaybackProgressPlayerLeafProof.cs','change':'Preserve old profile; introduce narrowly issuer-owned new exact leaf proof or adjacent new file.'},
    {'path':'src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceHub.ProgressWatch.cs','change':'Issue/verify resource credential from real retained watch and transient reference roots.'},
    {'path':'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.cs','change':'Handle resource-only observation before generic occurrence producer; do not route it as an external pitch/source leaf that automatically withdraws current Clock.'},
    {'path':'src/RizToolbox.Adapter/Gameplay/NativePlaybackInitializationTracker.cs','change':'Bind resource-dependent candidate revision; matching setter invalidates candidate and preserves physical drain responsibilities.'},
    {'path':'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.Initialization.cs','change':'Fresh recapture/source-open consumes current resource credential after actual Start.'},
    {'path':'native/RizToolbox.PlaybackStockFixture/ProgressPlayerWriterFixture.asm','change':'New explicit owned profile contains actual offset30 13B leaf and barrier; preserve old binaries/pins.'},
    {'path':'tests/RizToolbox.Gameplay.Tests/NativePlaybackStockMetadataChecks.cs','change':'Positive constructed generic reference metadata plus wrong type/field/method/RVA preflight rejection.'},
    {'path':'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceChecks.cs','change':'New bounded integration checks with real owned native entry and live Clock/future-capture assertions; fake source metadata addenda where needed.'}
 ],
 'acceptance':[
    {'id':'A01','test':'Owned native entry with actual 13B offset30 store and barrier tail, actual Dobby group commit and exact proof/unwind. Before sees old field; original store/barrier each exactly once; future resource revision already advanced.'},
    {'id':'A02','test':'Current watched self with null MI, exact MI and unrelated nonnull sentinel MI; preserve all raw incoming pointers. Null value stores null and records same invalidation without hook dereference.'},
    {'id':'A03','test':'Same value and A→B→A each expire old resource credentials before store; restore pointer cannot revive credential. Store/barrier then actual SEH propagates, abnormal and physical exit are recorded once.'},
    {'id':'A04','test':'Same-class foreign Progress sharing current AudioSource, unrelated class sharing physical4016F0, owner and worker, nested under current root: foreign original executes once; the foreign call itself and unrelated children create no journal/token/current/resource fence, callback or initializer changes. If its original deliberately calls a watched current ProgressClip or current AudioSource leaf, that related child is independently classified, has parent0 and its own journal/token; it advances the matching resource revision or existing source/writer/scheduling fence and performs required invalidation. Do not assert global zero activity in that deliberate related-child case. TLS restores after SEH.'},
    {'id':'A05','test':'Active host-only four-root and active/pending seven-root watches match exact retained Progress. Wrong class, stale/retired watch, same-address reuse and late predecessor retire cannot lend authority or reset revision.'},
    {'id':'A06','test':'Normally completed owner-thread pure setter on current Progress with independently bound current AudioClip leaves current Clock, native descriptor, current rate/prepared command usable while expiring a separately staged old-resource capture credential. No physical AudioSource.clip write at setter. This preservation does not apply to worker/class/SEH or any other case that actually faults the native group Unknown.'},
    {'id':'A07','test':'Fixture Start-shaped consumer actually reloads Progress+30 and invokes newRef.get_Resource after setter; actual set_clip child receives that new AudioClip once. Existing Start/set_clip fence then ends old current authority, completes real capture/readback/source-open and publishes one fresh usable Clock; old resource/capture credentials stay rejected.'},
    {'id':'A08','test':'Matching setter during resource-dependent initialization (including callback and pending watch) cannot open stale candidate; after actual native exit/drain, fresh actual Start under new revision succeeds. Avoid permanent fail-closed success criterion.'},
    {'id':'A09','test':'Current relevant worker has no managed callback and must exercise the existing native group Unknown path: current native Clock and all group-dependent authority are withdrawn by owner pump. Do not preserve current Clock merely because +30 alone would not change current audio. Roots remain retained until proven physical release, then return to expected count.'},
    {'id':'A10','test':'Metadata/body/prefix/tail/original/thunk/unwind/outside-executable/DIR64 tampering is rejected or sticky withdrawn; neither arbitrary field offset nor caller-provided mask enters the group.'}
 ],
 'requiredRegressionAfterImplementation':'Run affected short native and metadata/runtime integration checks first; then current Start/StockStop/ignored-original/handoff and formal SDK modes affected by the new helper/profile against its exact frozen source. Do not substitute n3 results for a changed helper. No new long tests run in this audit.',
 'evidenceLimits':[
    'No direct E8/E9 reference to4016F0 found; indirect calls and inline writes are outside that negative scan. No game path proved to call this setter.',
    'No actual incoming null/non-null setter MI history. Start clearing R8 before1511A20 proves downstream AudioSource.set_clip MI only.',
    'Resource interface helper name/get_Resource semantics inferred from metadata and dataflow; no actual asset load/getter executed.',
    'No reproduction of a stale current Clock or wrong current playback caused solely by Progress.set_Clip. Existing Start/source barriers already guard actual clip replacement.',
    'Hook observes only this known setter entry; inline/reflection/direct field/engine writers remain outside coverage. Full resource/source generation is not complete.'
 ],
 'exclusions':['production admission','default production provider enablement','full generation','unknown lower engine writers','arbitrary Seek rebuild','effect','all B01–B19','game installation/start/UI/account use'],
 'functionalValidationComplete':False,'productionAdmission':False,'currentGameExecution':False,
 'supportingArtifacts':['fixed-bodies.json','metadata-excerpts.json','pe-survey.json','direct-reference-scan.json','source-manifest.json','static-verification-result.json']
}
# Keep the handoff to seven bounded acceptance groups.
acceptance=contract['acceptance']
acceptance[0]['test']+=' '+acceptance[9]['test']
acceptance[4]['test']+=' '+acceptance[8]['test']
acceptance[6]['test']+=' '+acceptance[7]['test']
contract['acceptance']=acceptance[:7]
save('next-function-contract.json',contract)

scope='''# 审查范围

auth: 父任务明确授权的离线只读源码/既有证据/固定映像静态审查；报告仅写本目录。

in_scope: 固定 SHA853d GameAssembly.dll 的 ProgressControl.set_Clip@4016F0、同址共享声明、GC屏障、Start消费链、当前source覆盖及一个下批合同。

network_profile: offline；未连接游戏、账号或外部服务。

out_of_scope: 修改主仓/产品/已有冻结输入，启动长测，安装或启动游戏，执行游戏原函数/DllMain，游戏UI及账号，撤销B账号。未操作原SDK driver4104。当前T/formal-r2终态仍由root收尾。

只读输入原件和hash见 source-manifest.json、fixed-bodies.json、metadata-excerpts.json、pe-survey.json。直属skill共享资源缺失时使用reverse-skill-router内同名资源；旧tool-index记录IDA未安装，本次使用已有pefile/capstone，未安装或启动IDA。
'''
(OUT/'scope.md').write_text(scope,encoding='utf-8')
(OUT/'timeline.md').write_text('# 审查时间线\n\n'+
 '\n'.join('- '+STAMP+' '+s for s in [
 '读取当前检查点和NEXT-FUNCTIONAL-GAPS顶部，保持既有SDK长测与产品冻结范围。',
 '核对映像及dump SHA，完成PE导入/导出/异常表静态survey。',
 '解码setter及Start消费链，确认35同址声明、无当前成员碰撞、GC屏障汇合。',
 '核对Progress watch、当前七根和初始化/Router语义，收窄为资源连续性而非当前播放bug。',
 '写入静态18项验证、唯一建议合同、范围与报告；未运行行为fixture或新长测。'])+'\n',encoding='utf-8')

get=lambda rel: next(r['sha256'] for r in rows if r['relativePath']==rel)
report=f'''# Progress Clip 资源来源连续性审查

本次建议只补 `ProgressControl.set_Clip` 这个已知资源引用写入口的私有连续性凭据。它改的是下一次 Start 使用的 `IResourceReference<AudioClip>`，不会立即改当前 `AudioSource.clip`。当前 Start 和真实 set_clip 已有换源前屏障，本次没有复现“纯资源 setter 导致当前播放错误”。若只给 setter 加全局 Source fence、撤当前 Clock，却没有新引用可正常继续捕获的正向链，收益只是保守拒绝，不能算本合同完成。实现须等当前 T/formal-r2 收尾封存。

## 范围与状态

[scope.md](scope.md) 记录授权和离线边界；`flavor=null`。本报告只完成静态审查，`functionalValidationComplete=false`、`productionAdmission=false`。未改产品、主仓或冻结输入，未跑行为fixture或新长测，未执行游戏原函数/DllMain/游戏进程/UI/账号。原SDK进程不在本审查操作范围。时间线见 [timeline.md](timeline.md)。

输入映像为 `D:/riz-build-cache-next/playback-direct-audio-managed-r1-20261102/mapped-r4/image/GameAssembly.dll`，SHA `{bodies['image']['sha256']}`。旧dump原件为 `C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs`，SHA `{bodies['dump']['sha256']}`；与既有writer静态包一致。工具为本机pefile/capstone；版本及PE完整导入/导出表在 [pe-survey.json](pe-survey.json)。

## 核实的机器码与消费路径

| 目标 | 固定证据 | 语义与边界 |
|---|---|---|
| Progress backing field | dump98148：`IResourceReference<UnityEngine.AudioClip> <Clip>k__BackingField //0x30` | 资源引用，不是当前AudioClip；实例字段，不是标量时间。 |
| setter4016F0 | `48895130 4883c130 E96328E6FF`，13B，SHA `{byr[0x4016f0]['sha256']}` | `MOV [RCX+30],RDX; ADD RCX,30; JMP263F60`。RCX=self，RDX原引用，R8 incoming MI；本体不读取MI。 |
| file RF | 完整113821行异常表对这13字节无覆盖 | 仅静态file no-RF；新Dobby/thunk和动态RF/展开尚须owned fixture实际证明。 |
| 屏障 | 263F60→2F5750→320B20 | 导出`il2cpp_gc_wbarrier_set_field@284100`搬RDX→RCX、R8→RDX，经2F4910写`[RCX]`后同样到320B20；终端按字段地址标记位并`lock cmpxchg`。可证同一GC写屏障，不应再额外补一次屏障。 |
| Start state1 | 404AD8读`[RSI+30]`；404AEE调用2EF0；结果RAX→RDX；404B01调用1511A20 | 新引用影响实际下一Start选出的AudioClip。2EF0为通用调用辅助；`get_Resource`名称按资源metadata和数据流推断，未执行getter或加载资源。 |
| 等待谓词 | 400F91也读取+30并经2EF0后检查结果 | 等待过程也消费当时的资源引用，不应缓存setter前指针代替真正重读。 |
| 共享入口 | dump35个声明共享4016F0 | 其余包括Exception.HelpLink、UI、网络字段等34个声明；必须按实际根对象筛选，不可按RVA使所有调用污染播放。 |
| 现物理组 | 18source＋GetTime，共19独立RVA，无4016F0 | 401700是offset38的另一入口。下一只新增一个物理成员；不能重复hook同址声明。 |

完整指令和原始字节见 [fixed-bodies.asm.txt](fixed-bodies.asm.txt)、[fixed-bodies.json](fixed-bodies.json)。metadata摘录及35声明见 [metadata-excerpts.json](metadata-excerpts.json)。E8/E9扫描没有发现4016F0直接引用；getter的两个直接引用属于共享的其他类，不是Progress消费证据。该负扫描不排除间接调用/内联store，也不签stock实际调用过setter。`Start@404AF8`清R8D只能证明下游AudioSource.set_clip用空MI，不能证明此资源setter的MI历史。

## 为什么这是下批窄功能，而非当前播放修复

当前七根独立保留Controller/Information/Progress/Pause/Player/AudioSource/AudioClip，`NativePlaybackActualSource.VerifyGraph`检查Progress+38、Progress+28、Player+10及AudioSource.get_clip，没有把Progress+30资源引用作为当前播放依赖。因此单独setter发生后当前clip仍可继续正确播放。现有Start入口以及真实AudioSource.set_clip会在真正换clip前推进原source边界。静态证据没有表明现Clock因此漏撤，不能称已复现bug。

本批的正向收益应是：为这个**已知入口**发行一次真实资源写入观察、按Progress生命周期维护资源修订；旧的future-capture/initializer/source-open候选能准确过期，新的候选能在实际Start读取新reference后成功。纯setter时，现有独立current-audio Clock仍可读且可执行本身合法的rate命令。到实际Start/set_clip才按已有规则结束旧current票，并经过实际capture/readback/handoff恢复唯一fresh Clock。资源setter本身不发行当前source identity或generation。

owner-thread正常完成且native group保持Known时，必须撤当前票的条件是它确实绑定了已结束的资源修订，或者实际Start/set_clip/其他现有source边界已经发生；当前票只绑定既有七根current AudioClip时，仅撤依赖旧resource的future-capture票。matching worker仍走既有native-only故障路径，真实全组Unknown必须由owner pump撤当前native Clock及所有依赖该组的权限，不能以资源字段未直接换clip为由忽略Unknown；更细worker故障域隔离未证、也不在本批。本批不建议把正常资源revision混进owner.Source/Writer/Scheduling。新role必须在Router的通用“外部leaf使producer失败”路径之前分流，并保留真实PhysicalExit责任。

## 唯一建议合同及实施落点

完整可机读合同是 [next-function-contract.json](next-function-contract.json)，包含精确metadata/ABI/body、17个实施文件、7组验收及未证边界。

可直接复用 `ProgressRelevantLocked` 的active/pending真实Progress根＋class筛选，且与in-flight/source-open同锁；不需另做caller传入的地址名单。watch本身不包含+30资源revision：新增一个正交私有资源连续性状态，保留SourceV4/PlayerWatchV2/ProgressWatchV1原wire，必要时另发版本明确的private credential。相同Progress在active/pending移交时延续修订；不同Progress和同址其他类透明原样通过。长期持有资源引用的捕获票必须独立RootHandle，或证明只持诊断地址且从不解引用失去保留的指针。

安装阶段锁定真实声明MI、字段偏移、实例/void/构造泛型引用类型和RVA。现`RequireReference`硬编码type-code18，不可直接拿它核泛型实例type-code21；不能把AudioClip类当作IResourceReference<AudioClip>参数类。运行时判相关性不要求incoming MI精确或非空：该本体无论R8是什么都会写current self+30。null、实际MI、无关nonnull MI均按真正self分类，原参数位不变。

本体与已合MusicPlayer叶子只差字段偏移及E9位移，现`NativePlaybackProgressPlayerLeafProof`明确硬编码38。因此新offset30必须另有私有精确profile/证明，不能放宽为任意offset/tail/hash。新Dobby搬移须独立证明首8B、target+8继续、空展开、RX/只读元数据和持续篡改拒绝。新role追加19；完整新profile为19source、20物理成员（含GetTime）、21metadata声明（含pitch别名）。历史fixture/pin及旧证据保留。

## 最小但有意义的owned验收

1. 实际13B setter＋own barrier、真实Dobby group安装：Before看到旧+30值，资源修订已推进；随后原store和屏障各恰好一次，void/MI/引用原位保持，SEH与PhysicalExit真实完成。
2. current self的null/精确/无关nonnull MI，null引用，同值、A→B→A、写后SEH均使旧resource凭据不可恢复。只有owner-thread正常完成且组Known的纯setter保持独立current Clock/rate/prepared权限；matching worker真实Unknown必须撤当前Clock。
3. 同class foreign Progress共用current AudioSource、无关class共享入口，owner/worker及嵌套原调用都透明通过；foreign自身及无关child零journal/token/fence/initializer污染。若foreign original故意再调用watched current ProgressClip或current AudioSource leaf，该related child必须parent0独立计数、产生自己的journal并推进正确resource或现source/writer/scheduling fence，不能要求该用例全局零变化。需要n3 ForwardIgnored清TLS并在SEH finally恢复。
4. four-root host、seven-root active/pending、退休/ABA/晚retire都验证；setter打断依赖旧resource的capture时不得让旧candidate source-open。实际exit/drain后允许新捕获，不以永久NotReady作为通过。
5. 正向消费者必须真正从Progress+30重读newRef，调用fixture newRef.get_Resource，再走真实set_clip child；证明实际Start此时才撤旧current票，并完成新的source-open/readback、唯一Clock及合法命令。测试不可只记录计数或主动构造新binding绕过消费者。

以上是**待执行验收**。本次只运行18项静态文件断言，结果 [static-verification-result.json](static-verification-result.json)；不把它签成native/Runtime/SDK功能通过。

## Evidence → Finding → Path

| Evidence | source_ref / 内容 | 可重现与hash |
|---|---|---|
| E-imports/E-exports | 原映像PE survey：228导入、386导出；含gc/文件/加载器/同步依赖，完整名称在JSON | `python inspect_static.py`；本地artifact hash见artifact-index.json |
| E-body | 原映像setter、屏障、Start/predicate，完整body/异常表排除 | 同命令；fixed-bodies.json/asm；原映像SHA见上 |
| E-meta | dump98128–98175与91685–91728、35同址声明 | 同命令；metadata-excerpts.json；dump SHA见上 |
| E-source | 29份选读源/既有报告逐项hash、关键行号 | `python build_review.py`；source-manifest.json，不称整树freeze |
| E-xref | 可执行区E8/E9候选及所在RF的指令边界核对 | `python inspect_static.py`；direct-reference-scan.json |
| E-static-check | 本次18项静态断言 | `python build_review.py`；static-verification-result.json |

| Finding | status / severity | evidence_ids | location / confidence | 结论 |
|---|---|---|---|---|
| F01 | validated / n/a_re | E-body,E-meta | 4016F0 / high | 已知共享资源引用叶写入口；GC屏障路径可证。 |
| F02 | validated / n/a_re | E-body,E-meta,E-source | 404AD8→404B01 / high | 下一Start消费资源引用；当前AudioClip不立即变化，已有实际换源屏障。 |
| F03 | validated / n/a_re | E-source,E-xref | Hooks目标表/ActualSource / high | 此入口不在当前19物理成员；负xref不代表stock从不调用。 |
| F04 | candidate / n/a_re | E-body,E-meta,E-source | 新ProgressClip资源凭据 / high | 只有正交资源连续性＋新引用成功重捕获才是本批功能收益；未实施/未行为验收。 |

P01 (`path_type=callflow`)：资源setter4016F0修改Progress+30（E-body/F01）→GC屏障263F60/2F5750/320B20（E-body/F01）→下一实际Start404AD8加载该资源引用、404AEE取得资源（E-body/E-meta/F02，getter命名为静态推断）→404B01进入已有AudioSource.set_clip边界（E-body/E-source/F02）→后续捕获应依据新resource连续性票（E-source/F04，待实施/执行）。P02：纯setter→当前AudioSource/七根未变→独立current Clock保持；future旧resource票过期→真实Start/newRef正向重捕获（F04合同，非本轮actual结果）。

## 原件及完整性

当前选读Hook声明SHA `{get('src/RizToolbox.Runtime/NativeRuntime.PlaybackHooks.cs')}`；group目标表SHA `{get('src/RizToolbox.Runtime/Hooks/PlaybackSourceGroupTargets.cs')}`；Progress watch native SHA `{get('native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc')}`；ActualSource SHA `{get('src/RizToolbox.Adapter/Gameplay/NativePlaybackActualSource.cs')}`。所有选读源路径、hash、行号见source-manifest.json；本目录逐件hash见artifact-index.json。报告仅引用当前T n3状态背景，不重新验T，也不把仍运行的SDK矩阵视作故障。

剩余边界：未证实际stock setter caller/MI历史、任意inline/reflection/直接字段writer、未知engine lower、完整resource/source generation；未启production provider，未做任意Seek/effect或B01–B19整体完成声明。新功能不得借旧n3的SDK/ABI通过代签更改后的helper。
'''
(OUT/'2026-11-02_progress-clip-next-gap-review.md').write_text(report,encoding='utf-8')
index=[]
for p in sorted(OUT.iterdir()):
    if p.is_file() and p.name!='artifact-index.json': index.append(ref(p)|{'artifact':p.name})
save('artifact-index.json',{'schema':1,'observedAt':STAMP,'kind':'read-only next gap review','files':index,
    'functionalValidationComplete':False,'productionAdmission':False})
print(json.dumps({'passed':True,'staticChecks':len(checks),'selectedSources':len(rows),'artifacts':len(index),
    'contract':str(OUT/'next-function-contract.json'),'report':str(OUT/'2026-11-02_progress-clip-next-gap-review.md')},ensure_ascii=False,indent=2))
