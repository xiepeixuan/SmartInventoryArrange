# Third-party notices / 第三方依赖说明

本项目的 MIT 仅覆盖 [LICENSE_SCOPE.md](LICENSE_SCOPE.md) 列明的原创部分，不替换下面各组件的许可。

## 外部构建/运行依赖

| 组件 | 本机核实版本 | 上游许可 | 本仓库分发状态 |
| --- | --- | --- | --- |
| MelonLoader | 0.7.3 | Apache-2.0 | 外部引用，不跟踪其 DLL |
| 0Harmony / HarmonyX | 程序集 2.10.2.0 | MIT，保留 BepInEx/相关作者通知 | 外部引用，不跟踪其 DLL |
| Il2CppInterop.Runtime | 程序集 1.5.1.0 | LGPL-3.0；以附带的 v1.5.1 上游原文为准 | 外部引用，不跟踪其 DLL |
| .NET | 目标 net6.0 | 运行时及各组件使用自身许可 | 用户安装的运行时，不随本 Mod 源码打包 |
| Unity、Assembly-CSharp、Il2Cppmscorlib、TextMeshPro 等 | 游戏 0.46D / Unity 2021.3.45f2 对应程序集 | 原权利人条款，不属于本项目 MIT | 构建时从用户本机游戏安装引用 |

项目 `SmartInventoryArrange.csproj` 使用 `Private=false` 外部引用，当前 Git 跟踪内容未包含上述 DLL。引用接口不是取得第三方实现的版权或把其许可证转换成 MIT。

已保留对应版本的原文：

- [MelonLoader v0.7.3](third_party/licenses/MelonLoader-v0.7.3-LICENSE.txt) · [上游](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/LICENSE.md)
- [HarmonyX v2.10.2](third_party/licenses/HarmonyX-v2.10.2-LICENSE.txt) · [上游](https://github.com/BepInEx/HarmonyX/blob/v2.10.2/LICENSE)
- [Il2CppInterop v1.5.1（含 LGPL/GPL 正文）](third_party/licenses/Il2CppInterop-v1.5.1-LICENSE.txt) · [上游](https://github.com/BepInEx/Il2CppInterop/blob/v1.5.1/LICENSE)

复制、修改、链接或再次打包依赖时，仍应逐项履行对应通知、源码/重新链接等适用义务；本说明不宣称“未捆绑 DLL 就没有其他许可义务”。上游许可证文本的版权属于其权利人，不是本项目 MIT 的原创内容。

## 游戏素材与图片

`assets/**` 包含原生贴图参与生成的历史对比图。游戏素材归原权利人；现有记录尚未提供游戏方对这些导出素材公开再分发的明确授权。归属标注、Mod 接口可用或本项目采用 MIT 都不补足这项授权。

资源库也保留该待核实事项。后续可取得明确许可，或改为由用户本地导出素材、仓库只发布原创工具。此次未删除素材、改动资源仓库可见性或改写历史发布包。
