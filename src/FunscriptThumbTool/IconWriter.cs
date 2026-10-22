using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using FunscriptThumbnail;

namespace FunscriptThumbTool
{
    /// <summary>
    /// Builds a small multi-size .ico (PNG-compressed entries, Vista+) so the entry in
    /// Settings -> Apps -> Installed apps has a real icon instead of a blank sheet.
    /// The glyph is a miniature of what the plugin actually renders: the #1a1a1a ES dark page,
    /// the #4f4f4f header strip and a speed-coloured heat band with a motion zigzag on top.
    /// </summary>
    internal static class IconWriter
    {
        static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

        public static void Write(string path)
        {
            var png = new List<byte[]>();
            foreach (int s in Sizes)
            {
                using (var bmp = Draw(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    png.Add(ms.ToArray());
                }
            }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);                       // reserved
                w.Write((ushort)1);                       // type: icon
                w.Write((ushort)Sizes.Length);
                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));   // 0 means 256
                    w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                    w.Write((byte)0);                     // palette colours
                    w.Write((byte)0);                     // reserved
                    w.Write((ushort)1);                   // colour planes
                    w.Write((ushort)32);                  // bits per pixel
                    w.Write((uint)png[i].Length);
                    w.Write((uint)offset);
                    offset += png[i].Length;
                }
                foreach (var p in png) w.Write(p);
            }
        }

        static Bitmap Draw(int n)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // same reason as the renderer: HighQuality blends in linear light and dulls colour
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.Clear(Color.Transparent);

                using (var p = RoundRect(0, 0, n, n, n * 0.18f))
                using (var b = new SolidBrush(RenderOptions.DarkPage))
                    g.FillPath(b, p);

                float x0 = n * 0.075f, x1 = n * 0.925f, w = x1 - x0;
                float gy0 = n * 0.34f, gy1 = n * 0.90f, gh = gy1 - gy0;

                // heat band, coloured with the real speed palette
                int cols = Math.Max(2, n);
                using (var strip = new Bitmap(cols, 1, PixelFormat.Format32bppArgb))
                {
                    for (int i = 0; i < cols; i++)
                        strip.SetPixel(i, 0, SpeedColorProbe.Get(i * 1000.0 / (cols - 1)));
                    var st = g.Save();
                    using (var clip = RoundRect(x0, gy0, w, gh, n * 0.06f))
                    {
                        g.SetClip(clip);
                        using (var ia = new ImageAttributes())
                        {
                            var cm = new ColorMatrix();
                            cm.Matrix33 = 0.85f;
                            ia.SetColorMatrix(cm);
                            g.DrawImage(strip,
                                new[] { new PointF(x0, gy0), new PointF(x1, gy0), new PointF(x0, gy1) },
                                new RectangleF(0, 0, cols, 1), GraphicsUnit.Pixel, ia);
                        }
                    }
                    g.Restore(st);
                }

                // motion zigzag, the same white line the plugin draws over the heatmap
                using (var pen = new Pen(Color.FromArgb(238, 255, 255, 255), Math.Max(1f, n * 0.075f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                })
                {
                    const int seg = 4;
                    var pts = new PointF[seg + 1];
                    for (int i = 0; i <= seg; i++)
                        pts[i] = new PointF(x0 + w * i / seg, gy0 + gh * (i % 2 == 0 ? 0.80f : 0.20f));
                    g.DrawLines(pen, pts);
                }

                // header strip in the exact ES header grey
                using (var b = new SolidBrush(Color.FromArgb(0x4f, 0x4f, 0x4f)))
                    g.FillRectangle(b, x0, n * 0.155f, w, n * 0.125f);
            }
            return bmp;
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
    }
}
