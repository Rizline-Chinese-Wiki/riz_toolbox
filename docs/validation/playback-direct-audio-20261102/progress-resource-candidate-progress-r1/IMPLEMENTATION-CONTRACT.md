# Progress resource continuity：隔离实现合同

2026-11-02。基线为主目录当前 T n3 的894个冻结输入／482个产品输入，逐项复制和当前主目录／README保持记录见 `baseline-manifest.json`。原始静态合同与原件保留在 `basis-review/`，不改写其历史决定。

本候选在独立D盘目录开发，当前T SDK18矩阵继续使用自己的不可变源码、DLL和原进程。原审查建议“先等当前矩阵再开实现branch”调整为：**隔离开发可并行，合入主目录和宣称本批验收仍必须等T18当前矩阵实际闭合；新候选每次改动都由自己的fresh源码／helper／fixture验证。** 不用旧包签新源码，不终止或重启原长测。这个顺序消除开发空等，不降低产品和验收合同。

## 必须实际完成的行为

- 新准确角色 ProgressClip=19、20个物理成员和21个metadata声明（含pitch同址别名），精确offset30／泛型引用metadata／GC屏障叶proof；保持既有SourceV4、PlayerWatchV2、ProgressWatchV1 wire，资源凭据单独V1。
- 正常owner线程纯资源setter推进专用resource revision，使旧future-capture失效，保留独立current-audio Clock／合法控制。不同Progress和无关shared-body receiver透明执行；实际related child独立parent0观察。worker／class／SEH真实Unknown仍按原合同撤全组权限。
- ticket只由实际保留Progress watch＋独立ResourceReference root＋native同锁当前字段／revision比对发行。同值／ABA不能恢复旧票；裸pointer、诊断DTO和用户boolean不授权source。
- 实际Start重新读Progress+30并调用真实owned reference.get_Resource，set_clip child收到新AudioClip，实际initializer／source-open同锁再次核资源票，才发行新源和唯一Clock。setter自身不加载资源，不替代Start。
- 资源替换打断yield／未消费candidate后，真实drain并以新Start重捕获。已Begin且尚未source-open的phase6也补精确重试：Source同锁核旧initializer仅因资源事件失效、旧物理责任已退出、新实际initializer和新ticket就绪；SingleReturn永久失效旧opaque receipt，新发record，单调epoch，不重写旧graph或恢复旧authority。
- phase6若已stage旧pending七根watch，保留它直到精确新watch同锁替换，不能先撤唯一Progress身份导致fresh ticket无效；晚到旧retire不得清新watch。
- 资源和其他根只在真实known物理责任退出后释放；Unknown或free结果不明保留责任与原失败证据。

## 工作分配与初验

Root负责Adapter／初始化／SingleReturn coordinator及C#组合检查。三个Sol/max代理分别负责Source native、Runtime metadata／broker／私有桥和新owned fixture／Fake API，不交叉修改所有权文件。主仓及T输入保持不变。

先执行真正的native/metadata/fixed-profile和短组合检查，再补phase6／worker／foreign／SEH与正式SDK正向。任一失败保留原exit/log，修复后另冻结新目录；不将未运行、running或静态相似当功能通过。生产provider、未知lower、完整generation、Seek、effect及全部B01–B19仍不因本合同自动完成。

禁止安装或启动真实游戏、调用其方法／DllMain、操作游戏UI或账号；固定853d映像只做静态文件核对。无commit、push、reset或破坏性清理。
