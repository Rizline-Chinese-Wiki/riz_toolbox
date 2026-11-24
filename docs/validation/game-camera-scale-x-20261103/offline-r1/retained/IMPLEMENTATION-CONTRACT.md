# 相机 rig local-scale-x：独立离线增量

当前N2资源连续性及T SDK长测保持原冻结／原进程。N2尚缺的完整generation、未知lower writer和任意Seek依赖未闭合；本增量按N1→N2→N3→N4顺序转至已有固定来源可支持的N3新属性，不重做相机X位置接入、N1、N4或退休批次。

本目录以U的913输入／484托管／48native、foundation-r7已证源码为基线；逐项hash在baseline-manifest.json。U、T及桌面主目录的产品和当前测试输入均不可修改。隔离实现与验证可并行；本增量合主仍须在U与T真实终态闭合、U精确合主之后进行，并只合本增量实际修改的路径，不能覆盖后续已测试的SDK Lifecycle修补。

新增 `GameCameraRoles.RigLocalScaleX`／`local-scale-x`，单位无量纲，实验且影响玩法。它表示当前游戏Camera rig Transform.localScale.x，不宣称uniform zoom。既有SetScale@3E2E00完整608字节及Update消费者证明各轴为静态B向量各分量乘scale，不能假设XYZ相等；get_localScale@1561780／set_localScale@1561E10的完整方法及当前metadata须另外精确核验。

必须满足：

- 复用当前局controller／information／chart／progress／rig／NoteJudgeControl实际Camera及Transform祖先关系的私有根；不扫描任意相机来发权限。
- 位置X与缩放X各有独立角色、属性、identity、physical identity、别名和仲裁通道；不能因同rig共享identity把一个属性覆盖另一个。
- 缩放写入每次真实getter读取Vector3，重验当前图后只替换X，保留当时Y/Z；不改游戏field、不设虚构范围、不用uniform倍率偷换语义。
- 每个候选独立拥有自己的完整图根，任何失败／一属性失效／独立target退休不得提前释放另一属性的根。当前NativeGraph单次转移不可被两个candidate共享；可分别捕获两个图，性能后置。
- GameCameraService按backend声明的准确完整property记录白名单接收，使用该property自身channel key／ExternalWritePolicy；不能继续硬编码local-position-x，也不能信任候选自报任意property授予写入。
- 旧位置X的行为保留；错误metadata／返回值、getter重入时run或rig变化、外来对象／worker／过期scope均在setter前拒绝；拆清生产固定映像与owned fake正向范围。
- 真实NativeRuntime exports正向覆盖非均匀(1,2,3)→(1.5,2,3)、位置与缩放同时、两个独立插件／别名／优先权、外部基线、异常回滚、重建后旧票拒绝／新票成功、各自责任退出／全根最终清理。

不安装／启动真实游戏、不调用游戏方法或DllMain、不操作UI或账号；固定853d映像只静态文件核对。可见效果、每帧Update与render次序、输入投影和真实恢复另待实测；默认原生Speed／Seek及production不开放。无commit、push、reset或破坏性清理。所有失败留原件，修后另freeze；旧包不验新源。

Root负责SDK role、GameCameraService与docs／最终整合；Sol/max代理负责NativeCameraScaleAccessors和NativeGameCameraBackend、对应FakeNative/NativeCameraChecks；另一Sol/max负责固定方法证据及独立合同审查。不同所有者文件不交叉编辑。
