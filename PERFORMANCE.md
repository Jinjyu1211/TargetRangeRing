# 性能检查说明

## 版本基线

最初基于 main / cc2cc30（仓库源码为 0.4.0，发布清单为 0.6.2）进行优化。随后反编译本机已安装的 TargetRangeRing.dll，确认插件及程序集版本为 0.6.2；并在 My Plugin/TargetRangeRing 找到对应完整源码，补回玩家判定点、显示条件、配置界面及玩家实际命中框计算，同时保留此前性能优化。本次以 0.6.3 发布，程序集版本为 0.6.3.0，配置版本保持 12；同步源码、发布清单、下载链接和安装包 SHA-256。

参考 DLL：`C:/Users/ROG/AppData/Roaming/XIVLauncherCN/pluginConfigs/PromeRotation/Plugins/TargetRangeRing/TargetRangeRing.dll`。

参考源码：`C:/Users/ROG/AppData/Roaming/XIVLauncherCN/pluginConfigs/PromeRotation/Plugins/My Plugin/TargetRangeRing`。

反编译副本位于本地 `bin/reference-source`（忽略提交）。这些参考文件均未被修改。

## 已完成优化

- 普通绘图缓存 CircleGeometry 和 DrawStyle，仅位置、半径、颜色、线宽或后端改变时调用 Add；静止且配置不变时不创建这些对象。
- 绘图生命周期改为持久注册加显式清理，地图切换与战斗结束后缓存失效、自动恢复。卸载解除事件订阅。
- 绘制和 UI 共用同一个配置实例，移除每帧 GetPluginConfig，也修复首次没有保存配置时不绘制的问题。
- 禁用时提前返回；没有注册绘图时不重复 Remove。目标圈和玩家判定点分别清理，只使用本功能的四个固定 ID。
- VFX 按量化后的半径比缓存路径，避免每帧数字格式化、Replace 和字符串拼接。嵌入模板只读取一次。
- 缓存 VFX 上次成功应用的颜色与变换；不变时跳过原生调用和矩阵写入。
- 移除 SetMatrix 每帧日志及其参数数组、装箱开销。
- 修复 VFX 颜色修改不生效、创建失败后同路径不再重试的问题。
- VFX 初始化失败每 5 秒重试，避免每帧扫描特征码及记录错误。
- UI 后端选项数组静态复用；增加非有限目标半径检查。

## 玩家判定点恢复

- 默认启用，半径 0.5 米、蓝色实心圆，固定 ImGui 渲染，与目标圈后端独立。
- 不依赖目标或近战职业；半径和颜色可在“玩家判定圈”选项卡调整。显示半径只影响标记，不改变游戏实际命中框或目标圈范围计算。
- 共享总开关、战斗/副本显示条件；两项同时勾选时需同时满足。强制显示只绕过这两项条件，不绕过总开关和玩家判定点开关。
- 玩家静止且配置不变时复用绘图；移动时保留实时跟随并复用样式。
- 目标丢失、职业变化及目标后端切换不清理玩家判定点；禁用、玩家对象消失、非法大小、卸载时清理。
- 修复原 DLL 卸载时未删除玩家判定点的问题。地图切换及战斗结束后会恢复玩家判定点。

## 仍存在的限制

已检查本机 PromeRotation 1.5.10.4 API：CircleGeometry 是 sealed 且位置/半径只读。DrawManager.Update 也会创建新的命令及元素；AddBound 的宿主跟随解析仍每帧创建 CircleGeometry。

因此目标移动时普通后端仍需创建几何体及宿主注册记录，样式会复用。要让移动时也零几何体分配，需要修改宿主，提供可变几何体或原地变换接口并检查线程安全。宿主轮廓和渲染快照也可能分配，此仓库无法直接消除。没有限制更新频率，以保持实时跟随。

缓存恢复覆盖宿主的地图切换及战斗结束事件。如果其他插件主动 Clear/Remove 删除本功能 ID，而目标和配置始终不变，还需要宿主提供存在性查询或清理通知。

VFX 特征码、硬编码偏移及原生资源生命周期仍需对应游戏版本验证。保留原有资源注册上限及强制不透明行为，没有添加未知的原生资源卸载逻辑。

## 验证与游戏内回归

使用 .NET SDK 10.0.101、已安装的 Dalamud 依赖及 PromeRotation 1.5.10.4 编译：

```powershell
dotnet build TargetRangeRing.csproj -c Release -p:OutputPath=bin/Release/
dotnet run --project tests/DrawerChecks.csproj -c Release
git diff --check
```

输出定向到仓库，避免项目默认输出目录覆盖已安装插件。Release 编译通过，0 警告、0 错误。23 项隔离行为检查通过，使用真实 Config / TargetRingDrawer 源码与模拟宿主服务，覆盖玩家生命周期、目标独立性、样式复用、显示条件及清理恢复；VFX 原生调用未在这些检查中执行。尚未进行游戏内测试和性能采样，未声称 FPS 或 GC 改善百分比。

请分别在 ImGui、DirectX、VFX 下检查：

1. 静止目标与移动目标分别比较 Framework.Update 耗时和分配速率。
2. 目标移动、切换目标及碰撞半径变化时，位置和范围实时更新。
3. 颜色、线宽、自定义距离及开关立即生效，尤其是 VFX 颜色。
4. 切换后端/职业、禁用、取消目标、卸载时清理旧圆环且不影响其他绘图。
5. 战斗结束、切换地图后恢复绘图。
6. VFX 特征失效时重试间隔约 5 秒；正常运行没有 SetMatrix 每帧日志。

7. 无目标、非近战职业、目标切换时玩家判定点保持显示；修改半径/颜色立即生效，总开关和卸载后消失。

构建产物：bin/Release/TargetRangeRing.dll。
