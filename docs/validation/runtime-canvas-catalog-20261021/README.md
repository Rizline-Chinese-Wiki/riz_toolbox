# 787包：真实菜单与闭泛型元数据

实际安装包为 `../foundation-canvas-catalog-final-20261021/`；787/787、25套回归、0编译警告/错误、18项安装hash匹配、268源码输入当时未变。游戏session `20261021T094900103Z-5d3bca7a402a4aec8cefcda43ebf42a7`，启动PID37988。不能把之后的FakeMainMenu源码修复归入此包。

Computer Use在真实MainMenu中观察到LOCAL窗口，完成以下点击与日志核对：

1. 两首同标题、不同LocalId的原创验证歌曲显示；选择song0为generation1，收藏song0。
2. 选择song1为generation2；显示其独立ID和revision2，song0仍有收藏标记。
3. Play收到OperationUnsupported，游戏保持菜单，不替换原曲。
4. Favorites only显示1首song0；保存文件的lastSong为song1、favorites仅song0。
5. 点击原菜单进入LevelList，窗口消失，generation2以scene-change释放。
6. 返回实际场景名为FakeMainMenu；旧白名单未覆盖，因此窗口没有重建。这是失败证据，后续修复在 `../return-menu-scene-20261021/`，本会话不宣称恢复通过。

只读metadata marker还取得完整13行报告：InsideLevelParams的Sprite/AudioClip闭interface与5参数ctor，以及Sprite闭ResourceReference类的ctor/get/Release均由真实元数据核验。Sprite ctor RVA `AEF2C0`、Release `AEF1F0`，其declaring class、六项MethodInfo导出交叉检查与GameAssembly可执行区域成立。两个interface的占位入口不能代表concrete AudioClip；`audio.concrete`和`interface.dispatch`明确unknown。没有创建wrapper、Invoke资源方法或启动LOCAL关卡。

用户实体Escape停止Computer Use。`escape-stopped-partial-20261021T095412Z/`冻结了当时全部935条连续记录、0Error；游戏当时仍运行，属于部分快照，不能证明正常退出和最终清理。`runtime-proof.json`由`analyze-snapshot.py`逐条解析该快照生成，保存13行原始metadata、选择/保存/拒绝/释放序号及输入hash；0Error也不能否认FakeMainMenu功能失败。

临时配置、metadata marker、偏好和3原创资产在`probe-files.json`中有启动前记录。游戏运行时保持这些文件，退出后才运行`probe-files.py restore`并核对恢复记录；上一轮final-probes-restoration不适用于本会话。
