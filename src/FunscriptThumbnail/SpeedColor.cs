using System;
using System.Collections.Generic;
using System.Drawing;

namespace FunscriptThumbnail
{
    /// <summary>
    /// funlib speedToHex(): speed -> OKLCH -> sRGB, reproducing colorizr's oklch2hex rounding exactly.
    /// </summary>
    internal static class SpeedColor
    {
        static readonly Dictionary<double, Color> Cache = new Dictionary<double, Color>();
        static readonly object Lock = new object();

        static double Lerp(double l, double r, double t) { return l * (1 - t) + r * t; }
        static double Unlerp(double l, double r, double v) { return l == r ? 0.5 : (v - l) / (r - l); }
        static double ClampLerp(double v, double inMin, double inMax, double outMin, double outMax)
        {
            return Lerp(outMin, outMax, Math.Max(0, Math.Min(1, Unlerp(inMin, inMax, v))));
        }

        public static Color FromSpeed(double speed)
        {
            speed = Math.Abs(speed);
            lock (Lock)
            {
                Color c;
                if (Cache.TryGetValue(speed, out c)) return c;
                double l = ClampLerp(speed, 500, 600, 0.8, 0.4);
                double ch = ClampLerp(speed, 800, 900, 0.4, 0.1);
                double h = ((210 + speed / -2.4) % 360 + 360) % 360;
                c = Oklch(l, ch, h);
                if (Cache.Count < 20000) Cache[speed] = c;
                return c;
            }
        }

        static double Round(double v, int precision)
        {
            if (v == 0 || double.IsNaN(v)) return 0;
            double f = Math.Pow(10, precision);
            return Math.Floor(v * f + 0.5) / f;
        }

        static double LinToSrgb(double x)
        {
            double a = Math.Abs(x);
            double sign = x < 0 ? -1 : 1;
            if (a > 0.0031308) return sign * (Math.Pow(a, 1 / 2.4) * 1.055 - 0.055);
            return x * 12.92;
        }

        public static Color Oklch(double l, double c, double h)
        {
            if (double.IsNaN(h) || h < 0) h = 0;
            double hr = h * Math.PI / 180;
            double L = Round(l, 5), A = Round(c * Math.Cos(hr), 5), B = Round(c * Math.Sin(hr), 5);

            double lm = Math.Pow(L + 0.3963377773761749 * A + 0.2158037573099136 * B, 3);
            double mm = Math.Pow(L - 0.1055613458156586 * A - 0.0638541728258133 * B, 3);
            double sm = Math.Pow(L - 0.0894841775298119 * A - 1.2914855480194092 * B, 3);

            double r = 255 * LinToSrgb(4.076741636075958 * lm - 3.307711539258063 * mm + 0.2309699031821043 * sm);
            double g = 255 * LinToSrgb(-1.2684379732850315 * lm + 2.609757349287688 * mm - 0.341319376002657 * sm);
            double b = 255 * LinToSrgb(-0.0041960761386756 * lm - 0.7034186179359362 * mm + 1.7076146940746117 * sm);

            return Color.FromArgb(To255(r), To255(g), To255(b));
        }

        static int To255(double v)
        {
            v = Round(v, 0);
            v = Math.Min(Math.Max(v, 0), 255);
            return (int)Math.Floor(v);
        }
    }
}

namespace FunscriptThumbnail
{
    /// <summary>Diagnostics hook for the CLI tool.</summary>
    public static class SpeedColorProbe
    {
        public static System.Drawing.Color Get(double speed) { return SpeedColor.FromSpeed(speed); }
    }
}
