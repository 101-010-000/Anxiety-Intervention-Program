# 手机道具导入：Sketchfab 低模手机（搜索→许可→转换→描边预置体）

## Goal

为剧情场景补一个低边卡通风格的手机静态道具（可摆桌面/手中的通用 prop），风格与现有角色/道具一致（URP + Outline 描边）。

## Requirements

- 风格：低边卡通、浅色调，贴合项目治愈系淡蓝调。
- 许可：可商用；用户在候选清单中点名后才下载（asset-search 下载门禁）。
- 工程：Unity 2022.3 原生可读（项目无 glTFast / Blender）；不改包依赖。
- 与其他道具同款处理：整棵 Outline 层吃全局反向外壳描边；URP Lit 材质。

## Acceptance Criteria

- [x] 用户从 8 个候选中选定 `sketchfab:4ff99cf17b164e7b9790638c5d2ef4ce`（kimmy.k「Low Poly Mobile Phone」，CC-BY 4.0）。
- [x] 详情 API 许可证据核验并存档（`Downloads/asset-search/20260928-phone-lowpoly/licenses/`）；GLB 下载 + SHA-256 记录进 manifest。
- [x] GLB → 4 个单材质 OBJ（`额外文件/工具脚本/glb2obj_phone.py`）：烘焙节点变换、修源模型 45° 斜置、归一化到 0.081×0.016×0.155 m 屏幕朝上落地。
- [x] `Assets/Editor/PhonePropSetup.cs`（幂等）：URP Lit 材质×4、预置体 `手机_淡蓝.prefab`、整棵 Outline 层、预览图 + 报告自检。
- [x] 首跑自检：空材质 0 ✓、网格 4/4 ✓；用户已在场景中摆放使用（"可以了"）。

## Notes（收尾时修的 3 个问题，2026-09-28）

1. 预制体根节点漏挂 Outline 层 → 已补（自检 0 ✗）。
2. 材质/预制体用 DeleteAsset 重建会换 GUID、断场景实例引用 → 改为 CreateAsset / SaveAsPrefabAsset 同路径原地覆盖（GUID 不变）。
3. ★ 编辑器 `cam.Render()` 的剔除是全局按层、不分场景 → 首版预览把打开着的 Game 场景（角色脚/地板）拍进图。
   学 CharPreview 用隔离层 31 只画道具本体。此坑值得沉淀进 AGENTS.md。

## 遗留

- 触发器 `_phoneprop_trigger.txt` 已再丢：用户下次刷新 Unity 会自动重跑 ①（重渲染干净预览图 + 根节点层级修正），无需其他操作。
- 许可义务：CC-BY 4.0 —— 发布/商用需在致谢页署名 kimmy.k。
- 源文件追溯：GLB + 许可证据在 `Downloads/asset-search/20260928-phone-lowpoly/`（验收后可删）。
