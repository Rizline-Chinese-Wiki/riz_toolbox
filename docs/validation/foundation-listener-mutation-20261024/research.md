# UnityEvent mutation 入口与 ABI：固定文件离线研究

2026-10-24。最可用结论是同时观察已确认的Event/EventBase/Calls/Group入口，并保留MutationClosed=false：上层AddListener、AddCall、RemoveAll和Dirty会内联低层列表修改，单挂InvokableCallList方法入口会漏。显式引用参数要求新增精确void1/void2 native transport；既有Void0只适合真正零显式参数的方法。本文没有启动游戏、映射DLL或执行任何gamecode，也没有修改生产/测试源码或构建。

范围来自本轮root委派：仅研究给定固定GameAssembly和已生成dump/script，为真实观察接线提供有限ABI依据。采用ida-reverse技能；本agent没有可调用的idapro工具，因此按指定优先路径使用现有Python、pefile和Capstone离线读取，未启动IDA服务/GUI，不能据此声称本机没有安装IDA。工具版本Python3.14、pefile2024.8.26、Capstone5.0.6，详细Python版本见JSON。

## Evidence

source_type=file/command，observed_at=2026-10-24；下列为本agent实际读取或生成的文件。权威机器码是固定DLL，dump/script为命名和签名辅助，不把未经运行时核实的dump当作活体metadata。

| E-id | source_ref / artifact_path | SHA-256 | 观察 |
|---|---|---|---|
| E-image | D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll | 853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79 | PE AMD64，ImageBase180000000，ImageSize243C000，固定原始文件 |
| E-dump | C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs | 092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb | 方法/字段声明与RVA，相关类从114168行起 |
| E-script | 同目录script.json | a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10 | ScriptMethod命名、签名、RVA及共享地址集合 |
| E-abi | [mutation-abi.json](mutation-abi.json) | 7e7f638cbb67f8fd0235e50f51f1a3554afd8827382ae87c7043d61e52a6e363 | 17个有限研究入口、RF/unwind、前32bytes、完整body hash、关键指令和call/tail边 |

E-imports/E-exports纳入E-abi.survey：实际解析228个import、10个DLL和386个export。IAT包含ADVAPI32的CryptAcquireContextW/CryptGenRandom、WS2_32网络、KERNEL32文件/线程/虚拟内存/动态加载、ole/COM/baselib等；此导入概况不推导恶意行为。实际export il2cpp_init RVA2841C0、il2cpp_runtime_invoke2845E0；后者入口E9到2AF0E0。没有IAT修复或动态加载追踪。

本轮实际使用的首轮定位命令（PowerShell）：

```powershell
rg -n --max-count 40 'class UnityEventBase|class UnityEvent|class InvokableCallList' 'C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output' -g '*.cs'
Get-FileHash -Algorithm SHA256 -LiteralPath 'D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll'
python -c 'import importlib.util,json;print(json.dumps({n:importlib.util.find_spec(n) is not None for n in ["pefile","capstone"]}))'
```

机器码复核通过PowerShell here-string管给`python -u -`，使用`pefile.PE(path, fast_load=True)`、`parse_data_directories([1,0])`读import/export；以OptionalHeader.DataDirectory[3]原始12字节RUNTIME_FUNCTION表二分定位完整body，Capstone在RVA地址空间解码，不从第一个ret截断多分支body。等价的有限重核命令如下，输出选定完整RF内的机器指令而不执行它们：

```powershell
@'
import struct,bisect,pefile,capstone
pe=pefile.PE(r'D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll',fast_load=True)
d=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]; raw=pe.get_data(d.VirtualAddress,d.Size)
rf=[struct.unpack_from('<III',raw,i) for i in range(0,len(raw)-11,12)]
starts=[r[0] for r in rf]; md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
for rva in [0x15640a0,0x15630e0,0x1563c50,0x1563b80,0x15631a0,0x154ded0,0x154e280]:
    entry=rf[bisect.bisect_right(starts,rva)-1]
    assert entry[0] == rva < entry[1]
    print('FUNCTION',hex(rva),'END',hex(entry[1]),'UNWIND',hex(entry[2]))
    for ins in md.disasm(pe.get_data(rva,entry[1]-rva),rva):
        print(hex(ins.address),ins.bytes.hex(),ins.mnemonic,ins.op_str)
'@ | python -u -
```

