# 本批范围与授权

继续用户完整兼容底层与B01–B19目标。按“先继续代码，实测稍后”执行；三代理实现native runtime-invoke、固定映像producer和托管采用，root负责修改版Doorstop、构建/安装清单、唯一构建及证据。没有游戏安装、前台操作、游戏进程终止、原游戏文件写入或commit/push。

本批实作固定Doorstop原`il2cpp_init`之前的同步producer，预装UnityEvent父/mid和runtime-invoke三处指令，随后原init一次，post-init CLR仅Attach观察、不再执行这三处首次Dobby。固定映像/哈希/ABI/函数范围/原字节/unwind/near relay严格核对；写前拒绝可保持原启动并跳过框架，写后未知failfast，不自动回滚或重试。

产品合同限定固定受支持游戏正常首次IL2CPP启动：合法UnityEvent实例调用不早于本次原init。尚未实际冷启动验证该合同；不承诺恶意native插件任意RVA跳入的同进程隔离。映射fixture只在隔离EXE用DONT_RESOLVE_DLL_REFERENCES映射实际GameAssembly验证生产发布字节，不执行其DllMain或任何游戏函数，不充当实际启动/游戏验收。

暂存包新增修改版Doorstop/version及许可证/完整对应源码ZIP，共22项；此前游戏仍1021旧18项。完整B01–B19验收不变，仅B03整项/B18a子项勾选，RequestPlay仍关闭，listener/future取消与全部资源退休尚未闭合。
