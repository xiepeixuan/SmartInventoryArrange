# 许可范围与来源审查

审查日期：2026-10-21。项目原创代码采用根目录 [LICENSE](LICENSE) 中的标准 MIT License，署名为 `Copyright (c) 2026 xiepeixuan`。下面界定哪些作品受该许可覆盖，不为第三方作品另加限制或改变其许可证。

## MIT 覆盖范围

- `src/SmartInventoryArrange/**/*.cs`：本项目原创求解器、事务、状态排序和游戏接口适配代码。
- `src/SmartInventoryArrange/Models/directional21.json`：本项目生成的偏好模型配置与权重；不因此发布或许可私人标注、玩家原始日志。
- `tests/**/*.cs`：原创测试逻辑和托管 API 替身；它们不是游戏、Unity、MelonLoader 或 Harmony 的实现代码。
- `scripts/`、`ROLLBACK.sh`、构建项目及配置文件中的原创内容。
- README、`docs/` 中的原创文字、分析与本项目产生的测量结果；其中引用的第三方内容仍归原权利人。

涉及游戏 ItemId、状态标签、API 名称和回放占格的代码，只许可本项目的原创表达，不对游戏名称、标识、原生数据或第三方权利作额外授权。

## MIT 不覆盖的第三方材料

| 材料 | 处理方式 |
| --- | --- |
| `assets/**` 中的游戏贴图、游戏素材构成的渲染图及尚未独立核实来源的视觉素材 | 不纳入本项目 MIT 授权；项目绘图和文字贡献不改变底层素材归属 |
| 游戏和 Unity 原生/生成程序集、游戏代码、其他 Mod 实现 | 不由本项目再许可，也不随源码仓库分发 |
| MelonLoader、Harmony/HarmonyX、Il2CppInterop 及其他第三方依赖 | 保持各自许可证；见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |
| 原始游戏日志、玩家库存、私人偏好标注及未公开训练集 | 本次许可不构成发布、访问或使用授权 |

MIT 不授予 Probably Stolen 或其他产品的商标权、认可或合作关系。公开仓库和标注素材出处，也不等于取得游戏素材的再分发许可。

## 审查依据与边界

1. 已检查当前 Git 跟踪文件、历史提交和本地版本化开发/打包记录；代码修改可追溯至本项目研发。
2. 维护者于本次审查中确认：除本项目研发生成代码外，没有另外合入其他作者源码或代码片段。
3. 未发现当前源文件携带第三方版权头、复制声明或整份反编译源码；项目文件通过外部引用使用游戏及加载器程序集，`Private=false`，不是把依赖实现重新声明为原创。
4. 2026-10-19 的 InventorySorter 材料是外部行为/IL研究证据；本项目据用户要求独立实现容器固定条件，未将该 Mod DLL 或整份反编译实现纳入公开仓库。参考功能与授予外部实现版权是两回事。
5. 审查依据是现有记录和维护者确认，不是对全球代码库的穷尽比对或完整权利链保证。后续引入第三方代码须重新核验来源、保留通知并更新清单。
6. 现有源码树没有跟踪第三方 DLL。测试替身使用兼容命名空间/API形状不表示持有原库版权；`MelonInfo` 中的作者显示文本也不替代版权归属或许可证。

## 为什么选择 MIT

这是面向复用、研究和协作的小型原创 Mod 项目，当前没有要求下游必须公开修改，也没有提出需要 Apache-2.0 显式专利授权条款的目标，因此选用简洁的标准 MIT。许可证正文不追加“仅学习”“禁止商用”等限制。

标准条款允许使用、修改和再分发受许可的原创代码，并要求保留版权与许可通知；它不抹除依赖或素材的独立条件。参考 [OSI MIT](https://opensource.org/license/mit) 与 [GitHub 许可说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。

资源库另有独立 [许可范围](https://github.com/xiepeixuan/SmartInventoryArrange-Resources/blob/main/LICENSE_SCOPE.md)，其中游戏素材与打包运行时不按本项目代码 MIT 处理。
