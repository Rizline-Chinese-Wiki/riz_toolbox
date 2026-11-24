# Stop 请求入口与 ABI：固定文件离线复核

2026-10-24。六个确认入口应分别观察：两个public `StopCoroutine(Coroutine/IEnumerator)` 内联内部包装的检查与icall解析/缓存，直接尾跳引擎函数，**不调用**两个同名internal包装入口。仅挂internal会漏掉public调用。Stop入口返回仅能记为请求返回，不证明引擎已移除scheduler登记、未来生产者已封口或取消完成。

本轮使用已采用的ida-reverse技能与tool-index离线路径；当前没有可调用IDA工具，未启动或安装IDA/服务。Python pefile/Capstone只读磁盘PE和生成dump/script，没有映射DLL、调用gamecode、启动游戏、构建或安装。分析由只读子代理交叉核对后，本agent重新解码六个完整pdata范围并生成[stop-abi.json](stop-abi.json)。静态研究不是实际IL2CPP metadata解析或游戏动态证明。

## Evidence

source_type=file/command；observed_at=2026-10-24。source_ref同时为artifact_path，hash为实际SHA-256。

| E-id | source_ref / artifact_path | SHA-256 | 观察 |
|---|---|---|---|
| E-image | D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll | 853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79 | AMD64，35,146,752 bytes；ImageBase180000000、SizeOfImage243C000 |
| E-dump | C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs | 092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb | MonoBehaviour六声明及参数类型，112648–112664行 |
| E-script | 同目录script.json | a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10 | 每个目标RVA只有一条ScriptMethod地址记录；不等于活体元数据已核实 |
| E-abi | [stop-abi.json](stop-abi.json) | 1bfe8aecc8149f3cedadab6c8ba159e0fd1fc4485d8abea36dc07b322bd05fbe | 六body完整解码、RF/unwind、body hash、prefix32、指令/调用边、icall字符串和缓存 |

E-imports/E-exports写入JSON.survey：实际228 imports/10 DLL、386 exports。KERNEL32 148、USER32 1、ADVAPI32 4、ole32 7、OLEAUT32 11、SHELL32 2、WS2_32 37、IPHLPAPI 3、dbghelp 2、baselib 13。包含文件/虚拟内存/动态加载、网络、CryptAcquireContextW/CryptGenRandom及COM等导入；不据此推导恶意行为。导出`il2cpp_resolve_icall`实际RVA2845D0首条E9→2EDBD0；本批包装使用的named-string helper RVA265060不应直接冒称该导出或未经核实的运行时函数身份。

## 精确 allowlist

receiver均为`UnityEngine.MonoBehaviour`，返回均为`System.Void`、实例方法。只有kind0和kind1：`void(self RCX, hidden MethodInfo RDX)`；`void(self RCX, referenceArg RDX, hidden MethodInfo R8)`。机器码直接证明self/reference寄存器；成功路径不读取hidden MethodInfo寄存器，其位置来自IL2CPP入口约定，并非实际hidden指针值的运行时证明。script签名系统性省略this，不能照抄成native原型。

| 方法及显式参数 | RVA / kind | 完整RF半开范围 | 引擎尾跳 / cache |
|---|---|---|---|
| StopAllCoroutines() | 153D6C0 / 0 | [153D6C0,153D6F3) | jmp rax @153D6F0；1FB1770 |
| StopCoroutineFromEnumeratorManaged(System.Collections.IEnumerator) | 153D700 / 1 | [153D700,153D742) | jmp rax @153D73F；1FB17D8 |
| StopCoroutineManaged(UnityEngine.Coroutine) | 153D750 / 1 | [153D750,153D792) | jmp rax @153D78F；1FB17D0 |
| StopCoroutine(System.String) | 153D7A0 / 1 | [153D7A0,153D7E2) | jmp rax @153D7DF；1FB1768 |
| StopCoroutine(UnityEngine.Coroutine) | 153D7F0 / 1 | [153D7F0,153D8F1) | jmp rax @153D85C；1FB17D0；内联internal包装 |
| StopCoroutine(System.Collections.IEnumerator) | 153D900 / 1 | [153D900,153DA01) | jmp rax @153D96C；1FB17D8；内联internal包装 |

