// 把《ui素材》里的 12 张设计稿（整页稿/控件规范图）切成能直接用的 UI 贴图：
//   · 圆角控件：按圆角半径抠出透明外圈 + 把内部（原稿里画死的内容）压成纯色，得到干净可拉伸的九宫格
//   · 背景稿：整张直接当背景用
// 输出：Assets/assets/05_UI/{背景_Background,按钮_Button,界面_Panel}/稿_*.png + 对照拼图 + 报告
// 用法：菜单 Tools/干预项目/切分 UI 设计稿（MainMenuBuilder 搭场景前会自动跑一次）
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class MainMenuSlices
{
    public const string SRC_DIR  = MainMenuAssets.UI_ROOT + "/设计稿_原图";
    public const string REPORT   = "Assets/assets/_报告/_设计稿切图.txt";
    public const string CONTACT  = "Assets/assets/_报告/预览/主界面/00_设计稿切图_对照.png";

    /// 一条切图规则：Src=原稿，Name=输出贴图名（不带扩展名），
    /// X/Y/W/H=在原稿里的像素框（左上角为原点），Radius=圆角半径（0=按 Pill 算），Keep=保留原稿外圈宽度（内部会被压平）
    class Slice
    {
        public string Src, Name, Dir;
        public int X, Y, W, H, Keep;
        public float Radius;
        public bool Pill;        // 胶囊（半径 = 高度一半）
        public bool Raw;         // 原样输出（背景图）
        public string Note = "";
    }

    static readonly Slice[] TABLE =
    {
        // —— 背景稿（整张直接用）——
        new Slice { Src = "ui-001.png", Name = "稿_背景_主菜单", Dir = MainMenuAssets.DIR_BG, Raw = true,
                    Note = "ui-001 整张浅蓝氛围稿（含装饰元素）" },
        new Slice { Src = "ui-012.png", Name = "稿_背景_插画",   Dir = MainMenuAssets.DIR_BG, Raw = true,
                    Note = "ui-012 插画风，可作备用背景" },

        // —— 按钮四态（ui-008 底部四个 172×62 的按钮：正常/悬停/拖动/禁用）——
        new Slice { Src = "ui-008.png", Name = "稿_标签_1", Dir = MainMenuAssets.DIR_BUTTON, X = 179,  Y = 870, W = 172, H = 62, Pill = true, Keep = 8, Note = "ui-008 底部状态标签（正常）" },
        new Slice { Src = "ui-008.png", Name = "稿_标签_2", Dir = MainMenuAssets.DIR_BUTTON, X = 512,  Y = 870, W = 171, H = 62, Pill = true, Keep = 8, Note = "ui-008 底部状态标签（悬停）" },
        new Slice { Src = "ui-008.png", Name = "稿_标签_3", Dir = MainMenuAssets.DIR_BUTTON, X = 848,  Y = 870, W = 172, H = 62, Pill = true, Keep = 8, Note = "ui-008 底部状态标签（拖动）" },
        new Slice { Src = "ui-008.png", Name = "稿_标签_4", Dir = MainMenuAssets.DIR_BUTTON, X = 1184, Y = 870, W = 169, H = 62, Pill = true, Keep = 8, Note = "ui-008 底部状态标签（禁用）" },

        // —— 列表行 / 次按钮底（ui-009 左列=默认、右列=选中）——
        new Slice { Src = "ui-009.png", Name = "稿_列表_默认", Dir = MainMenuAssets.DIR_BUTTON, X = 73, Y = 182, W = 665, H = 74, Radius = 30, Keep = 12, Note = "ui-009 左列（默认态）" },
        new Slice { Src = "ui-009.png", Name = "稿_列表_选中", Dir = MainMenuAssets.DIR_BUTTON, X = 793, Y = 182, W = 669, H = 77, Radius = 30, Keep = 12, Note = "ui-009 右列（选中态，蓝）" },

        // —— 卡片（ui-007 四张卡）——
        new Slice { Src = "ui-007.png", Name = "稿_卡片_1", Dir = MainMenuAssets.DIR_PANEL, X = 63,  Y = 147, W = 777, H = 254, Radius = 28, Keep = 30, Note = "ui-007 左上卡（浅蓝）" },
        new Slice { Src = "ui-007.png", Name = "稿_卡片_2", Dir = MainMenuAssets.DIR_PANEL, X = 916, Y = 143, W = 786, H = 275, Radius = 28, Keep = 30, Note = "ui-007 右上卡（蓝）" },
        new Slice { Src = "ui-007.png", Name = "稿_卡片_3", Dir = MainMenuAssets.DIR_PANEL, X = 59,  Y = 477, W = 781, H = 279, Radius = 28, Keep = 30, Note = "ui-007 左下卡（淡紫）" },
        new Slice { Src = "ui-007.png", Name = "稿_卡片_4", Dir = MainMenuAssets.DIR_PANEL, X = 916, Y = 481, W = 781, H = 270, Radius = 28, Keep = 30, Note = "ui-007 右下卡（灰）" },

        // —— 面板（弹窗 / 大卡片 / 手机屏）——
        new Slice { Src = "ui-002.png", Name = "稿_面板_弹窗", Dir = MainMenuAssets.DIR_PANEL, X = 25, Y = 69,  W = 1484, H = 859,  Radius = 74,  Keep = 96,  Note = "ui-002 黑底上的大圆角弹窗" },
                new Slice { Src = "ui-003.png", Name = "稿_面板_手机", Dir = MainMenuAssets.DIR_PANEL, X = 2,  Y = 2,   W = 1528, H = 1016, Radius = 140, Keep = 150, Note = "ui-003 手机屏底（给后续手机 UI 留的）" },
    };

    [MenuItem("Tools/干预项目/切分 UI 设计稿")]
    public static void MenuEntry()
    {
        var log = new List<string>();
        log.Add("UI 设计稿切图报告   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        log.Add("");
        Run(log);
        Directory.CreateDirectory(Path.GetDirectoryName(REPORT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(REPORT, string.Join("\n", log.ToArray()));
        Debug.Log("[MainMenuSlices] 切图完成，报告：" + REPORT);
    }

    public static void Run(List<string> log)
    {
        Directory.CreateDirectory(SRC_DIR);
        if (!Directory.Exists(SRC_DIR) || Directory.GetFiles(SRC_DIR, "*.png").Length == 0)
        {
            log.Add("★ 找不到设计稿原图：" + SRC_DIR + "（把《ui素材》里的 12 张 png 复制进去）");
            return;
        }

        log.Add("【设计稿切图】源目录 " + SRC_DIR);
        CleanStale(log);
        var made = new List<Texture2D>();
        var madeNames = new List<string>();
        var srcCache = new Dictionary<string, Texture2D>();

        foreach (var s in TABLE)
        {
            string srcPath = SRC_DIR + "/" + s.Src;
            if (!File.Exists(srcPath)) { log.Add("    ★ 缺原稿 " + s.Src + "（跳过 " + s.Name + "）"); continue; }

            Texture2D src;
            if (!srcCache.TryGetValue(srcPath, out src))
            {
                EnsureReadable(srcPath);
                src = AssetDatabase.LoadAssetAtPath<Texture2D>(srcPath);
                srcCache[srcPath] = src;
            }
            if (src == null) { log.Add("    ★ 读不到原稿 " + s.Src); continue; }

            // 框裁到图内
            int x = Mathf.Clamp(s.X, 0, src.width - 1);
            int y = Mathf.Clamp(s.Y, 0, src.height - 1);
            int w = s.Raw ? src.width : Mathf.Min(s.W, src.width - x);
            int h = s.Raw ? src.height : Mathf.Min(s.H, src.height - y);
            if (s.Raw) { x = 0; y = 0; }
            if (w < 8 || h < 8) { log.Add("    ★ 框太小 " + s.Name); continue; }

            float radius = s.Raw ? 0f : (s.Pill ? h * 0.5f : Mathf.Clamp(s.Radius, 2f, Mathf.Min(w, h) * 0.5f));
            var tex = Crop(src, x, y, w, h, radius, s.Raw ? 0 : s.Keep);
            string outPath = s.Dir + "/" + s.Name + ".png";
            File.WriteAllBytes(outPath, tex.EncodeToPNG());

            float b = radius + (s.Raw ? 0f : Mathf.Max(2f, s.Keep * 0.25f));
            b = Mathf.Min(b, Mathf.Min(w, h) * 0.45f);
            var border = s.Raw ? Vector4.zero : new Vector4(Mathf.Round(b), Mathf.Round(b), Mathf.Round(b), Mathf.Round(b));
            MainMenuAssets.ImportAsSprite(outPath, border, 2048, s.Raw);

            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (loaded != null) madeNames.Add(s.Name);
            made.Add(tex);                        // 内存里的这份可读，留着拼对照图
            log.Add(string.Format("    {0,-16} ← {1}  [{2},{3} {4}×{5}]  圆角{6:0}  九宫格border{7:0}  {8}",
                s.Name, s.Src, x, y, w, h, radius, border.x, s.Note));
        }

        AssetDatabase.Refresh();
        BuildContactSheet(made, madeNames, log);
        foreach (var t in made) UnityEngine.Object.DestroyImmediate(t);
        log.Add("");
        log.Add("说明：圆角控件是从稿子里抠出来的，内部被压平成纯色（原稿里画死的文字/图形不会带进来）；");
        log.Add("      九宫格 border 取「圆角半径 + 一点留白」，所以拉伸时圆角不会变形。");
        log.Add("      对照拼图（每张单独一行，棋盘底看得出透明区域）：" + CONTACT);
    }

    /// 删掉不再是本表产物的 稿_*.png（改过名字/弃用的旧图不留在项目里）
    static void CleanStale(List<string> log)
    {
        var keep = new HashSet<string>();
        foreach (var s in TABLE) keep.Add(s.Dir + "/" + s.Name + ".png");
        var dirs = new[] { MainMenuAssets.DIR_BG, MainMenuAssets.DIR_PANEL, MainMenuAssets.DIR_BUTTON, MainMenuAssets.DIR_ICON };
        foreach (var d in dirs)
        {
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.GetFiles(d, "稿_*.png"))
            {
                string p = f.Replace('\\', '/');
                if (keep.Contains(p)) continue;
                AssetDatabase.DeleteAsset(p);
                log.Add("    清掉旧切图 " + p);
            }
        }
    }

    /// 从原稿裁一块，做圆角 alpha + 内部压平
    static Texture2D Crop(Texture2D src, int x, int y, int w, int h, float radius, int keep)
    {
        var px = src.GetPixels32();          // (0,0) 在左下
        var outPx = new Color32[w * h];
        int sw = src.width, sh = src.height;

        // 内部主色：取四角往内一点的位置的中位数，避免采到原稿背景
        Color32 inner = SampleInner(px, sw, sh, x, y, w, h, radius, keep);

        for (int oy = 0; oy < h; oy++)
        {
            for (int ox = 0; ox < w; ox++)
            {
                int sx = x + ox;
                int sy = sh - 1 - (y + oy);          // 图坐标(y向下) → 纹理坐标(y向上)
                Color32 c = (sx >= 0 && sx < sw && sy >= 0 && sy < sh) ? px[sy * sw + sx] : new Color32(0, 0, 0, 0);

                // 圆角外 → 透明；圆角内、keep 以外 → 压平成内部色
                float a = 1f;
                if (radius > 0.5f)
                {
                    float cx = Mathf.Min(ox, w - 1 - ox);
                    float cy = Mathf.Min(oy, h - 1 - oy);
                    if (cx < radius && cy < radius)
                    {
                        float dx = radius - cx, dy = radius - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                        a = Mathf.Clamp01(0.5f - d);     // 1px 抗锯齿
                    }
                }
                if (keep > 0 && ox > keep && ox < w - 1 - keep && oy > keep && oy < h - 1 - keep)
                    c = inner;

                c.a = (byte)Mathf.RoundToInt(255f * a);
                outPx[oy * w + ox] = c;
            }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(outPx);
        tex.Apply();
        return tex;
    }

    static Color32 SampleInner(Color32[] px, int sw, int sh, int x, int y, int w, int h, float radius, int keep)
    {
        // 在控件横向中线上、纵向 1/6 处各采几个点，取出现次数最多的颜色
        var count = new Dictionary<int, int>();
        int[] xs = { w / 4, w / 2, w * 3 / 4 };
        int[] ys = { Mathf.Max(keep + 4, 6), Mathf.Max(keep + 4, h / 2), h - Mathf.Max(keep + 4, 6) };
        foreach (var oy in ys)
            foreach (var ox in xs)
            {
                int sx = x + Mathf.Clamp(ox, 0, w - 1);
                int sy = sh - 1 - (y + Mathf.Clamp(oy, 0, h - 1));
                if (sx < 0 || sx >= sw || sy < 0 || sy >= sh) continue;
                var c = px[sy * sw + sx];
                int key = (c.r << 16) | (c.g << 8) | c.b;
                count[key] = (count.TryGetValue(key, out var n) ? n : 0) + 1;
            }
        int best = 0, bestN = -1;
        foreach (var kv in count) if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
        return new Color32((byte)((best >> 16) & 255), (byte)((best >> 8) & 255), (byte)(best & 255), 255);
    }

    /// 把切出来的贴图拼成一张对照图（每行一张，棋盘底），方便人眼检查
    static void BuildContactSheet(List<Texture2D> texs, List<string> names, List<string> log)
    {
        if (texs.Count == 0) return;
        int W = 1600, gap = 14, rowH = 180;
        int H = Mathf.Min(4000, 16 + texs.Count * (rowH + gap));
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++)
        {
            int gx = i % W, gy = i / W;
            bool dark = ((gx / 12) + (gy / 12)) % 2 == 0;
            px[i] = dark ? new Color32(64, 70, 82, 255) : new Color32(88, 95, 108, 255);
        }

        int top = 8;
        for (int i = 0; i < texs.Count; i++)
        {
            var t = texs[i];
            var src = t.GetPixels32();
            float scale = Mathf.Min(1560f / t.width, (float)rowH / t.height);
            int dw = Mathf.Max(1, Mathf.RoundToInt(t.width * scale));
            int dh = Mathf.Max(1, Mathf.RoundToInt(t.height * scale));
            int y0 = H - top - dh;
            for (int oy = 0; oy < dh; oy++)
            {
                int sy = Mathf.Clamp(Mathf.RoundToInt((oy + 0.5f) / scale), 0, t.height - 1);
                for (int ox = 0; ox < dw; ox++)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt((ox + 0.5f) / scale), 0, t.width - 1);
                    Color32 c = src[sy * t.width + sx];
                    if (c.a == 0) continue;
                    int gx = 8 + ox, gy = y0 + oy;
                    if (gx < 0 || gx >= W || gy < 0 || gy >= H) continue;
                    int idx = gy * W + gx;
                    float a = c.a / 255f;
                    px[idx] = new Color32(
                        (byte)(c.r * a + px[idx].r * (1 - a)),
                        (byte)(c.g * a + px[idx].g * (1 - a)),
                        (byte)(c.b * a + px[idx].b * (1 - a)), 255);
                }
            }
            top += dh + gap;
        }

        var sheet = new Texture2D(W, H, TextureFormat.RGBA32, false);
        sheet.SetPixels32(px);
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(CONTACT).Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllBytes(CONTACT, sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        log.Add("    对照拼图已生成（" + texs.Count + " 张，每行一张，自上而下：" + string.Join(" / ", names.ToArray()) + "）");
    }

    static void EnsureReadable(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); ti = AssetImporter.GetAtPath(path) as TextureImporter; }
        if (ti == null) return;
        if (!ti.isReadable || ti.textureType != TextureImporterType.Sprite)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.isReadable = true;                  // 切图要读像素
            ti.mipmapEnabled = false;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }
    }
}