## 推荐接线表

全部为instance方法；隐式self不能从script Signature中省掉。Windows x64真实参数合同：kind0=`void(self RCX, hidden MethodInfo RDX)`；kind1=`void(self RCX, arg0 RDX, hidden MethodInfo R8)`；kind2=`void(self RCX, arg0 RDX, arg1 R8, hidden MethodInfo R9)`。反射对象System.Reflection.MethodInfo参数与尾部IL2CPP MethodInfo元数据指针是不同对象。JSON存有每个入口的实际prolog/参数搬运及RF证据。

| receiver | 方法及显式参数 | RVA | kind / 返回 | 机器码覆盖限制 |
|---|---|---|---|---|
| Event | UnityEvent.AddListener(UnityAction call) | 15640A0 | 1 / void | 构建Invokable后内联runtime list Add，不call下层AddListener/AddCall |
| EventBase | AddCall(BaseInvokableCall call) | 15630E0 | 1 / void | 内联runtime list Add，不call154DED0 |
| EventBase | RemoveListener(object targetObj, Reflection.MethodInfo method) | 1563C50 | 2 / void | self换成m_Calls，R9置0，tail-jmp154E280 |
| EventBase | RemoveAllListeners() | 1563B80 | 0 / void | 内联Clear，不call154E110 |
| EventBase | DirtyPersistentCalls() | 15631A0 | 0 / void | 内联ClearPersistent，再event.m_CallsDirty=1 |
| EventBase | RebuildPersistentCallsIfNeeded() | 1563B40 | 0 / void | dirty时call Group.Initialize，随后dirty=0 |
| EventBase | OnBeforeSerialize()/OnAfterDeserialize() | 1563CF0 | 0 / void | 两个metadata同入口，RDX置0后tail-jmpDirty；不能重复claim同入口 |
| Calls | InvokableCallList.AddListener(BaseInvokableCall call) | 154DED0 | 1 / void | runtime list Add、needsUpdate=1 |
| Calls | AddPersistentInvokableCall(BaseInvokableCall call) | 154DF90 | 1 / void | persistent list Add、needsUpdate=1；Group.Initialize真实call此入口 |
| Calls | RemoveListener(object targetObj, Reflection.MethodInfo method) | 154E280 | 2 / void | 遍历并virtual Find，收集移除项、重建executing |
| Calls | Clear() | 154E110 | 0 / void | 清runtime并替换executing为persistent复制，needsUpdate=0 |
| Calls | ClearPersistent() | 154E050 | 0 / void | 清persistent并替换executing为runtime复制，needsUpdate=0 |
| Group | PersistentCallGroup.Initialize(InvokableCallList invokableList, UnityEventBase unityEventBase) | 1550EE0 | 2 / void | 枚举persistent call、GetRuntimeCall，然后AddPersistent；不代表delegate执行 |
| EventBase | PrepareInvoke() | 1563A60 | reference | 内联Rebuild和Calls.Prepare，非void operation；沿既有typed Factory观察 |
| Calls | PrepareInvoke() | 154E1D0 | reference | 重建/返回actual executing list；既有独立Prepare观察 |
| Event | UnityEvent.Invoke() | 15642E0 | 0 / void | 已有parent/listener lane；本体内联Rebuild与Prepare，不能重装同地址detour |

上表kind只是所需新transport的参数分类，尚未表示这些入口已经发布/安装/实测。当前helper NativeVoid0不能直接挂kind1/2，否则RDX会被错当hidden MethodInfo，R8/R9与原调用合同也未被现有原函数调用签名保留。零参数Clear/Dirty/Rebuild虽能复用Void0 ABI，仍须独立slot、精确metadata身份和首次发布安全证明，不能由本离线研究授权热写正在运行的游戏。

未在给定dump/script找到非泛型`UnityEvent.RemoveListener(UnityAction)`，也没找到`UnityEventBase.AddListener(object, Reflection.MethodInfo)`。不能借常见Unity源码版本补出本构建没有证据的方法。泛型UnityEvent<T>.AddListener/RemoveListener都指向1AFAD0；该RVA实际是527个metadata方法共享的generic转发体，在1AFAF3执行`call [rax+10h]`，RAX来自隐藏MethodInfo R8。它不是空ret，但不能当成唯一Add/Remove业务body，初版allowlist排除，具体inflated入口尚未核实。

