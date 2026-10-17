# SmartInventoryArrange · 智能排列

为 **Probably Stolen Demo** 提供确定性、视觉优先的库存整理。

**当前版本：v3.17.2（预发布）** · [下载发布包](https://github.com/xiepeixuan/SmartInventoryArrange/releases/tag/v3.17.2) · [验证记录](docs/VALIDATION.md)

## v3.13 → v3.17.2 更新说明

1. 改善排列不充分的问题：对全部可移动物品进行全局重排，减少原布局限制。
2. 修复取出部分物品后，再次智能排列不生效的问题。
3. 修复连续点击直接提示“已是最佳排列”并跳过的问题，现在会继续搜索更好的布局。

## 排列思路

- 同物品优先相邻成排，必要时使用少量相邻换行；容器、机器及大件优先靠上靠左。
- 使用游戏真实占格与合法旋转，支持不规则物品拼合、固定障碍和不同尺寸库存。
- 从固定/禁用格约束出发，对可移动物品生成全新完整候选；原布局用于失败保全，不作为视觉评分否决新方案。
- 再次点击保留搜索迭代器和前沿；没有更好候选时画面可以不变，但日志会显示新增工作。物品、状态或外部位置变化后重建搜索。
- 首批最多25万DFS节点/64个原始解，后续每批2.5万/64；种子生成和评分不计入DFS节点。最多4096唯一候选，保留最佳256个；不是400ms硬截止或数学全局最优保证。

## 环境与安装

Probably Stolen Demo **0.46D** · Unity **2021.3.45f2 / IL2CPP** · MelonLoader **0.7.3 x64** · Windows / .NET 6。

1. 退出游戏，从[发布页](https://github.com/xiepeixuan/SmartInventoryArrange/releases/tag/v3.17.2)下载ZIP。
2. 将当前DLL备份到Mods以外；只复制新的 `SmartInventoryArrange.dll` 至 `<游戏目录>/Mods`。
3. 启动后在日志确认加载 `v3.17.2`。使用DLL不需要Python、离线资源或源码依赖。

发布DLL的SHA256：

```text
AA8D0E9EA378EE3F5AFAC415F3A2887812F564F3898C99E7C0AEB2B4A32EEEBC
```

## 使用与保护

容器右键 → 游戏排序菜单 → **智能排列 / Smart Arrange**。运行中重复请求不会并发启动；完成后再次点击会继续搜索。

捕获完整快照，在游戏主线程检查原生实例、父容器指针、状态、坐标、方向、占格和容器几何；后台只计算托管数据。固定物品不移动，最终必须一一对应、不重叠、不越界。事务应用失败恢复快照，不以创建或销毁物品实现排列。

计算期间请不要移动目标库存物品。快照确实发生变化时取消提交；日志给出stage、物品和具体旧/新字段。通过检查不等于所有原生API故障、存档重载或多Mod兼容性已完成验证。

## 构建与公开测试

安装.NET SDK **6.0.428**。本机需游戏及MelonLoader生成的程序集；仓库不提供这些依赖。

```powershell
dotnet build ./src/SmartInventoryArrange/SmartInventoryArrange.csproj -c Release --nologo -warnaserror -p:GameDir="<游戏目录>"
```

也可通过 `PROBABLY_STOLEN_DIR` 环境变量指定游戏目录。发布DLL沿用已验证、已安装的精确产物；不同路径重编译因调试信息可能产生不同哈希。

以下五组测试直接编译Core及内嵌模型，不需要游戏程序集：

```powershell
dotnet run --project ./tests/SmartInventoryArrange.Tests -c Release
dotnet run --project ./tests/SmartInventoryArrange.NativeSafety -c Release
dotnet run --project ./tests/SmartInventoryArrange.PerformanceSafety -c Release
dotnet run --project ./tests/SmartInventoryArrange.SnapshotSafety -c Release
dotnet run --project ./tests/SmartInventoryArrange.SessionSafety -c Release
```

构建DLL后，可用纯.NET元数据检查中英文按钮常量，不加载Unity：

```powershell
dotnet run --project ./tests/SmartInventoryArrange.UiText -c Release -- ./src/SmartInventoryArrange/bin/Release/net6.0/SmartInventoryArrange.dll
```

[完整验证范围、测试计数和局限](docs/VALIDATION.md)。私人日志、玩家库存、盲选标注、旧DLL备份和游戏依赖不随源码发布。

## 历史真实日志对比

下面图片保留自 **v3.13**，不是v3.17.2效果图；由日志坐标、原生图标和图鉴占格重绘，不是游戏截图。两侧来自不同时间，同物品集合不等于已核实同一批实例/状态；左图不宣称游戏原版排序。

![24×10仓库历史布局与v3.13实际应用](assets/comparisons/warehouse-24x10.png)
![10×10医疗库存历史布局与v3.13实际应用](assets/comparisons/medical-10x10.png)

[三组历史案例与配对说明](docs/COMPARISONS.md) · [v3.13历史性能记录](docs/VALIDATION-v3.13.md)

## 日志、回滚及限制

日志位于 `<游戏目录>/MelonLoader/Logs`，物品坐标在 `SmartInventoryArrange-Snapshot.log`，原生占格输入在 `SmartInventoryArrange-Inputs`。反馈时附尺寸、占用率、截图和相关日志，公开前检查物品等私人内容。

卸载/回滚：退出游戏后移走本DLL，或恢复安装前的精确备份。仓库安装脚本记录备份，可用对应Uninstall脚本/ROLLBACK.sh恢复；不要混用研发隔离基线与真实安装前版本。

有限确定性搜索不保证任意高密度异形库存都更美观。最新版已完成构建、离线测试、安装校验和隔离回滚，**仍待最新游戏内菜单、排列、存档重载与多Mod兼容性确认**，因此保留预发布标记。旧版截图和旧性能数据不能证明新版游戏效果或提速。
