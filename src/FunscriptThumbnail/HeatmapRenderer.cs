using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;

namespace FunscriptThumbnail
{
    public sealed class RenderOptions
    {
        public float Width = 690;          // logical width (funlib default)
        public float Height = 52;          // height per axis incl. title bar
        public float TitleHeight = 20;
        public float LineWidth = 0.5f;
        public float GraphOpacity = 0.2f;
        public float TitleOpacity = 0.7f;
        public double MergeLimit = 500;
        public bool Halo = true;
        public bool ShowChapters = true;
        public float ChapterHeight = 10;
        /// <summary>null = auto: hidden for single axis, 46 for multi-axis scripts.</summary>
        public float? IconWidth = null;

        /// <summary>
        /// Colour every translucent layer is composited onto.
        /// #1a1a1a is the eroscripts.com dark-mode page background: the SVG there is transparent,
        /// so the header drop (#ccc at 30%) and the graph gradient (20%) resolve against it.
        /// 0.3*204 + 0.7*26 = 79.4 -> the "79,79,79" header strip the site shows, and 26,26,26
        /// wherever the gradient is still fully transparent (speed 0). Both values verified by
        /// sampling a screenshot of the site.
        /// </summary>
        public Color Background = DarkPage;

        public static readonly Color DarkPage = Color.FromArgb(0x1a, 0x1a, 0x1a);
        public static readonly Color LightPage = Color.White;
    }

    /// <summary>
    /// GDI+ port of Eroscripts/funlib rendering/svg.ts (MIT, (c) Dimava) — the eroscripts.com heatmap style,
    /// without the title (file name). Logical layout is identical to toSvgElement(); output is scaled.
    /// </summary>
    public static class HeatmapRenderer
    {
        const float SpacingBetweenAxes = 0;

        sealed class Stop { public double Offset; public Color Color; public double Opacity; }

        public static SizeF LogicalSize(FunscriptDoc doc, RenderOptions o)
        {
            float h = o.Height * (1 + doc.Channels.Count) + SpacingBetweenAxes * doc.Channels.Count + ChapterBarHeight(doc, o);
            return new SizeF(o.Width, h);
        }

        /// <summary>Render so that the longest side equals maxSide pixels.</summary>
        public static Bitmap RenderFit(FunscriptDoc doc, int maxSide, RenderOptions o)
        {
            var ls = LogicalSize(doc, o);
            float scale = maxSide / Math.Max(ls.Width, ls.Height);
            return Render(doc, scale, o);
        }

        public static Bitmap Render(FunscriptDoc doc, float scale, RenderOptions o)
        {
            var ls = LogicalSize(doc, o);
            int pw = Math.Max(1, (int)Math.Round(ls.Width * scale));
            int ph = Math.Max(1, (int)Math.Round(ls.Height * scale));
            var bmp = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(o.Background);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // NOT HighQuality: that blends in linear light (gamma-corrected) and washes out the
                // semi-transparent heatmap. SVG/browsers blend in sRGB space.
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.ScaleTransform(pw / ls.Width, ph / ls.Height);

                float iconWidth = o.IconWidth.HasValue ? o.IconWidth.Value : (doc.IsMultiAxis ? 46f : 0f);
                double durationMs = doc.DurationMs > 0 ? doc.DurationMs : 1;

                float chapterBar = ChapterBarHeight(doc, o);
                float y = 0;
                DrawTrack(g, doc.Main, false, y, chapterBar, iconWidth, durationMs, o, scale);
                if (chapterBar > 0) DrawChapters(g, doc, y, chapterBar, iconWidth, durationMs, o);
                y += o.Height + chapterBar + SpacingBetweenAxes;
                foreach (var ch in doc.Channels)
                {
                    DrawTrack(g, ch, true, y, 0, iconWidth, durationMs, o, scale);
                    y += o.Height + SpacingBetweenAxes;
                }
            }
            return bmp;
        }

        static float ChapterBarHeight(FunscriptDoc doc, RenderOptions o)
        {
            if (!o.ShowChapters || doc.Chapters.Count == 0) return 0;
            int maxLayer = LayerChapters(doc.Chapters).Max(e => e.Value);
            return (maxLayer + 1) * o.ChapterHeight;
        }

