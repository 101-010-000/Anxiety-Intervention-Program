// 主界面 UI 贴图：全部用 SDF 程序化画出来（清透治愈风：淡青绿 + 暖白 + 大圆角 + 细描边）。
// 输出到 Assets/assets/05_UI/…，连同 9 宫格 border 一起写进导入设置，供 MainMenuBuilder 搭场景用。
// 画法说明：每个图形都是一条有符号距离函数(SDF)，Fill 时按距离做 1px 抗锯齿，
// 所以圆角/描边在任何尺寸下都干净，且不依赖任何美术素材。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class MainMenuAssets
{
    // ------------------------------------------------------------------ 路径
    public const string UI_ROOT       = "Assets/assets/05_UI";
    public const string DIR_BG        = UI_ROOT + "/背景_Background";
    public const string DIR_PANEL     = UI_ROOT + "/界面_Panel";
    public const string DIR_BUTTON    = UI_ROOT + "/按钮_Button";
    public const string DIR_ICON      = UI_ROOT + "/图标_Icon";
    public const string DIR_FONT      = UI_ROOT + "/字体_Font";
    public const string DIR_OVERVIEW  = UI_ROOT + "/内容概览";
    public const string STAMP_PATH    = UI_ROOT + "/_生成版本.txt";   // 调色板/清单的指纹，变了就重生成
    public const string DB_PATH       = UI_ROOT + "/内容概览/概览数据.asset";

    // ------------------------------------------------------------------ 配色（清透治愈；已对齐《ui素材》设计稿的淡蓝调）
    public static readonly Color ACCENT       = Hex("4C9FE8");   // 主蓝（取自设计稿的按钮蓝）
    public static readonly Color ACCENT_LIGHT = Hex("7CC0F5");
    public static readonly Color ACCENT_DARK  = Hex("2C6FB5");
    public static readonly Color ACCENT_PALE  = Hex("E4F1FD");
    public static readonly Color INK          = Hex("23364F");   // 深蓝墨色（设计稿文字色）
    public static readonly Color INK_SOFT     = Hex("4E6785");
    public static readonly Color MUTED        = Hex("93AABF");
    public static readonly Color LINE         = Hex("CFE4F5");
    public static readonly Color WARM         = Hex("F0A868");
    public static readonly Color BG_TOP       = Hex("E8F4FE");
    public static readonly Color BG_MID       = Hex("F2F8FE");
    public static readonly Color BG_BOTTOM    = Hex("FBFDFF");
    // 遮罩色：取自 ui-004 的「普通遮罩 / 加深遮罩」
    public static readonly Color MASK_NORMAL  = Hex("2A3F5F");
    public static readonly Color MASK_DEEP    = Hex("16233A");

    public static Color Hex(string s)
    {
        var c = new Color(0, 0, 0, 1);
        ColorUtility.TryParseHtmlString("#" + s, out c);
        return c;
    }

    static Color A(Color c, float a) { c.a = a; return c; }

    // ================================================================== 画布
    public class Img
    {
        public readonly int W, H;
        public readonly Color[] P;

        public Img(int w, int h) { W = w; H = h; P = new Color[w * h]; }

        public void Over(int x, int y, Color c)
        {
            if (c.a <= 0f || x < 0 || y < 0 || x >= W || y >= H) return;
            Color d = P[y * W + x];
            float a = c.a + d.a * (1f - c.a);
            if (a <= 0.0001f) { P[y * W + x] = new Color(0, 0, 0, 0); return; }
            float r = (c.r * c.a + d.r * d.a * (1f - c.a)) / a;
            float g = (c.g * c.a + d.g * d.a * (1f - c.a)) / a;
            float b = (c.b * c.a + d.b * d.a * (1f - c.a)) / a;
            P[y * W + x] = new Color(r, g, b, a);
        }

        public Texture2D ToTexture()
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
            t.SetPixels(P);
            t.Apply();
            return t;
        }
    }

    // ================================================================== SDF 工具
    public delegate float Sd(float x, float y);

    public static Sd Circle(float cx, float cy, float r)
    {
        return (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
    }

    public static Sd Box(float cx, float cy, float hw, float hh, float r)
    {
        return (x, y) =>
        {
            float qx = Mathf.Abs(x - cx) - (hw - r), qy = Mathf.Abs(y - cy) - (hh - r);
            float mx = Mathf.Max(qx, 0f), my = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(mx * mx + my * my) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        };
    }

    public static Sd Seg(float ax, float ay, float bx, float by, float r)
    {
        return (x, y) =>
        {
            float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
            float h = Mathf.Clamp01((pax * bax + pay * bay) / Mathf.Max(0.0001f, bax * bax + bay * bay));
            float dx = pax - bax * h, dy = pay - bay * h;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        };
    }

    public static Sd Ring(float cx, float cy, float r, float w)
    {
        return (x, y) => Mathf.Abs(Circle(cx, cy, r)(x, y)) - w * 0.5f;
    }

    /// 圆弧（a0→a1 逆时针，角度制，0° = +X 方向）
    public static Sd Arc(float cx, float cy, float r, float w, float a0, float a1)
    {
        float span = Mathf.Repeat(a1 - a0, 360f);
        return (x, y) =>
        {
            float ang = Mathf.Repeat(Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg - a0, 360f);
            if (ang > span) return 1e5f;
            return Mathf.Abs(Circle(cx, cy, r)(x, y)) - w * 0.5f;
        };
    }

    /// 凸多边形（用半平面交集近似，做图标够用）
    public static Sd Poly(params float[] pts)
    {
        return (x, y) =>
        {
            float d = float.NegativeInfinity;
            int n = pts.Length / 2;
            for (int i = 0; i < n; i++)
            {
                float ax = pts[i * 2], ay = pts[i * 2 + 1];
                float bx = pts[((i + 1) % n) * 2], by = pts[((i + 1) % n) * 2 + 1];
                float ex = bx - ax, ey = by - ay;
                float len = Mathf.Sqrt(ex * ex + ey * ey);
                float nx = ey / len, ny = -ex / len;
                d = Mathf.Max(d, (x - ax) * nx + (y - ay) * ny);
            }
            return d;
        };
    }

    public static Sd Rot(Sd f, float cx, float cy, float deg)
    {
        float rad = -deg * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        return (x, y) =>
        {
            float dx = x - cx, dy = y - cy;
            return f(cx + dx * c - dy * s, cy + dx * s + dy * c);
        };
    }

    public static Sd U(Sd a, Sd b) { return (x, y) => Mathf.Min(a(x, y), b(x, y)); }
    public static Sd I(Sd a, Sd b) { return (x, y) => Mathf.Max(a(x, y), b(x, y)); }
    public static Sd Sub(Sd a, Sd b) { return (x, y) => Mathf.Max(a(x, y), -b(x, y)); }

    // ================================================================== 绘制
    public static void Fill(Img img, Sd f, Color c, float edge = 1f)
    {
        for (int y = 0; y < img.H; y++)
            for (int x = 0; x < img.W; x++)
            {
                float a = Mathf.Clamp01(0.5f - f(x + 0.5f, y + 0.5f) / Mathf.Max(0.05f, edge));
                if (a > 0f) img.Over(x, y, A(c, c.a * a));
            }
    }

    public static void Stroke(Img img, Sd f, Color c, float w, float edge = 1f)
    {
        Fill(img, (x, y) => Mathf.Abs(f(x, y)) - w * 0.5f, c, edge);
    }

    public static void FillGrad(Img img, Sd f, Func<float, float, Color> colorAt, float edge = 1f)
    {
        for (int y = 0; y < img.H; y++)
            for (int x = 0; x < img.W; x++)
            {
                float a = Mathf.Clamp01(0.5f - f(x + 0.5f, y + 0.5f) / Mathf.Max(0.05f, edge));
                if (a <= 0f) continue;
                Color c = colorAt(x + 0.5f, y + 0.5f);
                img.Over(x, y, A(c, c.a * a));
            }
    }

    /// 虚线描边（空存档位的"空框"）
    public static void StrokeDashed(Img img, Sd f, Color c, float w, float dash = 14f, float gap = 9f)
    {
        for (int y = 0; y < img.H; y++)
            for (int x = 0; x < img.W; x++)
            {
                float d = Mathf.Abs(f(x + 0.5f, y + 0.5f)) - w * 0.5f;
                float a = Mathf.Clamp01(0.5f - d);
                if (a <= 0f) continue;
                float t = Mathf.Repeat((x + y * 0.7f) * 0.7f, dash + gap);
                if (t > dash) a *= Mathf.Clamp01((dash + gap - t) / 3f);
                if (a > 0f) img.Over(x, y, A(c, c.a * a));
            }
    }

    // ================================================================== 贴图清单
    class Spec
    {
        public string Dir, Name;
        public int W, H;
        public Vector4 Border;
        public bool Compress;
        public Action<Img> Draw;
    }

    static Spec S(string dir, string name, int w, int h, Action<Img> draw, float l = 0, float b = 0, float r = 0, float t = 0)    {
        return new Spec { Dir = dir, Name = name, W = w, H = h, Draw = draw, Border = new Vector4(l, b, r, t) };
    }

    /// 调色板 + 贴图数量的指纹：改了调色板，下次跑工具就会重生成全部贴图
    static string Stamp()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in new[] { ACCENT, ACCENT_LIGHT, ACCENT_DARK, ACCENT_PALE, INK, INK_SOFT, MUTED, LINE, WARM, BG_TOP, BG_MID, BG_BOTTOM })
            sb.Append(ColorUtility.ToHtmlStringRGBA(c)).Append('|');
        sb.Append("v3");
        return sb.ToString();
    }

    static Spec Icon(string name, Action<Img> draw)
    {
        return new Spec { Dir = DIR_ICON, Name = "图标_" + name, W = 72, H = 72, Draw = draw };
    }

    // ------------------------------------------------------------------ 形状速写
    static Sd Speaker(float cx, float cy, float s)
    {
        // 喇叭：方形箱体 + 三角喇叭口
        return U(Box(cx - s * 0.48f, cy, s * 0.26f, s * 0.28f, s * 0.09f),
                 Poly(cx - s * 0.22f, cy - s * 0.30f,
                      cx + s * 0.26f, cy - s * 0.72f,
                      cx + s * 0.26f, cy + s * 0.72f,
                      cx - s * 0.22f, cy + s * 0.30f));
    }

    static Sd CheckMark(float cx, float cy, float s)
    {
        return U(Seg(cx - s * 0.40f, cy - s * 0.02f, cx - s * 0.10f, cy - s * 0.32f, s * 0.11f),
                 Seg(cx - s * 0.10f, cy - s * 0.32f, cx + s * 0.42f, cy + s * 0.32f, s * 0.11f));
    }

    static Sd TriangleRight(float cx, float cy, float s)
    {
        return Poly(cx - s * 0.42f, cy - s * 0.55f,
                    cx + s * 0.50f, cy,
                    cx - s * 0.42f, cy + s * 0.55f);
    }

    // ================================================================== 生成全部
    public static void GenerateAll(bool force, List<string> log)
    {
        foreach (var d in new[] { DIR_BG, DIR_PANEL, DIR_BUTTON, DIR_ICON, DIR_FONT, DIR_OVERVIEW })
            Directory.CreateDirectory(d);

        // 调色板或贴图清单变了 → 强制重生（否则旧的配色会一直留着）
        string stamp = Stamp();
        bool stale = true;
        try { stale = !File.Exists(STAMP_PATH) || File.ReadAllText(STAMP_PATH).Trim() != stamp; }
        catch { }
        if (stale && !force) { force = true; log.Add("    调色板/贴图清单有变化 → 重新生成全部 UI 贴图"); }

        var specs = BuildSpecs();
        int made = 0, skipped = 0;
        log.Add("UI 贴图（程序化生成，9 宫格 border 已写进导入设置）");
        foreach (var sp in specs)
        {
            string path = sp.Dir + "/" + sp.Name + ".png";
            if (!force && File.Exists(path)) { skipped++; continue; }

            var img = new Img(sp.W, sp.H);
            sp.Draw(img);
            var tex = img.ToTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            ImportAsSprite(path, sp.Border, Mathf.Max(sp.W, sp.H), sp.Compress);
            made++;
        }
        AssetDatabase.Refresh();
        try { File.WriteAllText(STAMP_PATH, stamp); } catch { }
        log.Add(string.Format("    生成 {0} 张，跳过已存在 {1} 张", made, skipped));
        log.Add("    背景 4 / 面板与卡片 12 / 按钮与控件 18 / 图标 22（清单见下）");
        log.Add("");
    }

    static List<Spec> BuildSpecs()
    {
        var list = new List<Spec>();

        // ---------------------------------------------------------- 背景
        list.Add(S(DIR_BG, "bg_渐变", 1024, 576, img =>
        {
            for (int y = 0; y < img.H; y++)
            {
                float t = (float)y / (img.H - 1);
                Color c = t < 0.5f ? Color.Lerp(BG_BOTTOM, BG_MID, t * 2f) : Color.Lerp(BG_MID, BG_TOP, (t - 0.5f) * 2f);
                for (int x = 0; x < img.W; x++) img.Over(x, y, c);
            }
            // 右上角一团柔光 + 左下角一团暖光
            Fill(img, Circle(img.W * 0.78f, img.H * 0.82f, img.W * 0.34f), A(Color.white, 0.55f), 260f);
            Fill(img, Circle(img.W * 0.14f, img.H * 0.16f, img.W * 0.30f), A(ACCENT_LIGHT, 0.16f), 300f);
            Fill(img, Circle(img.W * 0.30f, img.H * 0.72f, img.W * 0.22f), A(WARM, 0.07f), 260f);
            // 底部一点点回暖
            for (int y = 0; y < img.H * 0.22f; y++)
            {
                float a = (1f - y / (img.H * 0.22f)) * 0.10f;
                for (int x = 0; x < img.W; x++) img.Over(x, y, A(WARM, a * 0.5f));
            }
        }, 0, 0, 0, 0).With(c => c.Compress = true));

        list.Add(S(DIR_BG, "bg_云", 512, 288, img =>
        {
            var cloud = U(U(Circle(150, 150, 74), U(Circle(250, 176, 96), Circle(356, 146, 78))),
                          Box(250, 118, 190, 44, 40));
            Fill(img, cloud, A(Color.white, 0.85f), 26f);
        }));

        list.Add(S(DIR_BG, "bg_光斑", 512, 512, img =>
        {
            Fill(img, Circle(256, 256, 250), A(Color.white, 0.5f), 340f);
        }));

        list.Add(S(DIR_BG, "bg_光点", 128, 128, img =>
        {
            Fill(img, Circle(64, 64, 60), A(Color.white, 0.75f), 80f);
        }));

        // ---------------------------------------------------------- 面板 / 卡片
        list.Add(S(DIR_PANEL, "面板_玻璃", 96, 96, img =>
        {
            var r = Box(48, 48, 44, 44, 20);
            Fill(img, r, A(Color.white, 0.80f));
            Stroke(img, r, A(LINE, 0.95f), 2f);
        }, 24, 24, 24, 24));

        list.Add(S(DIR_PANEL, "面板_亮", 128, 128, img =>
        {
            var r = Box(64, 64, 58, 58, 26);
            Fill(img, (x, y) => r(x, y + 6f) , A(INK, 0.10f), 9f);      // 投影
            Fill(img, r, A(Color.white, 0.96f));
            Stroke(img, r, A(LINE, 1f), 2f);
            Stroke(img, (x, y) => Box(64, 64, 55, 55, 24)(x, y), A(Color.white, 0.9f), 2f);
        }, 20, 20, 20, 20));

        list.Add(S(DIR_PANEL, "面板_暗", 96, 96, img =>
        {
            var r = Box(48, 48, 44, 44, 20);
            Fill(img, r, A(Hex("1F3A41"), 0.72f));
            Stroke(img, r, A(Color.white, 0.16f), 2f);
        }, 24, 24, 24, 24));

        list.Add(S(DIR_PANEL, "面板_标题条", 192, 48, img =>
        {
            var r = Box(96, 24, 92, 20, 18);
            FillGrad(img, r, (x, y) => Color.Lerp(ACCENT_LIGHT, ACCENT, x / 192f));
            Fill(img, (x, y) => Box(96, 24, 88, 14, 14)(x, y), A(Color.white, 0.18f), 10f);
        }, 32, 0, 32, 0));

        list.Add(S(DIR_PANEL, "卡片_普通", 128, 128, img =>
        {
            var r = Box(64, 64, 58, 58, 22);
            Fill(img, r, A(Color.white, 0.62f));
            Stroke(img, r, A(LINE, 0.95f), 2f);
        }, 26, 26, 26, 26));

        list.Add(S(DIR_PANEL, "卡片_悬停", 128, 128, img =>
        {
            var r = Box(64, 64, 58, 58, 22);
            Fill(img, (x, y) => r(x, y + 5f), A(ACCENT, 0.10f), 8f);
            Fill(img, r, A(Color.white, 0.86f));
            Stroke(img, r, A(ACCENT, 0.45f), 2f);
        }, 26, 26, 26, 26));

        list.Add(S(DIR_PANEL, "卡片_选中", 128, 128, img =>
        {
            var r = Box(64, 64, 58, 58, 22);
            Fill(img, (x, y) => r(x, y + 6f), A(ACCENT, 0.16f), 10f);
            Fill(img, r, A(Color.white, 0.97f));
            Stroke(img, r, A(ACCENT, 0.85f), 3f);
        }, 26, 26, 26, 26));

        list.Add(S(DIR_PANEL, "槽位_空", 192, 192, img =>
        {
            var r = Box(96, 96, 90, 90, 20);
            StrokeDashed(img, r, A(MUTED, 0.85f), 3f);
        }, 20, 20, 20, 20));

        list.Add(S(DIR_PANEL, "分隔线", 64, 6, img =>
        {
            Fill(img, Box(32, 3, 31, 1.2f, 1.2f), A(LINE, 1f));
        }, 2, 2, 2, 2));

        // ---------------------------------------------------------- 页签
        list.Add(S(DIR_BUTTON, "页签_普通", 160, 56, img =>
        {
            var r = Box(80, 28, 76, 24, 16);
            Fill(img, r, A(Color.white, 0.55f));
            Stroke(img, Box(80, 26, 76, 20, 14), A(LINE, 1f), 2f);
        }, 14, 14, 14, 14));

        list.Add(S(DIR_BUTTON, "页签_悬停", 160, 56, img =>
        {
            var r = Box(80, 28, 76, 24, 16);
            Fill(img, r, A(Color.white, 0.80f));
            Stroke(img, Box(80, 26, 76, 20, 14), A(ACCENT, 0.45f), 2f);
        }, 14, 14, 14, 14));

        list.Add(S(DIR_BUTTON, "页签_选中", 160, 56, img =>
        {
            var r = Box(80, 28, 76, 24, 16);
            Fill(img, r, A(ACCENT, 0.14f));
            Stroke(img, Box(80, 26, 76, 20, 14), A(ACCENT, 0.9f), 3f);
        }, 14, 14, 14, 14));

        // ---------------------------------------------------------- 主按钮
        list.Add(S(DIR_BUTTON, "按钮_主_普通", 256, 84, img => Primary(img, ACCENT, ACCENT_DARK, false, false), 22, 22, 22, 22));
        list.Add(S(DIR_BUTTON, "按钮_主_悬停", 256, 84, img => Primary(img, ACCENT_LIGHT, ACCENT, true, false), 22, 22, 22, 22));
        list.Add(S(DIR_BUTTON, "按钮_主_按下", 256, 84, img => Primary(img, ACCENT, ACCENT_DARK, false, true), 22, 22, 22, 22));
        list.Add(S(DIR_BUTTON, "按钮_主_禁用", 256, 84, img =>
        {
            var r = Box(128, 42, 122, 36, 30);
            FillGrad(img, r, (x, y) => Color.Lerp(Hex("CBD9D8"), Hex("B8C9C8"), y / 84f));
            Stroke(img, r, A(Color.white, 0.25f), 2f);
        }, 14, 14, 14, 14));

        // ---------------------------------------------------------- 次按钮
        list.Add(S(DIR_BUTTON, "按钮_次_普通", 256, 72, img => Secondary(img, Color.white, ACCENT, 0.80f), 22, 20, 22, 20));
        list.Add(S(DIR_BUTTON, "按钮_次_悬停", 256, 72, img => Secondary(img, Color.white, ACCENT, 0.94f), 22, 20, 22, 20));
        list.Add(S(DIR_BUTTON, "按钮_次_按下", 256, 72, img => Secondary(img, ACCENT_PALE, ACCENT, 1f), 22, 20, 22, 20));
        list.Add(S(DIR_BUTTON, "按钮_次_禁用", 256, 72, img =>
        {
            var r = Box(128, 36, 122, 30, 24);
            Fill(img, r, A(Hex("DCE5E4"), 0.75f));
            Stroke(img, r, A(MUTED, 0.4f), 2f);
        }, 22, 20, 22, 20));

        // ---------------------------------------------------------- 图标按钮
        list.Add(S(DIR_BUTTON, "按钮_图标_普通", 72, 72, img =>
        {
            Fill(img, Circle(36, 36, 32), A(Color.white, 0.72f));
            Stroke(img, Circle(36, 36, 32), A(LINE, 1f), 2f);
        }, 20, 20, 20, 20));
        list.Add(S(DIR_BUTTON, "按钮_图标_悬停", 72, 72, img =>
        {
            Fill(img, Circle(36, 36, 32), A(Color.white, 0.95f));
            Stroke(img, Circle(36, 36, 32), A(ACCENT, 0.6f), 2f);
        }, 20, 20, 20, 20));
        list.Add(S(DIR_BUTTON, "按钮_图标_按下", 72, 72, img =>
        {
            Fill(img, Circle(36, 36, 32), A(ACCENT_PALE, 1f));
            Stroke(img, Circle(36, 36, 32), A(ACCENT, 0.8f), 2f);
        }, 20, 20, 20, 20));

        // ---------------------------------------------------------- 滑条 / 开关
        list.Add(S(DIR_BUTTON, "滑条_轨道", 64, 18, img =>
        {
            var r = Box(32, 9, 31, 6f, 6f);
            Fill(img, r, A(Hex("D5E5E3"), 1f));
            Stroke(img, r, A(INK, 0.06f), 2f);
        }, 8, 4, 8, 4));

        list.Add(S(DIR_BUTTON, "滑条_填充", 64, 18, img =>
        {
            var r = Box(32, 9, 31, 6f, 6f);
            FillGrad(img, r, (x, y) => Color.Lerp(ACCENT_LIGHT, ACCENT, x / 64f));
        }, 8, 4, 8, 4));

        list.Add(S(DIR_BUTTON, "滑条_把手", 44, 44, img =>
        {
            Fill(img, (x, y) => Circle(22, 21, 15)(x, y), A(INK, 0.13f), 8f);
            Fill(img, Circle(22, 22, 15), Color.white);
            Stroke(img, Circle(22, 22, 15), A(ACCENT, 0.9f), 3f);
        }));

        list.Add(S(DIR_BUTTON, "开关_槽_关", 96, 44, img =>
        {
            var r = Box(48, 22, 44, 18, 18);
            Fill(img, r, A(Hex("D3E1E0"), 1f));
            Stroke(img, r, A(INK, 0.08f), 2f);
        }, 20, 20, 20, 20));

        list.Add(S(DIR_BUTTON, "开关_槽_开", 96, 44, img =>
        {
            var r = Box(48, 22, 44, 18, 18);
            FillGrad(img, r, (x, y) => Color.Lerp(ACCENT_LIGHT, ACCENT, x / 96f));
        }, 20, 20, 20, 20));

        list.Add(S(DIR_BUTTON, "开关_把手", 44, 44, img =>
        {
            Fill(img, (x, y) => Circle(22, 20, 16)(x, y), A(INK, 0.16f), 8f);
            Fill(img, Circle(22, 22, 16), Color.white);
            Stroke(img, Circle(22, 22, 16), A(Color.white, 0.9f), 2f);
        }));

        list.Add(S(DIR_BUTTON, "遮罩_白", 8, 8, img =>
        {
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) img.Over(x, y, Color.white);
        }));

        // ---------------------------------------------------------- 角标
        list.Add(S(DIR_ICON, "角标_新", 32, 32, img =>
        {
            Fill(img, Circle(16, 16, 12), Color.white);
            Fill(img, Circle(16, 16, 9), Hex("E8776A"));
        }));

        list.Add(S(DIR_ICON, "Logo_徽标", 256, 256, img =>
        {
            var badge = Box(128, 128, 118, 118, 66);
            Fill(img, (x, y) => badge(x, y + 8f), A(ACCENT_DARK, 0.20f), 22f);
            FillGrad(img, badge, (x, y) => Color.Lerp(ACCENT_LIGHT, ACCENT, y / 256f));
            Stroke(img, (x, y) => Box(128, 128, 112, 112, 62)(x, y), A(Color.white, 0.35f), 3f);
            // 心跳/呼吸线：焦虑干预的意象
            var line = U(U(Seg(74, 132, 100, 132, 8), Seg(100, 132, 118, 170, 8)),
                         U(Seg(118, 170, 140, 92, 8), U(Seg(140, 92, 158, 132, 8), Seg(158, 132, 186, 132, 8))));
            Fill(img, line, Color.white);
        }));

        // ---------------------------------------------------------- 图标
        list.Add(Icon("关闭", img => Fill(img, U(Seg(22, 22, 50, 50, 4.5f), Seg(22, 50, 50, 22, 4.5f)), Color.white)));
        list.Add(Icon("返回", img => Fill(img, U(Seg(24, 36, 50, 36, 4.5f), U(Seg(24, 36, 36, 49, 4.5f), Seg(24, 36, 36, 23, 4.5f))), Color.white)));
        list.Add(Icon("设置", img =>
        {
            Sd gear = Ring(36, 36, 14, 8);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                gear = U(gear, Seg(36 + Mathf.Cos(a) * 13f, 36 + Mathf.Sin(a) * 13f,
                                   36 + Mathf.Cos(a) * 20f, 36 + Mathf.Sin(a) * 20f, 4f));
            }
            Fill(img, Sub(gear, Circle(36, 36, 7)), Color.white);
        }));
        list.Add(Icon("音乐", img => Fill(img, U(Circle(27, 22, 8), U(Seg(33, 24, 33, 52, 3.5f), U(Seg(33, 52, 51, 45, 3.5f), Seg(51, 45, 51, 38, 3.5f)))), Color.white)));
        list.Add(Icon("音量", img => Fill(img, U(Speaker(32, 36, 30), U(Arc(38, 36, 12, 3.5f, -55, 55), Arc(38, 36, 19, 4f, -50, 50))), Color.white)));
        list.Add(Icon("音效", img => Fill(img, U(Speaker(32, 36, 30), U(Arc(38, 36, 13, 3.5f, -50, 50), Circle(54, 36, 3.6f))), Color.white)));
        list.Add(Icon("语音", img => Fill(img, U(Seg(36, 46, 36, 30, 9), U(Arc(36, 30, 13, 3.5f, 200, 340), U(Seg(36, 17, 36, 11, 3f), Seg(27, 11, 45, 11, 3f)))), Color.white)));
        list.Add(Icon("对勾", img => Fill(img, CheckMark(36, 36, 34), Color.white)));
        list.Add(Icon("锁", img => Fill(img, Sub(U(Box(36, 26, 14, 11, 3f), Arc(36, 37, 9, 4.5f, 0, 180)), Circle(36, 27, 3.5f)), Color.white)));
        list.Add(Icon("存档", img => Fill(img, Sub(U(StrokeShape(Box(36, 36, 19, 19, 4f), 3f), U(Box(36, 22, 11, 6f, 1.5f), Box(36, 50, 5.5f, 6f, 1.5f))), Box(36, 36, 21, 21, 4f)), Color.white)));
        list.Add(Icon("章节", img => Fill(img, U(StrokeShape(Box(36, 36, 17, 19, 4f), 3f), Seg(36, 18, 36, 54, 2.5f)), Color.white)));
        list.Add(Icon("概览", img => Fill(img, U(U(Box(28, 44, 8, 8, 2.5f), Box(44, 44, 8, 8, 2.5f)),
                                                      U(Box(28, 28, 8, 8, 2.5f), Box(44, 28, 8, 8, 2.5f))), Color.white)));
        list.Add(Icon("手机", img => Fill(img, U(StrokeShape(Box(36, 37, 13, 20, 4f), 3f), Seg(31, 22, 41, 22, 2.5f)), Color.white)));
        list.Add(Icon("播放", img => Fill(img, TriangleRight(34, 36, 32), Color.white)));
        list.Add(Icon("暂停", img => Fill(img, U(Box(29, 36, 4.5f, 13, 2f), Box(43, 36, 4.5f, 13, 2f)), Color.white)));
        list.Add(Icon("自动", img => Fill(img, U(TriangleRight(24, 36, 26), TriangleRight(46, 36, 26)), Color.white)));
        list.Add(Icon("回退", img => Fill(img, U(TriangleRight(48, 36, 26), Box(22, 36, 2.5f, 12, 2f)), Color.white)));
        list.Add(Icon("放大", img => Fill(img, U(Ring(32, 40, 14, 3.5f), U(Seg(42, 30, 53, 19, 3.5f),
                                                                        U(Seg(25, 40, 39, 40, 3f), Seg(32, 33, 32, 47, 3f)))), Color.white)));
        list.Add(Icon("消息", img => Fill(img, U(StrokeShape(Box(36, 40, 20, 14, 5f), 3f),
                                                     Poly(24, 26, 35, 30, 26, 21)), Color.white)));
        list.Add(Icon("更多", img => Fill(img, U(Circle(22, 36, 4.5f), U(Circle(36, 36, 4.5f), Circle(50, 36, 4.5f))), Color.white)));
        list.Add(Icon("刷新", img => Fill(img, U(Arc(36, 36, 15, 4f, 30, 300), Poly(45, 52, 58, 46, 44, 40)), Color.white)));
        list.Add(Icon("退出", img => Fill(img, U(
            Sub(StrokeShape(Box(29, 36, 13, 19, 3f), 3f), Box(40, 36, 9, 24, 2f)),      // 门（右边开口）
            U(Seg(44, 36, 58, 36, 3.5f), U(Seg(58, 36, 50, 44, 3.5f), Seg(58, 36, 50, 28, 3.5f)))), Color.white)));

        // 手型光标（按钮悬停时用）：白色手掌 + 深色描边，热点在指尖
        list.Add(new Spec
        {
            Dir = DIR_ICON, Name = "光标_手", W = 40, H = 40,
            Draw = img =>
            {
                Sd hand = U(
                    Box(20, 16, 10, 9, 5f),                                              // 手掌
                    U(U(Seg(13, 16, 13, 28, 3.6f), Seg(20, 16, 20, 33, 3.6f)),          // 手指
                      U(Seg(27, 16, 27, 29, 3.6f), Seg(33, 14, 37, 20, 3.6f))));         // 拇指
                Fill(img, (x, y) => hand(x - 1.2f, y + 1.2f), new Color(0.09f, 0.13f, 0.22f, 0.95f));
                Fill(img, hand, Color.white);
            }
        });

        return list;
    }

    static Sd StrokeShape(Sd f, float w) { return (x, y) => Mathf.Abs(f(x, y)) - w * 0.5f; }

    static void Primary(Img img, Color top, Color bottom, bool glow, bool pressed)
    {
        var r = Box(128, 42, 122, 36, 30);
        if (glow) Fill(img, r, A(ACCENT_LIGHT, 0.30f), 14f);
        FillGrad(img, r, (x, y) => Color.Lerp(bottom, top, y / 84f));
        if (!pressed) Fill(img, (x, y) => Box(128, 52, 112, 12, 12)(x, y), A(Color.white, 0.20f), 12f);
        else Fill(img, (x, y) => Box(128, 34, 112, 14, 12)(x, y), A(INK, 0.14f), 12f);
        Stroke(img, r, A(Color.white, 0.35f), 2f);
    }

    static void Secondary(Img img, Color fill, Color border, float alpha)
    {
        var r = Box(128, 36, 122, 30, 24);
        Fill(img, r, A(fill, alpha));
        Stroke(img, r, A(border, alpha > 0.9f ? 0.85f : 0.5f), 2f);
    }

    static Spec With(this Spec s, Action<Spec> f) { f(s); return s; }

    // ================================================================== 导入设置
    public static void ImportAsSprite(string path, Vector4 border, int maxSize, bool compress)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 100f;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled       = false;
        ti.wrapMode            = TextureWrapMode.Clamp;
        ti.filterMode          = FilterMode.Bilinear;
        ti.maxTextureSize      = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(maxSize, 32)), 32, 2048);
        ti.textureCompression  = compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
        // 光标贴图必须可读，否则 Cursor.SetCursor 报错
        ti.isReadable = path.Contains("光标");

        var st = new TextureImporterSettings();
        ti.ReadTextureSettings(st);
        st.spriteBorder   = border;
        st.spriteMeshType = SpriteMeshType.FullRect;   // 九宫格必须整图，不能 Tight
        st.spriteExtrude  = 0;
        ti.SetTextureSettings(st);
        ti.SaveAndReimport();
    }

    public static string PathOf(string name)
    {
        foreach (var d in new[] { DIR_PANEL, DIR_BUTTON, DIR_ICON, DIR_BG, DIR_OVERVIEW })
        {
            string p = d + "/" + name + ".png";
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static Sprite Sprite(string name)
    {
        string p = PathOf(name);
        return p == null ? null : AssetDatabase.LoadAssetAtPath<Sprite>(p);
    }

    /// 优先用《ui素材》设计稿切出来的贴图（稿_ 开头），没有就退回程序化生成的那套
    public static string Pick(string fromArt, string fallback)
    {
        return Sprite(fromArt) != null ? fromArt : fallback;
    }

    // ================================================================== 字体
    /// 中文界面必须有中文字形：项目里没有字体时，从系统字体里拷一个进项目。
    public static Font EnsureFont(List<string> log)
    {
        Directory.CreateDirectory(DIR_FONT);
        var existing = new List<string>();
        foreach (var ext in new[] { "*.ttf", "*.otf", "*.ttc" })
            existing.AddRange(Directory.GetFiles(DIR_FONT, ext, SearchOption.AllDirectories));
        if (existing.Count > 0)
        {
            string p = existing[0].Replace('\\', '/');
            AssetDatabase.ImportAsset(p);
            var f0 = AssetDatabase.LoadAssetAtPath<Font>(p);
            if (f0 != null)
            {
                log.Add("字体：复用项目里的 " + Path.GetFileName(p));
                return f0;
            }
        }

        string[] candidates =
        {
            @"C:/Windows/Fonts/Deng.ttf",     // 等线（清透感最好）
            @"C:/Windows/Fonts/simhei.ttf",   // 黑体
            @"C:/Windows/Fonts/msyh.ttc",     // 微软雅黑
            @"C:/Windows/Fonts/simsun.ttc",   // 宋体
        };
        foreach (var src in candidates)
        {
            if (!File.Exists(src)) continue;
            string name = Path.GetFileNameWithoutExtension(src);
            string dst = DIR_FONT + "/中文_" + name + Path.GetExtension(src);
            try
            {
                if (!File.Exists(dst)) File.Copy(src, dst, false);
            }
            catch (Exception e)
            {
                log.Add("字体：拷贝 " + name + " 失败：" + e.Message);
                continue;
            }
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var f = AssetDatabase.LoadAssetAtPath<Font>(dst);
            if (f != null)
            {
                log.Add("字体：从系统字体拷入 " + Path.GetFileName(dst) + "（Unity 内置字体没有汉字，必须换）");
                return f;
            }
            log.Add("字体：★ " + Path.GetFileName(dst) + " 导入失败，试下一个");
        }
        log.Add("字体：★ 没找到可用中文字体，文字会显示成方框");
        return null;
    }

    // ================================================================== 内容概览
    static readonly string[] CHAPTER_DIRS = { "第一章", "第二章", "第三章", "第四章", "第五章" };

    /// 把《项目文档/选项收录内容》里的 20 张原图复制进项目（内容概览用，需求要求"优先采用原图"）
    public static void CopyOverviewFrames(bool force, List<string> log)
    {
        int ok = 0, missing = 0;
        for (int c = 0; c < 5; c++)
        {
            string outDir = DIR_OVERVIEW + "/第" + (c + 1) + "章";
            Directory.CreateDirectory(outDir);
            for (int k = 0; k < 4; k++)
            {
                int frameNo = c * 4 + k + 1;
                string rel = "项目文档/选项收录内容/" + CHAPTER_DIRS[c] + "/Frame " + frameNo + ".png";
                string src = FindRepoPath(rel);
                string dst = outDir + "/选择" + (k + 1) + ".png";
                if (src == null)
                {
                    missing++;
                    log.Add("  ★ 找不到收录图：" + rel);
                    continue;
                }
                if (force || !File.Exists(dst)) File.Copy(src, dst, true);
                ImportAsSprite(dst, Vector4.zero, 1024, true);
                ok++;
            }
        }
        AssetDatabase.Refresh();
        log.Add(string.Format("内容概览原图：导入 {0}/20 张{1}", ok, missing > 0 ? "，★ 缺 " + missing + " 张" : ""));
    }

    /// 从项目根往上找仓库里的文件/目录
    public static string FindRepoPath(string relative)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p) || Directory.Exists(p)) return p;
        }
        return null;
    }

    // ------------------------------------------------------------------ 概览数据
    class OvRow { public int Chapter; public string View, Title, Body; }

    static readonly OvRow[] OV_ROWS =
    {
        // 第 1 章（Body 压缩到一行以内，防止概览卡文字超出底图框线）
        new OvRow { Chapter = 1, View = "自我", Title = "徐夏实际害怕的是什么", Body = "怕汇报出错被否定，更怕所有后果只能自己一个人扛。" },
        new OvRow { Chapter = 1, View = "其他", Title = "帮林溪补充一句鼓励的话", Body = "代入林溪的视角：写一句鼓励徐夏的话。" },
        new OvRow { Chapter = 1, View = "旁观", Title = "在张同学看来，她的焦虑是", Body = "放大了失误、忽略了信任：把任务交给她本就是认可。" },
        new OvRow { Chapter = 1, View = "未来", Title = "未来的徐夏会说些什么", Body = "学会了勇敢面对与沟通，不必一个人扛下所有压力。" },

        // 第 2 章
        new OvRow { Chapter = 2, View = "自我", Title = "她在焦虑的是什么", Body = "怕拒绝邀约后被孤立，也怕去了却融不进话题。" },
        new OvRow { Chapter = 2, View = "其他", Title = "帮林溪补充鼓励的话", Body = "代入林溪的视角：写一句让徐夏不再反复纠结的话。" },
        new OvRow { Chapter = 2, View = "旁观", Title = "从陆宣雨的角度看", Body = "放大了拒绝的后果；真朋友不会因小事疏远。" },
        new OvRow { Chapter = 2, View = "未来", Title = "未来的自己会说些什么", Body = "学会坦然拒绝：真正的友谊不需要刻意讨好。" },

        // 第 3 章
        new OvRow { Chapter = 3, View = "自我", Title = "她实际在焦虑什么", Body = "考研就业都没底气，怕的是没有方向、一事无成。" },
        new OvRow { Chapter = 3, View = "其他", Title = "帮李老师补充鼓励的话", Body = "代入老师的视角：写一句给迷茫中的学生的话。" },
        new OvRow { Chapter = 3, View = "旁观", Title = "在老师看来，她的焦虑是", Body = "总和别人比进度，把暂时的迷茫当成了失败。" },
        new OvRow { Chapter = 3, View = "未来", Title = "未来的自己会怎么看待迷茫", Body = "成长没有固定节奏，按自己的步调走就很好。" },

        // 第 4 章
        new OvRow { Chapter = 4, View = "自我", Title = "焦虑的感觉是什么", Body = "怕复习不完、论文写不好，也怕努力没有回报。" },
        new OvRow { Chapter = 4, View = "其他", Title = "帮学姐补充鼓励的话", Body = "代入学姐的视角：写一句让人安心的话。" },
        new OvRow { Chapter = 4, View = "旁观", Title = "图书馆里发现自己的焦虑是", Body = "把困难无限放大，忽略了身边愿意帮忙的人。" },
        new OvRow { Chapter = 4, View = "未来", Title = "未来的自己会对现在说什么", Body = "把大任务拆成小目标，不安并不代表做不到。" },

        // 第 5 章
        new OvRow { Chapter = 5, View = "自我", Title = "她担心的是什么", Body = "怕被舍友嫌弃排斥，也怕小事毁了宿舍关系。" },
        new OvRow { Chapter = 5, View = "其他", Title = "帮陆宣雨补充鼓励的话", Body = "代入宿舍长的视角：写一句让徐夏放下心的话。" },
        new OvRow { Chapter = 5, View = "旁观", Title = "从舍友们的角度看", Body = "把无心之言无限放大，忘了沟通才能解开误会。" },
        new OvRow { Chapter = 5, View = "未来", Title = "未来的自己回看这次矛盾", Body = "内耗之前先主动沟通：相处靠真诚不靠讨好。" },
    };

    public static OverviewDatabase LoadOverviewDb()
    {
        return AssetDatabase.LoadAssetAtPath<OverviewDatabase>(DB_PATH);
    }

    public static OverviewDatabase BuildDatabase(List<string> log)
    {
        var db = AssetDatabase.LoadAssetAtPath<OverviewDatabase>(DB_PATH);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<OverviewDatabase>();
            AssetDatabase.CreateAsset(db, DB_PATH);
        }        db.entries.Clear();
        foreach (var row in OV_ROWS)
        {
            var e = new OverviewEntry
            {
                chapter = row.Chapter,
                view    = row.View,
                title   = row.Title,
                body    = row.Body,
                frame   = AssetDatabase.LoadAssetAtPath<Sprite>(DIR_OVERVIEW + "/第" + row.Chapter + "章/选择"
                            + (CountInChapter(row.Chapter, row) + 1) + ".png")
            };
            db.entries.Add(e);
        }
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        log.Add("内容概览数据：" + DB_PATH + "（" + db.entries.Count + " 条 = 5 章 × 4 处选择）");
        // 必须重新 Load 一次：CreateAsset/Refresh 会把内存里的实例变成 Unity 的 fake-null
        var reloaded = AssetDatabase.LoadAssetAtPath<OverviewDatabase>(DB_PATH);
        return reloaded != null ? reloaded : db;
    }

    static int CountInChapter(int chapter, OvRow row)
    {
        int n = 0;
        foreach (var r in OV_ROWS)
        {
            if (r.Chapter != chapter) continue;
            if (r == row) return n;
            n++;
        }
        return n;
    }
}
