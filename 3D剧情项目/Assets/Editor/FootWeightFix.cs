// ============================================================================
// 鞋/袜 踝部权重修复（治「脚踝肿包 / 鞋被拧变形」）
//
// 根因（详见 assets/_报告/_脚部变形_诊断报告.md 与 _腿部蒙皮量化.txt）：
//   CC_Base 骨架的小腿上有 4 根【扭转骨】CC_BaseCalfTwist01/02（Thigh 同理），
//   它们的 localRotation 在动画里【永远不动】（mixamo 没有这些骨），
//   而它们的父级就是小腿 —— 所以蒙皮矩阵和小腿【完全等价】（数学上可证）：
//       M_twist = calfWorld · localRest · localRest⁻¹ · calfBind⁻¹ = M_calf
//   于是压在扭转骨上的权重 = “焊在小腿上”。而鞋/袜的踝口正好被刷了
//   12%~50% 的扭转骨权重 → 踝关节一弯（走/跑时 40°~90°），
//   鞋口跟着小腿、鞋底跟着脚 → 踝口被拧成“肿包”。
//
// 修法：把「小腿侧权重（Calf + CalfTwist01/02）」按高度做一道坡道，
//   踝关节以下 → 全给 Foot（鞋底/鞋头本来就该跟着脚走），
//   踝关节以上 RAMP 米 → 仍然留给小腿（高筒靴/袜筒才不会脱离腿），
//   中间线性过渡 → 剪切被摊开到整个踝口，不再是 5mm 内突变。
//   只动这几根骨的权重，其余权重、每顶点总权重都不变。
//
// 菜单：
//   ① Tools/干预项目/修复：鞋袜踝部权重（生成修复网格）   —— 只生成资产 + 报告
//   ② Tools/干预项目/修复：鞋袜踝部权重（写进角色 prefab） —— 把修复网格挂到 9 个角色
//   ③ Tools/干预项目/修复：还原鞋袜权重（回到原 FBX 网格）
//   报告：Assets/assets/_报告/_脚部权重修复.txt
// ============================================================================
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class FootWeightFix
{
    public const string OUTDIR = "Assets/assets/02_角色_Character/Meshes/修复权重";
    public const string REPORT = "Assets/assets/_报告/_脚部权重修复.txt";
    public const string MAPFILE = OUTDIR + "/_修复映射.txt";
    public const string CHAR_DIR = "Assets/assets/02_角色_Character/角色_URP";

    public const float RAMP = 0.16f;        // 坡道长度（米）：踝下 1cm 起，往上 16cm 线性回到小腿
    public const float RAMP_BELOW = 0.01f;  // 坡道起点在踝下多少

    static readonly string[] CALF_SIDE = { "CC_BaseCalf", "CC_BaseCalfTwist01", "CC_BaseCalfTwist02" };
    static readonly string[] FOOT_BONE = { "CC_BaseFoot" };

    // ================================================================== 菜单
    [MenuItem("Tools/干预项目/修复：鞋袜踝部权重（生成修复网格）", false, 95)]
    public static void Generate()
    {
        var log = new List<string>();
        log.Add("鞋袜踝部权重修复 —— 生成修复网格  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add(string.Format("坡道：踝下 {0:0.00}m 起，往上 {1:0.00}m 线性过渡（踝关节以下全给脚，以上留给小腿）", RAMP_BELOW, RAMP));
        log.Add("");

        Directory.CreateDirectory(OUTDIR);
        var map = new Dictionary<string, string>();          // 修复网格 → 原 FBX
        int made = 0, skip = 0;

        // 只处理「鞋」这一类的 FBX（袜/靴都在这套件里）
        foreach (var fbx in Directory.GetFiles("Assets/assets/02_角色_Character/Meshes/9_鞋_Shoes", "*.fbx"))
        {
            string path = fbx.Replace('\\', '/');
            foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
            {
                if (mesh.name.EndsWith("_fix")) { skip++; continue; }
                var r = RepairMesh(mesh, path, log, out string fixPath);
                if (r == null) continue;
                map[fixPath] = path;
                // 自检：克隆出来的网格各通道还在不在（源网格不可读，只能看非空 + 三角面数）
                int srcTris = TriCount(mesh);
                log.Add(string.Format("        → 修复网格 {0,-28} 顶点 {1} 三角 {2}/{3} 法线 {4} UV {5} 绑定 {6} 材质槽 {7} {8}",
                    Path.GetFileName(fixPath), r.vertexCount, r.triangles.Length / 3, srcTris,
                    r.normals.Length, r.uv.Length, r.bindposes.Length, r.subMeshCount,
                    (r.vertexCount == mesh.vertexCount && r.triangles.Length / 3 == srcTris) ? "✓" : "★ 不一致"));
                made++;
            }
        }
        log.Add("");
        log.Add(string.Format("生成/更新修复网格 {0} 块，跳过 {1} 块（已经是修复版）", made, skip));

        var sb = new System.Text.StringBuilder();
        foreach (var kv in map) sb.AppendLine(kv.Key + "|" + kv.Value);
        File.WriteAllText(MAPFILE, sb.ToString());
        Flush(log);
    }

    public const bool ALSO_FEET = true;      // 裸脚 F_body_feet / M_body_feet 一起修（同属膝下）
    public const bool LR_SYMM = false;       // 中线左右对称化（实测有得有失：裙摆 19→13.6，但短裤 3.1→4.2 变差）→ 默认关
    public const float LR_SYMM_X0 = 0.045f;  // 对称化生效的半宽（米）：|x| 小于它就向 50/50 拉

    // 左右腿骨混绑的区域 → 按 |x| 向“左右均等”拉；外面保持原样（腿本身不受影响）
    static void SymmetrizeLR(float x, List<int> idx, List<float> wt, string[] names, float x0)
    {
        float sL = 0, sR = 0;
        for (int i = 0; i < idx.Count; i++)
        {
            string n = names[idx[i]];
            if (n.StartsWith("CC_BaseThigh")) { }
            else if (n.StartsWith("CC_BaseCalf")) { }
            else if (n.StartsWith("CC_BaseFoot")) { }
            else if (n.StartsWith("CC_BaseToeBase")) { }
            else continue;
            if (n.EndsWith(".L")) sL += wt[i];
            else if (n.EndsWith(".R")) sR += wt[i];
        }
        float tot = sL + sR;
        if (tot < 0.02f || sL < 1e-4f || sR < 1e-4f) return;      // 只处理“真的左右混着绑”的顶点
        float t = Mathf.Clamp01(Mathf.Abs(x) / x0);
        float half = tot * 0.5f;
        float wantL = Mathf.Lerp(half, sL, t);
        float wantR = Mathf.Lerp(half, sR, t);
        for (int i = 0; i < idx.Count; i++)
        {
            string n = names[idx[i]];
            if (n.EndsWith(".L") && sL > 1e-4f) wt[i] *= wantL / sL;
            else if (n.EndsWith(".R") && sR > 1e-4f) wt[i] *= wantR / sR;
        }
    }

    [MenuItem("Tools/干预项目/修复：鞋袜踝部权重（写进角色 prefab）", false, 96)]
    public static void ApplyToPrefabs()
    {
        var log = new List<string>();
        log.Add("把修复网格写进角色 prefab  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("目标：鞋 Shoes_*" + (ALSO_FEET ? " + 裸脚 *_feet" : "") + "（膝下只留 大腿/小腿/脚）");
        log.Add("");

        int n = 0;
        foreach (var f in Directory.GetFiles(CHAR_DIR, "*.prefab"))
        {
            string path = f.Replace('\\', '/');
            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            foreach (var smr in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string nm = smr.name;
                bool isShoe = nm.StartsWith("Shoes_");
                bool isFeet = ALSO_FEET && (nm.StartsWith("F_body_feet") || nm.StartsWith("M_body_feet")
                                            || nm.EndsWith("_feet"));
                // 衣服/裙裤/手也一起治（中线左右对称化 + 扭转骨并掉）：这些才是 stretch 最大的
                bool isCloth = nm.StartsWith("Top_") || nm.StartsWith("Bot_") || nm.StartsWith("Outfit_")
                               || nm.Contains("_body_");
                if (!isShoe && !isFeet && !isCloth) continue;
                if (smr.sharedMesh == null) continue;
                var fix = RepairMesh(smr.sharedMesh, null, log, out string fixPath);
                if (fix == null) { log.Add("  ★ " + contents.name + " / " + nm + "：没能生成修复网格"); continue; }
                smr.sharedMesh = fix;
                dirty = true; n++;
                log.Add("  " + contents.name + " / " + nm + " → " + Path.GetFileName(fixPath));
            }
            if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }
        log.Add("");
        log.Add("共替换 " + n + " 个网格。");
        log.Add("⚠ 之后再跑「重建角色模型」时，CharRebuild 会自动对鞋再套用一次（已接线）；裸脚不在 CharRebuild 里，重建后要重跑一次这个菜单。");
        AssetDatabase.SaveAssets();
        Flush(log);
    }

    [MenuItem("Tools/干预项目/修复：还原鞋袜权重（回到原 FBX 网格）", false, 97)]
    public static void RestorePrefabs()
    {
        var log = new List<string>();
        log.Add("还原角色 prefab 上的鞋网格  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        var map = ReadMap();
        var origByFix = map.ToDictionary(kv => kv.Key, kv => kv.Value);
        int n = 0;
        foreach (var f in Directory.GetFiles(CHAR_DIR, "*.prefab"))
        {
            string path = f.Replace('\\', '/');
            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            foreach (var smr in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.name.StartsWith("Shoes_") || smr.sharedMesh == null) continue;
                if (!smr.sharedMesh.name.EndsWith("_fix")) continue;
                string fixPath = AssetDatabase.GetAssetPath(smr.sharedMesh);
                string srcFbx;
                if (!origByFix.TryGetValue(fixPath, out srcFbx)) continue;
                var orig = AssetDatabase.LoadAllAssetsAtPath(srcFbx).OfType<Mesh>()
                                .FirstOrDefault(m => m.name == smr.sharedMesh.name.Replace("_fix", ""));
                if (orig == null) { log.Add("  ★ 找不到原网格 " + srcFbx); continue; }
                smr.sharedMesh = orig;
                dirty = true; n++;
                log.Add("  " + contents.name + " / " + smr.name + " → " + orig.name);
            }
            if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }
        log.Add("共还原 " + n + " 个鞋网格。");
        AssetDatabase.SaveAssets();
        Flush(log);
    }

    // ================================================================== CharRebuild 用的入口
    // 在 CharRebuild.Attach() 里对鞋网格调用；已经修过的直接返回原样（幂等）。
    public static Mesh FixIfShoe(Mesh src, string label)
    {
        if (src == null) return null;
        if (label != "Shoes") return src;
        if (src.name.EndsWith("_fix")) return src;
        var fix = RepairMesh(src, null, null, out string fixPath);   // 映射表由 RepairMesh 自己维护
        return fix != null ? fix : src;
    }

    // ================================================================== 修复核心
    static Mesh RepairMesh(Mesh src, string srcFbx, List<string> log, out string fixPath)
    {
        fixPath = null;
        if (src == null) return null;

        // 传进来的是「已经修过的网格」→ 先从映射表找回原 FBX 网格，重新从零修一遍
        // （保证改了 RAMP 参数后再跑一次能生效，不会在旧修复上再修一层）
        if (src.name.EndsWith("_fix"))
        {
            string fixAssetPath = AssetDatabase.GetAssetPath(src);
            var map0 = ReadMap();
            string fbx0;
            if (map0.TryGetValue(fixAssetPath, out fbx0))
            {
                var orig0 = AssetDatabase.LoadAllAssetsAtPath(fbx0).OfType<Mesh>()
                                .FirstOrDefault(m => m.name == src.name.Replace("_fix", ""));
                if (orig0 != null) return RepairMesh(orig0, fbx0, log, out fixPath);
            }
            log?.Add("  ★ " + src.name + "：是修复版但映射表里找不到原网格，跳过（可先跑一次『还原』）");
            return null;
        }

        string srcPath = srcFbx ?? AssetDatabase.GetAssetPath(src);
        fixPath = OUTDIR + "/" + src.name + "_fix.asset";

        // 骨名表：优先用 FBX 里那个网格所属的渲染器（拿不到时用 FBX 骨架里的名字）
        var boneNames = BoneNamesOf(src, srcPath);
        if (boneNames == null) { log?.Add("  ★ " + src.name + "：拿不到骨骼名字，跳过"); return null; }

        // 顶点（需要 y 坐标）
        if (!ReadMeshData(src, out Vector3[] verts, out BoneWeight[] bw))
        { log?.Add("  ★ " + src.name + "：读不到顶点/权重，跳过"); return null; }

        // 脚踝高度（网格空间）：bindpose 反算 CC_BaseFoot.L 的绑定位置
        int iFL = FindBone(boneNames, "CC_BaseFoot.L");
        if (iFL < 0) { log?.Add("  ★ " + src.name + "：没有 CC_BaseFoot.L，跳过"); return null; }
        float ankleY = src.bindposes[iFL].inverse.MultiplyPoint3x4(Vector3.zero).y;

        // 脚/小腿/扭转骨索引
        int iFootL = iFL, iFootR = FindBone(boneNames, "CC_BaseFoot.R");
        var calfIdx = new List<int>();
        for (int i = 0; i < boneNames.Length; i++)
            if (IsCalfSide(boneNames[i])) calfIdx.Add(i);

        // 逐顶点重刷
        double moved = 0; int touched = 0;
        var newBw = new BoneWeight[bw.Length];
        for (int v = 0; v < bw.Length; v++)
        {
            var w = bw[v];
            var idx = new List<int>(6); var wt = new List<float>(6);
            int[] bi = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
            float[] ww = { w.weight0, w.weight1, w.weight2, w.weight3 };
            for (int k = 0; k < 4; k++)
            {
                if (ww[k] <= 1e-5f) continue;
                int b = bi[k];
                string bn = boneNames[b];
                if (calfIdx.Contains(b))
                {
                    // 小腿侧（Calf / CalfTwist01 / CalfTwist02）：按高度拆给 脚 / 小腿
                    float u = Mathf.Clamp01((verts[v].y - (ankleY - RAMP_BELOW)) / RAMP);
                    int iFoot = bn.EndsWith(".R") ? iFootR : iFootL;
                    int iCalf = SameSide(boneNames, "CC_BaseCalf", bn);
                    float toFoot = ww[k] * (1f - u);
                    if (iFoot >= 0 && toFoot > 1e-5f)
                    {
                        Push(idx, wt, iFoot, toFoot);
                        moved += toFoot;
                        if (toFoot > ww[k] * 0.05f) touched++;
                    }
                    // 剩下的归【真正的小腿骨】（扭转骨全部并掉 → 膝下只留大腿/小腿/脚）
                    if (ww[k] * u > 1e-5f) Push(idx, wt, iCalf >= 0 ? iCalf : b, ww[k] * u);
                }
                else if (bn.StartsWith("CC_BaseToeBase"))
                {
                    // 脚趾骨一样没人驱动 → 全部并给脚（鞋头不再被它拽）
                    int iFoot = bn.EndsWith(".R") ? iFootR : iFootL;
                    Push(idx, wt, iFoot >= 0 ? iFoot : b, ww[k]);
                    moved += ww[k]; touched++;
                }
                else if (bn.StartsWith("CC_BaseThighTwist"))
                {
                    // 大腿扭转骨同理 → 并给大腿
                    int iTh = SameSide(boneNames, "CC_BaseThigh", bn);
                    Push(idx, wt, iTh >= 0 ? iTh : b, ww[k]);
                }
                else if (bn.StartsWith("CC_BaseUpperarmTwist") || bn.StartsWith("CC_BaseForearmTwist"))
                {
                    // 胳膊上的扭转骨同理（mixamo 从不驱动，等于包在上臂/前臂上）→ 并给它们爸
                    string parent = bn.StartsWith("CC_BaseUpperarm") ? "CC_BaseUpperarm" : "CC_BaseForearm";
                    int ip = SameSide(boneNames, parent, bn);
                    Push(idx, wt, ip >= 0 ? ip : b, ww[k]);
                }
                else Push(idx, wt, b, ww[k]);
            }

            // 中线左右对称化：左右腿骨混绑的区域，在中线附近强制左右均等。
            // 裙子/短裤/裤子的裆部中线原本 12mm 内左右权重跳 145% → 腿一分开就是十几厘米的撕裂。
            if (LR_SYMM) SymmetrizeLR(verts[v].x, idx, wt, boneNames, LR_SYMM_X0);
            var ord = Enumerable.Range(0, idx.Count).OrderByDescending(i => wt[i]).Take(4).ToList();
            float s = ord.Sum(i => wt[i]);
            var res = new BoneWeight();
            if (s > 0)
            {
                int[] oi = { 0, 0, 0, 0 }; float[] ow = { 0, 0, 0, 0 };
                for (int k = 0; k < ord.Count; k++) { oi[k] = idx[ord[k]]; ow[k] = wt[ord[k]] / s; }
                res.boneIndex0 = oi[0]; res.weight0 = ow[0];
                res.boneIndex1 = oi[1]; res.weight1 = ow[1];
                res.boneIndex2 = oi[2]; res.weight2 = ow[2];
                res.boneIndex3 = oi[3]; res.weight3 = ow[3];
            }
            else res = w;
            newBw[v] = res;
        }

        // 写资产（尽量原地更新，保住 GUID 引用）
        Directory.CreateDirectory(OUTDIR);
        var fix = AssetDatabase.LoadAssetAtPath<Mesh>(fixPath);
        if (fix == null || fix.vertexCount != src.vertexCount)
        {
            if (fix != null) AssetDatabase.DeleteAsset(fixPath);
            fix = Object.Instantiate(src);
            fix.name = src.name + "_fix";
            AssetDatabase.CreateAsset(fix, fixPath);
        }
        if (fix.vertexCount != src.vertexCount)
        {
            log?.Add("  ★ " + src.name + "：克隆出来的网格是空的（源网格不可读？），已放弃");
            AssetDatabase.DeleteAsset(fixPath);
            fixPath = null;
            return null;
        }
        fix.boneWeights = newBw;
        EditorUtility.SetDirty(fix);

        // 自检：写回去的权重真的变了吗（换成通用比较：逐骨总权重差，不能只看某一根骨）
        var back = fix.boneWeights;
        double twBefore = 0, twAfter = 0, ftBefore = 0, ftAfter = 0;
        for (int i = 0; i < bw.Length; i++)
        {
            twBefore += Wof(boneNames, bw[i], "CC_BaseCalfTwist02");
            twAfter += Wof(boneNames, back != null && back.Length == bw.Length ? back[i] : bw[i], "CC_BaseCalfTwist02");
            ftBefore += Wof(boneNames, bw[i], "CC_BaseFoot");
            ftAfter += Wof(boneNames, back != null && back.Length == bw.Length ? back[i] : bw[i], "CC_BaseFoot");
        }
        double wDiff = 0;
        if (back != null && back.Length == bw.Length)
        {
            var tot0 = new Dictionary<int, double>(); var tot1 = new Dictionary<int, double>();
            for (int i = 0; i < bw.Length; i++)
            {
                int[] b0 = { bw[i].boneIndex0, bw[i].boneIndex1, bw[i].boneIndex2, bw[i].boneIndex3 };
                float[] w0 = { bw[i].weight0, bw[i].weight1, bw[i].weight2, bw[i].weight3 };
                int[] b1 = { back[i].boneIndex0, back[i].boneIndex1, back[i].boneIndex2, back[i].boneIndex3 };
                float[] w1 = { back[i].weight0, back[i].weight1, back[i].weight2, back[i].weight3 };
                for (int q = 0; q < 4; q++)
                {
                    if (w0[q] > 1e-5f) { double c; tot0.TryGetValue(b0[q], out c); tot0[b0[q]] = c + w0[q]; }
                    if (w1[q] > 1e-5f) { double c; tot1.TryGetValue(b1[q], out c); tot1[b1[q]] = c + w1[q]; }
                }
            }
            var keys = new HashSet<int>(tot0.Keys); foreach (var kk in tot1.Keys) keys.Add(kk);
            foreach (var kk in keys)
            {
                double a0, a1;
                tot0.TryGetValue(kk, out a0); tot1.TryGetValue(kk, out a1);
                wDiff += System.Math.Abs(a1 - a0);
            }
        }
        bool ok = back != null && back.Length == bw.Length && wDiff > 1.0;
        log?.Add(string.Format("        → 权重复查：逐骨总权重变化量 {0:0.0}（{1}）；Twist02 {2:0}→{3:0}，Foot {4:0}→{5:0}",
            wDiff, ok ? "✓ 已写入" : "★ 没写进去（网格不可写？）", twBefore, twAfter, ftBefore, ftAfter));
        if (!ok) { log?.Add("  ★ " + src.name + "：权重写入失败（或本来就无需改），放弃"); return null; }

        // 报告：修复后的这块网格到底还被哪几根骨影响（期望：只有 大腿/小腿/脚）
        if (log != null)
        {
            var used = new List<string>();
            for (int i = 0; i < back.Length; i++)
            {
                int[] bi2 = { back[i].boneIndex0, back[i].boneIndex1, back[i].boneIndex2, back[i].boneIndex3 };
                float[] ww2 = { back[i].weight0, back[i].weight1, back[i].weight2, back[i].weight3 };
                for (int q = 0; q < 4; q++)
                    if (ww2[q] > 1e-4f && bi2[q] >= 0 && bi2[q] < boneNames.Length && !used.Contains(boneNames[bi2[q]]))
                        used.Add(boneNames[bi2[q]]);
            }
            var leg = new List<string>();
            var other = new List<string>();
            foreach (var bn in used)
            {
                if (bn.Contains("Thigh") || bn.Contains("Calf") || bn.Contains("Foot") || bn.Contains("Toe")) leg.Add(bn);
                else if (bn != "RL_BoneRoot" && !bn.StartsWith("CC_Base_Hip") && !bn.Contains("Pelvis")) other.Add(bn);
            }
            leg.Sort(); other.Sort();
            log.Add("        → 修后下肢用到的骨：" + string.Join("、", leg)
                    + (other.Count == 0 ? "   ✓ 只有腿骨" : "   ｜ 还有其它骨：" + string.Join("、", other)));
        }

        // 记映射（修复网格 ↔ 原 FBX），供还原 / 下次重修用
        var mp = ReadMap();
        mp[fixPath] = srcPath;
        WriteMap(mp);

        log?.Add(string.Format("  {0,-34} 顶点 {1,4}  踝高 y={2:0.000}  移动权重合计 {3:0.1}  影响顶点 {4}",
            src.name, src.vertexCount, ankleY, moved, touched));
        return fix;
    }

    // ================================================================== A/B 预览渲染
    const string ABDIR = "Assets/assets/_报告/预览/腿脚/修复AB";

    [MenuItem("Tools/干预项目/修复：渲染踝部权重 A/B 对比图", false, 98)]
    public static void RenderAB()
    {
        var log = new List<string>();
        log.Add("踝部权重修复 A/B 渲染  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("每组同一机位、同一帧（自动选脚踝弯得最狠的一帧）：A_修复前 / B_修复后");
        log.Add("");

        const string PREVIEW = "Assets/Scenes/角色资源预览场景.unity";
        if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("预览"))
        {
            if (!File.Exists(PREVIEW)) { log.Add("★ 找不到预览场景"); Flush(log); return; }
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                PREVIEW, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }
        Directory.CreateDirectory(ABDIR);
        var map = ReadMap();

        var camGO = new GameObject("~fixcam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.14f, 0.16f, 0.20f);
        cam.nearClipPlane = 0.02f;
        // URP 下相机必须有 UniversalAdditionalCameraData，手动 Camera.Render() 才画得出东西
        if (camGO.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
            camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var rt = new RenderTexture(900, 700, 24);

        // 参考：老工具渲出来的图到底有没有内容？
        log.Add("· 旧预览图亮度抽查");
        foreach (var p in new[] { "鞋_1b_绑定姿势_脚特写.png", "鞋_2b_动画中_脚特写.png", "隔离_4_只留鞋.png" })
        {
            string f = "Assets/assets/_报告/预览/腿脚/" + p;
            log.Add("   " + (File.Exists(f) ? p + "  " + Bright(f) : p + "  （不存在）"));
        }
        log.Add("");
        log.Add("· 临时相机自检");
        log.Add("   Camera.allCameras = " + Camera.allCameras.Length + "，渲染管线 = " + UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);

        cam.targetTexture = rt;

        var savedPose = new Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>();
        var savedMesh = new Dictionary<SkinnedMeshRenderer, Mesh>();
        var hidden = new List<GameObject>();
        string abWide = null, abNear = null;

        // 先把所有角色找出来（因为后面会把其它角色 SetActive(false)，再找就找不到了）
        var allRoots = new List<GameObject>();
        foreach (var t in Object.FindObjectsOfType<Transform>())
            if (t != null && t.parent == null && (t.name.EndsWith("_可动") || t.name == "徐夏"))
                allRoots.Add(t.gameObject);

        foreach (var who in new[] { "徐夏", "王含_可动", "林溪_可动" })
        {
            GameObject root = allRoots.FirstOrDefault(g => g.name == who);
            if (root == null) { log.Add("★ 找不到 " + who); continue; }

            Animator an = null;
            foreach (var smr0 in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var a = smr0.GetComponentInParent<Animator>();
                if (a != null && a.isHuman && a.avatar != null && a.runtimeAnimatorController != null) { an = a; break; }
            }
            if (an == null) { log.Add("★ " + who + " 没有驱动 Animator"); continue; }
            var clip = an.runtimeAnimatorController.animationClips.FirstOrDefault();
            if (clip == null) { log.Add("★ " + who + " 没挂动画"); continue; }
            // A/B 一律用同一条「快速跑」当压力测试（角色自己挂待机时看不出区别），
            // 采样只发生在内存里，不会改场景
            var stress = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/assets/03_动作_Animation/预览Clip/快速跑.anim");
            if (stress != null) clip = stress;

            var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.StartsWith("Shoes_"));
            if (smr == null) { log.Add("★ " + who + " 找不到鞋网格"); continue; }
            if (smr.sharedMesh == null)
            {
                // 上一次跑崩了留下 null（没存场景）：按 SMR 名字接回修复网格
                string fp = OUTDIR + "/" + smr.name.Replace("Shoes_", "") + "_fix.asset";
                smr.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(fp);
            }
            if (smr.sharedMesh == null) { log.Add("★ " + who + " 鞋网格为空，跳过（先在 prefab 上重挂一下）"); continue; }

            // 原网格 / 修复网格
            Mesh cur = smr.sharedMesh, orig = null, fix = null;
            string curPath = AssetDatabase.GetAssetPath(cur);
            if (cur.name.EndsWith("_fix")) { fix = cur; orig = OriginalOf(cur, curPath, map); }
            else
            {
                orig = cur;
                string fp = OUTDIR + "/" + cur.name + "_fix.asset";
                fix = AssetDatabase.LoadAssetAtPath<Mesh>(fp);
            }
            if (orig == null || fix == null) { log.Add("★ " + who + "：找不到 原/修 网格（先跑一次『生成修复网格』）"); continue; }
            _lastNames = smr.bones.Select(b => b != null ? Norm(b.name) : "?").ToArray();
            log.Add("   权重对比 原网格：" + WeightSummary(orig) + " ｜ 修复网格：" + WeightSummary(fix));

            // 极端测试网格：小腿侧权重全部归脚（内存里造，不入库）—— 用来确认渲染是否真的跟着权重变
            Mesh allFoot = null;
            {
                var m = Object.Instantiate(orig);
                m.name = orig.name + "_allfoot";
                var names = smr.bones.Select(b => b != null ? Norm(b.name) : "?").ToArray();
                _lastNames = names;
                var arr = m.boneWeights;
                int iFootL = FindBone(names, "CC_BaseFoot.L"), iFootR = FindBone(names, "CC_BaseFoot.R");
                for (int v = 0; v < arr.Length; v++)
                {
                    var w = arr[v];
                    int[] bi = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    float[] ww = { w.weight0, w.weight1, w.weight2, w.weight3 };
                    var idx = new List<int>(); var wt = new List<float>();
                    for (int q = 0; q < 4; q++)
                    {
                        if (ww[q] <= 1e-5f) continue;
                        if (IsCalfSide(names[bi[q]]))
                        {
                            int f = names[bi[q]].EndsWith(".R") ? iFootR : iFootL;
                            Push(idx, wt, f, ww[q]);
                        }
                        else Push(idx, wt, bi[q], ww[q]);
                    }
                    float s = wt.Sum();
                    var res = new BoneWeight();
                    int[] oi = { 0, 0, 0, 0 }; float[] ow = { 0, 0, 0, 0 };
                    for (int q = 0; q < idx.Count && q < 4; q++) { oi[q] = idx[q]; ow[q] = wt[q] / s; }
                    res.boneIndex0 = oi[0]; res.weight0 = ow[0];
                    res.boneIndex1 = oi[1]; res.weight1 = ow[1];
                    res.boneIndex2 = oi[2]; res.weight2 = ow[2];
                    res.boneIndex3 = oi[3]; res.weight3 = ow[3];
                    arr[v] = res;
                }
                m.boneWeights = arr;
                m.UploadMeshData(false);
                allFoot = m;
                log.Add(string.Format("   极端测试网格（小腿侧全归脚）：" + WeightSummary(allFoot)
                        + "  顶点 {0}/{1} 三角 {2} 法线 {3}", m.vertexCount, orig.vertexCount, m.triangles.Length / 3, m.normals.Length));
            }

            // 存姿势 + 藏其它角色
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!savedPose.ContainsKey(t)) savedPose[t] = (t.localPosition, t.localRotation, t.localScale);
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                if (t == null || t.parent != null || t.gameObject == root) continue;
                if (!t.name.EndsWith("_可动") && t.name != "徐夏") continue;
                if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); }
            }

            // 找脚踝弯得最狠的一帧
            float bestT = clip.length * 0.35f, best = 0f;
            AnimationMode.StartAnimationMode();
            for (int i = 1; i < 40; i++)
            {
                float t = clip.length * i / 40f;
                AnimationMode.SampleAnimationClip(an.gameObject, clip, t);
                var lf = an.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                var ft = an.GetBoneTransform(HumanBodyBones.LeftFoot);
                var to = an.GetBoneTransform(HumanBodyBones.LeftToes);
                if (lf == null || ft == null || to == null) break;
                float a = Vector3.Angle((ft.position - lf.position).normalized, (to.position - ft.position).normalized);
                float dev = Mathf.Abs(a - 90f);
                if (dev > best) { best = dev; bestT = t; }
            }
            AnimationMode.StopAnimationMode();
            log.Add(string.Format("{0}／{1}／{2}：取 t={3:0.00}s（脚踝偏离 90° 共 {4:0.0}°）",
                who, clip.name, smr.sharedMesh.name, bestT, best));

            var foot = an.GetBoneTransform(HumanBodyBones.LeftFoot);
            Vector3 look = foot != null ? foot.position : root.transform.position;
            Vector3 sider = (root.transform.right * 0.75f + root.transform.forward * 0.55f).normalized;

            int k = 0;
            foreach (var m in new[] { orig, fix })
            {
                string tag = (m == orig ? "A_修复前" : "B_修复后");
                smr.sharedMesh = null;                 // 两步赋值：确保渲染器真的重新取网格
                smr.sharedMesh = m;
                smr.enabled = false; smr.enabled = true;
                AnimationMode.StartAnimationMode();
                AnimationMode.SampleAnimationClip(an.gameObject, clip, bestT);
                log.Add(string.Format("   [{0}] 实际挂的网格 = {1} (id={2}, 资产={3})", tag, smr.sharedMesh != null ? smr.sharedMesh.name : "null",
                    smr.sharedMesh != null ? smr.sharedMesh.GetInstanceID() : 0,
                    smr.sharedMesh != null ? AssetDatabase.GetAssetPath(smr.sharedMesh) : "-"));
                // ⚠ 机位必须在采样之后算：mixamo 的 clip 带 root motion，
                //    按 rest 姿势的脚位置取景会直接拍到空背景。
                var footNow = an.GetBoneTransform(HumanBodyBones.LeftFoot);
                look = footNow != null ? footNow.position : root.transform.position;
                string baseName = who + "_" + smr.name.Replace("Shoes_", "") + "_" + clip.name + "_" + tag;
                string pWide = ABDIR + "/" + baseName + "_双脚.png";
                string pNear = ABDIR + "/" + baseName + "_左脚特写.png";
                Shot(cam, rt, look, sider, 1.6f, Vector3.up * 0.18f, 40f, pWide);
                Shot(cam, rt, look, sider, 0.60f, Vector3.up * 0.10f, 35f, pNear);
                AnimationMode.StopAnimationMode();
                if (m == orig) { abWide = pWide; abNear = pNear; }
                else
                {
                    log.Add("   双脚图差异： " + DiffDesc(abWide, pWide));
                    log.Add("   特写图差异： " + DiffDesc(abNear, pNear));
                    log.Add("      亮度： A=" + Bright(abNear) + "  B=" + Bright(pNear));
                    // 自检：把相机拉远点拍全身，看相机到底渲得出东西吗
                    string pBody = ABDIR + "/" + who + "_自检_全身.png";
                    var hips = an.GetBoneTransform(HumanBodyBones.Hips);
                    Vector3 bodyLook = (hips != null ? hips.position : root.transform.position) + Vector3.up * 0.15f;
                    Shot(cam, rt, bodyLook, sider, 3.2f, Vector3.up * 0.9f, 45f, pBody);
                    log.Add("   自检全身图： " + Bright(pBody) + "（背景约 42.5，高于它说明相机正常）");
                    // 对照：把鞋藏了再渲一张，确认「渲染真的跟着网格变」
                    string pNone = ABDIR + "/" + who + "_" + smr.name.Replace("Shoes_", "") + "_" + clip.name + "_C_藏掉鞋.png";
                    smr.sharedMesh = null;
                    Shot(cam, rt, look, sider, 0.60f, Vector3.up * 0.10f, 35f, pNone);
                    smr.sharedMesh = m;                    // 马上接回来
                    log.Add("   对照图（藏掉鞋）vs B： " + DiffDesc(pNear, pNone));
                    // 极端测试：小腿侧权重全归脚
                    smr.sharedMesh = allFoot;
                    Shot(cam, rt, look, sider, 0.60f, Vector3.up * 0.10f, 35f, pNone.Replace("_C_藏掉鞋", "_D_全绑脚"));
                    log.Add("   极端图（小腿侧全归脚）vs A： " + DiffDesc(pNear, pNone.Replace("_C_藏掉鞋", "_D_全绑脚"))
                            + "（应该明显不同；如果也是 0.0，那就是渲染没跟权重走）");
                    smr.sharedMesh = m;
                }
                k++;
            }
            savedMesh[smr] = cur;              // 渲染结束后还原成开始的网格
            smr.sharedMesh = cur;

            // 这个角色弄完了：把藏起来的邻居放回去（否则下一个角色 GetComponentInParent 找不到组件）
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            hidden.Clear();
        }

        cam.targetTexture = null;
        Object.DestroyImmediate(camGO);
        Object.DestroyImmediate(rt);
        foreach (var kv in savedMesh) if (kv.Key != null) kv.Key.sharedMesh = kv.Value;
        foreach (var g in hidden) if (g != null) g.SetActive(true);
        foreach (var kv in savedPose) if (kv.Key != null) { kv.Key.localPosition = kv.Value.p; kv.Key.localRotation = kv.Value.r; kv.Key.localScale = kv.Value.s; }

        log.Add("");
        log.Add("图在 " + ABDIR + "（同一机位成对出现，直接对看看踝口就知道了）");
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText("Assets/assets/_报告/_脚部权重修复_AB预览.txt", string.Join("\n", log.ToArray()));
        Debug.Log("[FootWeightFix] A/B 预览报告已写");
    }

    static Mesh OriginalOf(Mesh fix, string fixPath, Dictionary<string, string> map)
    {
        string fbx;
        if (!map.TryGetValue(fixPath, out fbx)) return null;
        string origName = fix.name.Replace("_fix", "");
        return AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().FirstOrDefault(m => m.name == origName);
    }

    // 两张渲染图的差异（客观说明「改动到底看不看得出来」）
    static string DiffDesc(string a, string b)
    {
        try
        {
            var ta = new Texture2D(2, 2); var tb = new Texture2D(2, 2);
            ta.LoadImage(File.ReadAllBytes(a)); tb.LoadImage(File.ReadAllBytes(b));
            if (ta.width != tb.width || ta.height != tb.height) return "尺寸不同";
            var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
            double sum = 0; int big = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                int d = Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
                sum += d / 3.0;
                if (d / 3.0 > 12) big++;
            }
            Object.DestroyImmediate(ta); Object.DestroyImmediate(tb);
            return string.Format("平均每像素差 {0:0.0}/255，明显变化的像素 {1:0.0}%",
                sum / pa.Length, big * 100.0 / pa.Length);
        }
        catch (System.Exception e) { return "比对失败 " + e.Message; }
    }

    static string Bright(string p)
    {
        try
        {
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(p));
            var px = t.GetPixels32();
            double s = 0;
            for (int i = 0; i < px.Length; i++) s += (px[i].r + px[i].g + px[i].b) / 3.0;
            int w = t.width, h = t.height;
            Object.DestroyImmediate(t);
            return string.Format("{0:0.0}/255 ({1}x{2})", s / px.Length, w, h);
        }
        catch (System.Exception e) { return "读不到 " + e.Message; }
    }

    static void Shot(Camera cam, RenderTexture rt, Vector3 look, Vector3 dir, float dist, Vector3 up, float fov, string path)
    {
        cam.fieldOfView = fov;
        cam.transform.position = look + dir * dist + up;
        cam.transform.LookAt(look, Vector3.up);
        cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static int TriCount(Mesh m)
    {
        try
        {
            var md = Mesh.AcquireReadOnlyMeshData(m);
            int n = 0;
            for (int s = 0; s < md[0].subMeshCount; s++) n += md[0].GetSubMesh(s).indexCount;
            md.Dispose();
            return n / 3;
        }
        catch { return -1; }
    }

    // ================================================================== 小工具
    static double Wof(string[] names, BoneWeight w, string key)
    {
        double s = 0;
        int[] bi = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
        float[] ww = { w.weight0, w.weight1, w.weight2, w.weight3 };
        for (int q = 0; q < 4; q++)
        {
            if (ww[q] <= 0 || bi[q] < 0 || bi[q] >= names.Length) continue;
            string n = names[bi[q]];
            if (n == key || n == key + ".L" || n == key + ".R") s += ww[q];
        }
        return s;
    }

    // 一块网格的小腿侧 / 脚侧权重总量（诊断用；源网格不可读就只能靠 boneWeights）
    static string WeightSummary(Mesh m)
    {
        if (m == null) return "无";
        var bw = m.boneWeights;
        if (bw == null || bw.Length == 0) return "读不到权重";
        double tw = 0, calf = 0, foot = 0, toe = 0;
        for (int i = 0; i < bw.Length; i++)
        {
            int[] bi = { bw[i].boneIndex0, bw[i].boneIndex1, bw[i].boneIndex2, bw[i].boneIndex3 };
            float[] ww = { bw[i].weight0, bw[i].weight1, bw[i].weight2, bw[i].weight3 };
            for (int q = 0; q < 4; q++)
            {
                if (ww[q] <= 0 || bi[q] < 0 || bi[q] >= _lastNames.Length) continue;
                string n = _lastNames[bi[q]];
                if (n == "CC_BaseCalfTwist01.L" || n == "CC_BaseCalfTwist01.R" ||
                    n == "CC_BaseCalfTwist02.L" || n == "CC_BaseCalfTwist02.R") tw += ww[q];
                else if (n == "CC_BaseCalf.L" || n == "CC_BaseCalf.R") calf += ww[q];
                else if (n == "CC_BaseFoot.L" || n == "CC_BaseFoot.R") foot += ww[q];
                else if (n == "CC_BaseToeBase.L" || n == "CC_BaseToeBase.R") toe += ww[q];
            }
        }
        return string.Format("扭转骨 {0:0} / 小腿 {1:0} / 脚 {2:0} / 脚趾 {3:0}", tw, calf, foot, toe);
    }

    // 最近一次 WeightSummary 用的骨名表（由调用方在调用前设好）
    public static string[] _lastNames = new string[0];

    static string FixPathOf(Mesh src)
    {
        if (src == null) return null;
        if (src.name.EndsWith("_fix")) return AssetDatabase.GetAssetPath(src);
        string p = OUTDIR + "/" + src.name + "_fix.asset";
        return File.Exists(p) ? p : null;
    }

    static Dictionary<string, string> ReadMap()
    {
        var map = new Dictionary<string, string>();
        if (!File.Exists(MAPFILE)) return map;
        foreach (var line in File.ReadAllLines(MAPFILE))
        {
            var a = line.Split('|');
            if (a.Length == 2) map[a[0]] = a[1];
        }
        return map;
    }

    static void WriteMap(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(OUTDIR);
        var sb = new System.Text.StringBuilder();
        foreach (var kv in map) sb.AppendLine(kv.Key + "|" + kv.Value);
        File.WriteAllText(MAPFILE, sb.ToString());
    }

    static bool IsCalfSide(string n)
    {
        foreach (var k in CALF_SIDE) if (n == k || n == k + ".L" || n == k + ".R") return true;
        return false;
    }

    static int FindBone(string[] names, string key)
    {
        for (int i = 0; i < names.Length; i++) if (names[i] == key) return i;
        return -1;
    }

    // 与 like 同一边的那根 key 骨（like 形如 xxx.L / xxx.R）
    static int SameSide(string[] names, string key, string like)
    {
        string side = like.EndsWith(".L") ? ".L" : (like.EndsWith(".R") ? ".R" : "");
        for (int i = 0; i < names.Length; i++)
            if (names[i] == key + side) return i;
        return -1;
    }

    static void Push(List<int> idx, List<float> wt, int bone, float w)
    {
        for (int i = 0; i < idx.Count; i++) if (idx[i] == bone) { wt[i] += w; return; }
        idx.Add(bone); wt.Add(w);
    }

    // 骨名表：从 FBX 里任意一个 SkinnedMeshRenderer 的 bones[] 取（顺序与网格一致）
    static string[] BoneNamesOf(Mesh mesh, string assetPath)
    {
        foreach (var go in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<GameObject>())
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != mesh || smr.bones == null) continue;
                if (smr.bones.Length != mesh.bindposes.Length) continue;
                return smr.bones.Select(b => b != null ? Norm(b.name) : "?").ToArray();
            }
        }
        return null;
    }

    static string Norm(string n)
    {
        if (n.EndsWith("_L")) return n.Substring(0, n.Length - 2) + ".L";
        if (n.EndsWith("_R")) return n.Substring(0, n.Length - 2) + ".R";
        return n;
    }

    // 角色 FBX 的网格 isReadable=0：顶点走 MeshData 快照，权重直接读
    static bool ReadMeshData(Mesh mesh, out Vector3[] verts, out BoneWeight[] bw)
    {
        verts = null; bw = mesh.boneWeights;
        try
        {
            var md = Mesh.AcquireReadOnlyMeshData(mesh);
            var d = md[0];
            var tmp = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
            d.GetVertices(tmp);
            verts = tmp.ToArray();
            tmp.Dispose();
            md.Dispose();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[FootWeightFix] MeshData 读取失败 " + mesh.name + " : " + e.Message);
            return false;
        }
        return verts != null && verts.Length > 0 && bw != null && bw.Length == verts.Length;
    }

    static void Flush(List<string> log)
    {
        Directory.CreateDirectory("Assets/assets/_报告");
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        AssetDatabase.SaveAssets();
        Debug.Log("[FootWeightFix] 报告已写 " + REPORT);
    }
}

[InitializeOnLoad]
static class FootWeightFixTrigger
{
    const string GEN = "Assets/_footwfix_trigger.txt";
    const string APPLY = "Assets/_footwapply_trigger.txt";
    const string RESTORE = "Assets/_footwrestore_trigger.txt";
    const string AB = "Assets/_footwab_trigger.txt";
    static FootWeightFixTrigger()
    {
        Run(GEN, FootWeightFix.Generate);
        Run(APPLY, FootWeightFix.ApplyToPrefabs);
        Run(RESTORE, FootWeightFix.RestorePrefabs);
        Run(AB, FootWeightFix.RenderAB);
    }
    static void Run(string path, System.Action act)
    {
        if (!File.Exists(path)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                act();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[FootWeightFix] " + e);
                Directory.CreateDirectory("额外文件");
                File.WriteAllText("额外文件/错误_脚部权重修复.txt", e.ToString());
            }
        };
    }
}