StopAll unwindData=1CE4D38；另五项为1CE4D20。JSON完整body hash绑定固定映像。六个地址在生成script范围内各一条记录，没有在该辅助数据发现共享实现地址；不能由此排除未知机器码别名、未列元数据、其他调用方内联或直接icall。

实际字符串：`UnityEngine.MonoBehaviour::StopAllCoroutines()`在185D880；`StopCoroutineFromEnumeratorManaged(System.Collections.IEnumerator)`在185DC30；`StopCoroutineManaged(UnityEngine.Coroutine)`在185DBE0；`StopCoroutine(System.String)`在185D848；public检查的`IsObjectMonoBehaviour(UnityEngine.Object)`在185DAE0/cache1FB17B8。public参数为null或receiver检查失败时走异常构造/抛出分支；native transport需保持原始异常传播，不能将请求记录解释成成功停止。

## Finding 与 Path

F-coverage，severity=n/a_re，category=design，confidence=high，status=source-reviewed。Evidence E-image/E-script/E-abi：public两种直接使用与internal相同缓存，未call internal RVA。因此有限入口观察应包含六声明，上下层不应相加视作六个独立逻辑取消。字符串只保留原始pointer与requestUnproven；本研究没有读取字符串对象，也未将名字关联Enumerator/Coroutine。

P-request，path_type=callflow：exact receiver/方法/参数/void/RVA验证→kind0/1 Enter保存请求scalar和既有责任→typed original真实一次→包装检查/resolve/cache→引擎间接尾跳→原始返回或异常→Exit只标请求完成事实。该路径前后的机器码已静态读取；真实native Operation传输与自有Dobby安装由另fixture验证，**不自动覆盖固定游戏首写或引擎内部行为**。pending请求须继续保留原有roots/owner责任，不能因Stop返回、Dispose、暂时不再MoveNext或原异常而宣称CancellationCompleted。

未知仍包括实际metadata匹配、固定游戏六Stop入口安全首次发布/并发首写、引擎scheduler登记移除、在途排空、future生产者封口、所有inlined/direct/icall调用方、跨owner/handle复用、真实GC/root失败和完整异常Record/Context逐级透明性。不能把本文件作为取消完成、根退休或B01–B19完成凭据。

## 实际命令与复现

先执行`rg -n 'StopCoroutine|StopAllCoroutines'`定位dump，再用PowerShell here-string经stdin交Python读取PE/metadata，未生成或执行game地址。Python3.14、pefile2024.8.26、Capstone5.0.6；精确Python版本/路径记录在JSON.tools。下列为本轮实际执行计算的有限核心（artifact生成同样使用这些完整RF范围）：

```powershell
@'
import hashlib,json,pefile,capstone,pathlib
p=pathlib.Path(r'D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll')
pe=pefile.PE(data=p.read_bytes())
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
s=json.loads(pathlib.Path(r'C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/script.json').read_text(encoding='utf-8-sig'))['ScriptMethod']
print(hashlib.sha256(p.read_bytes()).hexdigest())
for rva in [0x153d6c0,0x153d700,0x153d750,0x153d7a0,0x153d7f0,0x153d900]:
    rf=next(x.struct for x in pe.DIRECTORY_ENTRY_EXCEPTION if x.struct.BeginAddress<=rva<x.struct.EndAddress)
    raw=pe.get_data(rva,rf.EndAddress-rva)
    print(hex(rva),hex(rf.EndAddress),hex(rf.UnwindData),hashlib.sha256(raw).hexdigest())
    print([x for x in s if x['Address']==rva])
    for i in md.disasm(raw,rva):print(hex(i.address),i.bytes.hex(),i.mnemonic,i.op_str)
'@ | python -
```

本轮只新增本research与JSON，以及独立自有OperationDobby fixture/runner；未改既有冻结源码或历史报告。fixture未由本agent构建或执行，本文不写其通过计数。