## 内联、共享及 dirty 的实际证据

| F-id / status | evidence_ids / location | 结论 |
|---|---|---|
| F-abi，validated静态 | E-image,E-dump,E-script,E-abi；各prolog与参数使用 | 13类void mutation入口可按0/1/2引用参数分类，self/hidden MethodInfo必须补齐。动态metadata/MethodPointer匹配仍待运行时合同核验 |
| F-inline，validated静态 | E-abi；15640A0/15630E0/1563B80/15631A0完整RF | 上层直接写list/version/count/needsUpdate，body中没有call对应低层方法。上下层都观察会重复记录同一逻辑变化，不能相加称不同mutation |
| F-rebuild，validated静态 | E-abi；1563A86/1563B62/156435A | Base.Prepare、Rebuild和UnityEvent.Invoke都能直接call1550EE0；Invoke不经过独立Rebuild/Prepare入口，必须保留现有mid/listener实际路径 |
| F-shared，validated静态 | E-script,E-abi；1563CF0与1AFAD0 | 序列化两metadata共享同void0 tail可单一物理claim；generic527共享转发不能按地址唯一归属 |
| F-not-closed，accepted boundary | E-abi；有限17入口 | 只确认所查body及其直接call/tail，不是全映像xref/全部写者证明。原生直接字段写、所有generic/type实例、反射与运行时mutation路径仍可超出入口集合，MutationClosed继续false |

以上severity=n/a_re、category=design/reverse_algo、confidence=high；validated只表示固定文件机器码与dump的静态对照，无动态执行。alias数量来自script.json全表相同RVA聚合，不是函数被调用次数。

dirty字段是EventBase+20h。有限body核到的写点：ctor1563D3C置1、Dirty1563265置1；Rebuild1563B67、Base.Prepare1563A8B、UnityEvent.Invoke156435F置0。序列化两回调tail到Dirty。构造期写1不属于已存在事件接收者的常规mutation观察范围；这五处不是全映像写者枚举。Calls.needsUpdate位于+28h，与event.dirty不是同一字段。

固定体内Clear/RemoveAll不只是标dirty：实际清runtime，把executing换成persistent的复制并置needsUpdate0。ClearPersistent/Dirty清persistent，executing换成runtime的复制并置needsUpdate0；Dirty再把EventBase.dirty置1。AddRuntime/AddPersistent设置needsUpdate1。因此观察时须分别保留实际Calls/List身份及前后快照，不能用“入口已返回”等同“下次Invoke一定使用旧list”。

## 可复用通道与未证内容

既有runtime_invoke native transport可以在真正经过export的调用正常返回后，用实际MethodInfo/self/args识别这些方法；它只有After，原SEH/C++没有After，也不能捕获编译后的直接native调用，不能单凭它封口所有mutation。它不赋予故障后读取args或已释放引用的权限，必要引用根仍须调用前责任持有。

P-public-add，path_type=callflow：UnityEvent.AddListener15640A0→创建Invokable→内联runtime list Add→needsUpdate1；不经过Base.AddCall或Calls.AddListener（E-abi/F-inline）。P-remove，path_type=callflow：Base.RemoveListener1563C50→真实tail-jmpCalls.RemoveListener154E280→virtual Find/实际移除→executing替换（E-abi/F-abi）。P-rebuild，path_type=callflow：Invoke或独立Prepare/Rebuild内联分支→Group.Initialize1550EE0→PersistentCall.GetRuntimeCall1551620→Calls.AddPersistent154DF90→dirty0（E-abi/F-rebuild）。所有终点是静态调用链，均无游戏运行证明。

本轮确定入口可支持下一步实际typed observation，但仍缺：MethodInfo精确运行时解析/参数类型验证、未占用slot与物理detour共享策略、新入口的生产首写发布安全、异常/递归实际配对、GC/引用采用、所有generic具体实现、全部dirty/list字段写者、provider转交/取消/全run消费者退休。未执行游戏、未安装新包、无并发首写安全或全mutation闭包结论。接线应继续保留Pending及既有安全标志，不扩大RequestPlay授权。
