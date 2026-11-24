# 下一段把最小发布器接入早期宿主

本批完成固定组件的独立Prepare/Bind/Commit和真实非NOP桩验证；[上批](../foundation-early-loader-20261025/README.md)完成带固定Dobby的早期宿主。下一段必须把两者真正接通，不能把分开成功的两份证据拼成已完成的加载链。

1. 新建不导入Dobby的OwnedProxy变体，普通静态依赖固定publisher、helper和无CRT recorder。Importer改用本批真实非NOP目标和标记，TLS/C/C++/DllMain中的首次四目标调用走最小桩；所有模块名、标记、导出和实际依赖闭包重新核验。现有39个旧宿主及本批57组件原件保留为历史。
2. 在Proxy真实初始化阶段先全Prepare，之后由精确helper ABI2 Configure全部四项，再Bind、逐项Commit；OS/SEH外部依赖和三个helper查询的初始化时机用实际观测分别核验。新增定长事件必须一次冻结并同步验证器，不能在唯一builder运行中改源。
3. 重做正常冷启、TLS/CRT首调用、Start嵌套、加载后Attach、worker/迟到拒绝、每次API内部边界与实际部分写入/保护责任矩阵。每个故障保存实际目标页字节和保护状态，按同页影响核对；有源写尝试或保护未知的失败阻止Importer继续。原函数桩保留50控制上下文与真实基线异常核对，补实际非易失寄存器调用前后哨兵及需要支持的更多PC。
4. 合并宿主通过后，再建立真实游戏独立合同：系统WinHTTP转发/依赖命名与放置、固定双映像和完整原字节、首次相关执行前的可信时机、不可伪造的producer凭据、CLR只读采用。仅改一个模块名/哈希不能把自有fixture升级为游戏权限；普通运行中的字节patch也不能充当首次发布方案。
5. 接真实coordinator/root/owner/provider和GC，补调度登记/消费/drain、异步future来源、Start内联清理与全部消费者责任，再形成取消完成和资源退休凭据。最后跑冷启动、暂停、正常/失败结束、重试、返回菜单、再次开局与Loader重建完整流程。

本页是剩余实现任务，不表示这些步骤已完成。完整[B01–B19](../../../.docs/foundation-expansion/SPEC.md)继续；EngineCancelled=false、Pending保留、RequestPlay关闭。游戏实测仍按用户“稍后”的安排，不安装本批fixture进游戏。
