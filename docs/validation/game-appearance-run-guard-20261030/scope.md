# GameAppearance 本轮验证范围

- auth：用户授权恢复 GameAppearance，实现功能与受控测试；实际游戏安装、启动及 UI 验收留待后续。
- in_scope：`src/RizToolbox.Adapter/GameAppearance/`、`src/RizToolbox.Sdk/GameAppearanceContracts.cs`、`tests/RizToolbox.GameAppearance.Tests/`、`src/RizToolbox.Runtime/GameAppearanceTestAssemblyInfo.cs` 以及本目录文档。
- network_profile：离线源码、已有静态反汇编记录、当前进程自有 unmanaged 缓冲区与 IL2CPP export delegates；本轮未加载游戏 DLL。
- 集成边界：Adapter 模块注册、SDK capability 元数据、宿主生命周期 matcher、共享语义 target/mutation、解决方案和总测试脚本由 root 集成。
- 本轮验收：GameAppearance 项目成功构建；service、NativeRuntime accessor 和 NativeGraph 受控用例通过；按 owned 文件生成只读交付快照及 SHA256 清单。
- 后续独立验收：真实游戏内轨道首尾色与 note SpriteRenderer 颜色的可见效果、暂停中修改、外部重写与退出恢复。本报告不声称这些实机验收已通过。
