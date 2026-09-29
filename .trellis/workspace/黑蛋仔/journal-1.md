# Journal - 黑蛋仔 (Part 1)

> AI development session journal
> Started: 2026-09-15

---



## Session 1: 陆宣雨退场穿模：via 经由点走位 + 任务收尾归档

**Date**: 2026-09-29
**Task**: 陆宣雨退场穿模：via 经由点走位 + 任务收尾归档
**Branch**: `ugui`

### Summary

排查第2章陆宣雨退场穿桌椅：根因=NpcEntrance 直线 Lerp 直移 transform 不经物理（项目无 NavMesh）。实现 enter/leave 的 via 经由锚点列表（RunPath 分段走+段间平滑转向，缺锚点警告兜底，trellis-check 零问题）；代码已随并行窗口 161d262a 入库。用户跑菜单建出场景路线点并定稿单拐弯方案（双点方案已撤销）。本次收尾提交：路线点落场景 + AGENTS.md/spec 沉淀 + 任务归档。用户遗留：拖路线点到过道后再试玩验证。

### Git Commits

| Hash | Message |
|------|---------|
| `4ce25595` | (see git log) |

### Status

[OK] **Completed**