        static List<KeyValuePair<FunChapter, int>> LayerChapters(List<FunChapter> chapters)
        {
            var last = new List<FunChapter>();
            var res = new List<KeyValuePair<FunChapter, int>>();
            foreach (var c in chapters)
            {
                int layer = 0;
                while (layer < last.Count && last[layer] != null && last[layer].EndAt > c.StartAt) layer++;
                while (last.Count <= layer) last.Add(null);
                last[layer] = c;
                res.Add(new KeyValuePair<FunChapter, int>(c, layer));
            }
            return res;
        }

        // ------------------------------------------------------------------ one axis block
        static void DrawTrack(Graphics g, FunTrack track, bool secondary, float y0, float chapterBar,
                              float iconWidth, double durationMs, RenderOptions o, float scale)
        {
            float top = y0 + chapterBar;
            float titleStart = iconWidth;               // iconSpacing = 0
            float graphWidth = o.Width - titleStart;
            float titleBottom = top + o.TitleHeight;
            float graphTop = titleBottom;               // titleSpacing = 0
            float graphHeight = o.Height - o.TitleHeight;
            float bottom = top + o.Height;

            var stats = Stats.ToStats(track.Actions, durationMs / 1000);
            var stops = BackgroundStops(track.Actions, durationMs);

            Color iconColor = SpeedColor.FromSpeed(stats.AvgSpeed);
            float iconOpacity = (float)Round2(o.TitleOpacity * Math.Max(0.5, Math.Min(1, stats.AvgSpeed / 100.0)));
            var ccc = Color.FromArgb(0xcc, 0xcc, 0xcc);
            float dropOpacity = (float)Round2(o.GraphOpacity * 1.5);

            // backgrounds
            // funlib draws these as translucent SVG rects; GDI+ would quantise the alpha to 8 bits
            // (0.3 -> 77/255) and land on 80 instead of the 79 a browser produces from exact 0.3.
            // Resolving each flat layer against what is actually underneath removes that error.
            Color drop = Blend(ccc, dropOpacity, o.Background);
            Color icon = Blend(iconColor, iconOpacity, drop);
            if (iconWidth > 0) Fill(g, drop, 0, top, iconWidth, bottom - top);
            Fill(g, drop, titleStart, top, graphWidth, titleBottom - top);
            if (iconWidth > 0) Fill(g, icon, 0, top, iconWidth, bottom - top);
            FillGradient(g, stops, o.TitleOpacity, titleStart, top, graphWidth, titleBottom - top, scale);
            FillGradient(g, stops, o.GraphOpacity, titleStart, graphTop, graphWidth, graphHeight, scale);

            // motion lines
            DrawLines(g, track.Actions, o, titleStart, graphTop, graphWidth, graphHeight, durationMs);

            // stats text (right aligned, reverse order)
            var entries = new List<KeyValuePair<string, string>>();
            if (!secondary) entries.Add(new KeyValuePair<string, string>("Duration", stats.Duration));
            entries.Add(new KeyValuePair<string, string>("Actions", stats.Actions.ToString()));
            entries.Add(new KeyValuePair<string, string>("MaxSpeed", stats.MaxSpeed.ToString()));
            entries.Add(new KeyValuePair<string, string>("AvgSpeed", stats.AvgSpeed.ToString()));
            entries.Reverse();

            float k = o.TitleHeight / 20f;
            float labelSize = (float)Round2(o.TitleHeight * 0.4);
            float valueSize = (float)Round2(o.TitleHeight * 0.72);
            float labelY = (float)Round2(top + o.TitleHeight * 0.35);
            float valueY = (float)Round2(top + o.TitleHeight * 0.92);

            using (var textPath = new GraphicsPath())
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    float x = (float)Round2(o.Width - (7 + i * 46) * k);
                    AddText(textPath, entries[i].Key, Fonts.Sans, FontStyle.Bold, labelSize, x, labelY, StringAlignment.Far);
                    AddText(textPath, entries[i].Value, Fonts.Sans, FontStyle.Bold, valueSize, x, valueY, StringAlignment.Far);
                }
                if (o.Halo)
                {
                    using (var pen = new Pen(Color.FromArgb(128, 255, 255, 255), 3f) { LineJoin = LineJoin.Round })
                        g.DrawPath(pen, textPath);
                }
                using (var path2 = new GraphicsPath())
                {
                    if (iconWidth > 0)
                    {
                        float iconSize = (float)Round2(Math.Max(12, iconWidth * 0.75));
                        float cy = (float)Round2((top + bottom) / 2 + 4);
                        // dominant-baseline="middle": baseline sits ~half an x-height below the anchor
                        float baseline = cy + iconSize * 0.27f;
                        AddText(path2, track.Axis, Fonts.Mono, FontStyle.Regular, iconSize, iconWidth / 2, baseline, StringAlignment.Center);
                    }
                    path2.AddPath(textPath, false);
                    g.FillPath(Brushes.Black, path2);
                }
            }
        }

        static void DrawLines(Graphics g, List<FunAction> actions, RenderOptions o,
                              float x0, float y0, float width, float height, double durationMs)
        {
            float w = o.LineWidth;
            var lines = Stats.ActionsToLines(actions);
            Stats.MergeLinesSpeed(lines, o.MergeLimit);
            var sorted = lines.OrderBy(l => l.Speed).ToList();   // stable, slow first / fast on top

            var pens = new Dictionary<int, Pen>();
            var state = g.Save();
            g.TranslateTransform(x0, y0);
            try
            {
                foreach (var l in sorted)
                {
                    Color c = SpeedColor.FromSpeed(l.Speed);
                    Pen pen;
                    if (!pens.TryGetValue(c.ToArgb(), out pen))
                    {
                        pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                        pens[c.ToArgb()] = pen;
                    }
                    float ax = (float)(l.A.At / durationMs * (width - 2 * w) + w);
                    float bx = (float)(l.B.At / durationMs * (width - 2 * w) + w);
                    float ay = (float)((100 - l.A.Pos) * (height - 2 * w) / 100 + w);
                    float by = (float)((100 - l.B.Pos) * (height - 2 * w) / 100 + w);
                    g.DrawLine(pen, ax, ay, bx, by);
                }
            }
            finally
            {
                g.Restore(state);
                foreach (var p in pens.Values) p.Dispose();
            }
        }

        // ------------------------------------------------------------------ background gradient
        static List<Stop> BackgroundStops(List<FunAction> actions, double durationMs)
        {
            var zig = Stats.ActionsToZigzag(actions);
            var raw = Stats.ActionsToLines(zig);

            var lines = new List<FunLine>();
            foreach (var e in raw)
            {
                double len = e.B.At - e.A.At;
                if (len <= 0) continue;
                if (len < 2000) { lines.Add(e); continue; }
                int N = (int)Math.Truncate((len - 500) / 1000);
                for (int i = 0; i < N; i++)
                {
                    var a = new FunAction(Lerp(e.A.At, e.B.At, (double)i / N), Lerp(e.A.Pos, e.B.Pos, (double)i / N));
                    var b = new FunAction(Lerp(e.A.At, e.B.At, (double)(i + 1) / N), Lerp(e.A.Pos, e.B.Pos, (double)(i + 1) / N));
                    lines.Add(new FunLine(a, b, e.Speed));
                }
            }
            // merge lines so they are at least ~1000ms long
            for (int i = 0; i < lines.Count - 1; i++)
            {
                var ab = lines[i]; var cd = lines[i + 1];
                if (cd.B.At - ab.A.At < 1000)
                {
                    double d1 = ab.B.At - ab.A.At, d2 = cd.B.At - cd.A.At;
                    double speed = (ab.Speed * d1 + cd.Speed * d2) / (d1 + d2);
                    lines.RemoveRange(i, 2);
                    lines.Insert(i, new FunLine(ab.A, cd.B, speed));
                    i--;
                }
            }

            var pts = new List<KeyValuePair<double, double>>(); // at, speed
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0 && i < lines.Count - 1 && lines[i - 1].Speed == lines[i].Speed && lines[i].Speed == lines[i + 1].Speed) continue;
                pts.Add(new KeyValuePair<double, double>((lines[i].A.At + lines[i].B.At) / 2, lines[i].Speed));
            }
            if (lines.Count > 0)
            {
                var first = lines[0]; var last = lines[lines.Count - 1];
                pts.Insert(0, new KeyValuePair<double, double>(first.A.At, first.Speed));
                if (first.A.At > 100) pts.Insert(0, new KeyValuePair<double, double>(first.A.At - 100, 0));
                pts.Add(new KeyValuePair<double, double>(last.B.At, last.Speed));
                if (last.B.At < durationMs - 100) pts.Add(new KeyValuePair<double, double>(last.B.At + 100, 0));
            }
            var stops = new List<Stop>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0 && i < pts.Count - 1 && pts[i - 1].Value == pts[i].Value && pts[i].Value == pts[i + 1].Value) continue;
                double s = pts[i].Value;
                stops.Add(new Stop
                {
                    Offset = Round2(Math.Max(0, Math.Min(1, pts[i].Key / durationMs))),
                    Color = SpeedColor.FromSpeed(s),
                    Opacity = s >= 100 ? 1 : Round2(s / 100),
                });
            }
            // SVG: offsets are clamped to be non-decreasing
            for (int i = 1; i < stops.Count; i++) if (stops[i].Offset < stops[i - 1].Offset) stops[i].Offset = stops[i - 1].Offset;
            return stops;
        }

        static void FillGradient(Graphics g, List<Stop> stops, float opacity, float x, float y, float w, float h, float scale)
        {
            if (stops.Count == 0) return;   // url(#id) with no stops = no paint
            int cols = Math.Max(1, (int)Math.Ceiling(w * scale));
            using (var strip = new Bitmap(cols, 1, PixelFormat.Format32bppArgb))
            {
                for (int i = 0; i < cols; i++)
                {
                    double t = (i + 0.5) / cols;
                    strip.SetPixel(i, 0, Sample(stops, t, opacity));
                }
                var st = g.Save();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(strip, new[] { new PointF(x, y), new PointF(x + w, y), new PointF(x, y + h) },
                        new RectangleF(0, 0, cols, 1), GraphicsUnit.Pixel, ia);
                }
                g.Restore(st);
            }
        }

        static Color Sample(List<Stop> stops, double t, float opacity)
        {
            Stop a, b;
            if (t <= stops[0].Offset) { a = b = stops[0]; }
            else if (t >= stops[stops.Count - 1].Offset) { a = b = stops[stops.Count - 1]; }
            else
            {
                int i = 0;
                while (i + 1 < stops.Count && stops[i + 1].Offset <= t) i++;
                a = stops[i]; b = stops[Math.Min(i + 1, stops.Count - 1)];
            }
            double f = (b.Offset > a.Offset) ? (t - a.Offset) / (b.Offset - a.Offset) : 0;
            double r = Lerp(a.Color.R, b.Color.R, f), gg = Lerp(a.Color.G, b.Color.G, f), bb = Lerp(a.Color.B, b.Color.B, f);
            double al = Lerp(a.Opacity, b.Opacity, f) * opacity;
            return Color.FromArgb(Clamp255(al * 255), Clamp255(r), Clamp255(gg), Clamp255(bb));
        }

        // ------------------------------------------------------------------ chapters
        static readonly Color[] ChapterColors = Enumerable.Range(0, 15)
            .Select(i => SpeedColor.Oklch(0.9, 0.25, (i * 720.0 / 15 + 30) % 360)).ToArray();

        static void DrawChapters(Graphics g, FunscriptDoc doc, float y0, float barH, float iconWidth, double durationMs, RenderOptions o)
        {
            float graphStart = iconWidth, graphWidth = o.Width - graphStart;
            float r = Math.Min(4, o.ChapterHeight / 2);
            float fontSize = (float)Round2(o.TitleHeight * 0.4);
            var layered = LayerChapters(doc.Chapters);
            int maxLayer = layered.Max(e => e.Value);

            var st = g.Save();
            g.SetClip(new RectangleF(graphStart, y0, graphWidth, barH));
            for (int layer = maxLayer; layer >= 0; layer--)
            {
                foreach (var kv in layered.Where(e => e.Value == layer))
                {
                    var c = kv.Key;
                    int idx = doc.Chapters.IndexOf(c);
                    var color = ChapterColors[idx % ChapterColors.Length];
                    float x = (float)Round2(graphStart + c.StartAt / durationMs * graphWidth);
                    float cw = (float)Round2((c.EndAt - c.StartAt) / durationMs * graphWidth);
                    float y = (float)Round2(barH - (layer + 1) * o.ChapterHeight);
                    float rh = (float)Round2(barH - y + r);
                    if (cw <= 0) continue;
                    using (var path = RoundRect(x, y0 + y, cw, rh, Math.Min(r, cw / 2)))
                    using (var br = new SolidBrush(Color.FromArgb(128, color)))
                        g.FillPath(br, path);

                    if (cw >= fontSize * 2 && !string.IsNullOrEmpty(c.Name))
                    {
                        float tx = (float)Round2(x + cw / 2);
                        float ty = (float)Round2(y0 + y + o.ChapterHeight / 2 + fontSize * 0.35);
                        string label = c.Name;
                        float maxW = cw - 8;
                        while (label.Length > 0 && Measure(g, label, fontSize) > maxW) label = label.Substring(0, label.Length - 1);
                        if (label.Length == 0) continue;
                        using (var tp = new GraphicsPath())
                        {
                            AddText(tp, label, Fonts.Sans, FontStyle.Bold, fontSize, tx, ty, StringAlignment.Center);
                            if (o.Halo)
                                using (var pen = new Pen(Color.FromArgb(128, 255, 255, 255), 3f) { LineJoin = LineJoin.Round })
                                    g.DrawPath(pen, tp);
                            g.FillPath(Brushes.Black, tp);
                        }
                    }
                }
            }
            g.Restore(st);
        }

        static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            if (r <= 0) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------ helpers
        static class Fonts
        {
            public static readonly FontFamily Sans = Pick("Arial", GenericFontFamilies.SansSerif);
            public static readonly FontFamily Mono = Pick("Consolas", GenericFontFamilies.Monospace);
            static FontFamily Pick(string name, GenericFontFamilies fallback)
            {
                try { return new FontFamily(name); }
                catch (ArgumentException) { return new FontFamily(fallback); }
            }
        }

        static float Measure(Graphics g, string text, float size)
        {
            using (var f = new Font(Fonts.Sans, size, FontStyle.Bold, GraphicsUnit.Pixel))
                return g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic).Width;
        }

        /// <summary>Adds text whose baseline is at y; x is the start/center/end depending on alignment (SVG text-anchor).</summary>
        static void AddText(GraphicsPath path, string text, FontFamily family, FontStyle style, float size,
                            float x, float baselineY, StringAlignment align)
        {
            if (string.IsNullOrEmpty(text)) return;
            float ascent = size * family.GetCellAscent(style) / family.GetEmHeight(style);
            using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                sf.Alignment = align;
                const float big = 4000;
                RectangleF rect;
                if (align == StringAlignment.Far) rect = new RectangleF(x - big, baselineY - ascent, big, size * 2);
                else if (align == StringAlignment.Center) rect = new RectangleF(x - big / 2, baselineY - ascent, big, size * 2);
                else rect = new RectangleF(x, baselineY - ascent, big, size * 2);
                path.AddString(text.Trim(), family, (int)style, size, rect, sf);
            }
        }

        /// <summary>Composites <paramref name="top"/> at <paramref name="alpha"/> over an opaque colour, in float.</summary>
        static Color Blend(Color top, double alpha, Color under)
        {
            return Color.FromArgb(255,
                Clamp255(alpha * top.R + (1 - alpha) * under.R),
                Clamp255(alpha * top.G + (1 - alpha) * under.G),
                Clamp255(alpha * top.B + (1 - alpha) * under.B));
        }

        static void Fill(Graphics g, Color c, float x, float y, float w, float h)
        {
            using (var b = new SolidBrush(c))
                g.FillRectangle(b, x, y, w, h);
        }

        static double Lerp(double a, double b, double t) { return a * (1 - t) + b * t; }
        static double Round2(double v) { return Math.Floor(v * 100 + 0.5) / 100; }
        static int Clamp255(double v) { return (int)Math.Max(0, Math.Min(255, Math.Floor(v + 0.5))); }
    }
}
