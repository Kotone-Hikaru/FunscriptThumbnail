using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FunscriptThumbnail
{
    public sealed class FunAction
    {
        public double At;
        public double Pos;
        public FunAction(double at, double pos) { At = at; Pos = pos; }
        public FunAction Clone() { return new FunAction(At, Pos); }
    }

    public sealed class FunChapter
    {
        public string Name = "";
        public double StartAt;
        public double EndAt;
    }

    public sealed class FunTrack
    {
        /// <summary>Axis id shown in the left square, e.g. L0, L1, R2.</summary>
        public string Axis = "L0";
        public List<FunAction> Actions = new List<FunAction>();
    }

    /// <summary>
    /// A parsed funscript (main track + optional secondary axes), normalized exactly the way
    /// Eroscripts/funlib does before rendering (Funscript.normalize()).
    /// </summary>
    public sealed class FunscriptDoc
    {
        public FunTrack Main = new FunTrack();
        public List<FunTrack> Channels = new List<FunTrack>();
        public List<FunChapter> Chapters = new List<FunChapter>();
        /// <summary>Duration used for the time axis, in milliseconds (funlib: actualDuration after normalize).</summary>
        public double DurationMs;

        public bool IsMultiAxis { get { return Channels.Count > 0; } }

        static readonly Dictionary<string, string> NameToAxis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "stroke", "L0" }, { "surge", "L1" }, { "sway", "L2" },
            { "twist", "R0" }, { "roll", "R1" }, { "pitch", "R2" }, { "suck", "A1" },
        };

        public static FunscriptDoc Parse(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("funscript root is not an object");

            var doc = new FunscriptDoc();
            doc.Main.Actions = ReadActions(Get(root, "actions"));

            // secondary axes: v1.1 "axes": [{id, actions}], v2.0 "channels": {name: {actions}} (or array)
            object chObj = Get(root, "channels") ?? Get(root, "axes");
            var chList = chObj as List<object>;
            var chDict = chObj as Dictionary<string, object>;
            if (chList != null)
            {
                foreach (var o in chList)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    string axis = AsString(Get(d, "id")) ?? AsString(Get(d, "channel")) ?? "L0";
                    doc.Channels.Add(new FunTrack { Axis = ToAxis(axis), Actions = ReadActions(Get(d, "actions")) });
                }
            }
            else if (chDict != null)
            {
                foreach (var kv in chDict)
                {
                    var d = kv.Value as Dictionary<string, object>;
                    if (d == null) continue;
                    doc.Channels.Add(new FunTrack { Axis = ToAxis(kv.Key), Actions = ReadActions(Get(d, "actions")) });
                }
            }

            double metaDuration = 0;
            var meta = Get(root, "metadata") as Dictionary<string, object>;
            if (meta != null)
            {
                metaDuration = AsNumber(Get(meta, "duration"));
                if (double.IsNaN(metaDuration) || metaDuration < 0) metaDuration = 0;
                // funlib FunMetadata: a "duration" given in ms is converted back to seconds
                if (metaDuration > 3600)
                {
                    double ad = doc.Main.Actions.Count > 0 ? doc.Main.Actions[doc.Main.Actions.Count - 1].At / 1000 : 0;
                    if (ad != 0 && ad < 500 * metaDuration) metaDuration /= 1000;
                }
                var chapters = Get(meta, "chapters") as List<object>;
                if (chapters != null)
                {
                    foreach (var o in chapters)
                    {
                        var c = o as Dictionary<string, object>;
                        if (c == null) continue;
                        double s, e;
                        if (!TryTimeSpan(Get(c, "startTime"), out s) || !TryTimeSpan(Get(c, "endTime"), out e)) continue;
                        doc.Chapters.Add(new FunChapter { Name = AsString(Get(c, "name")) ?? "", StartAt = s, EndAt = e });
                    }
                }
            }

            // --- normalize (channels first, then main), as funlib does ---
            foreach (var ch in doc.Channels) ch.Actions = Normalize(ch.Actions);
            doc.Main.Actions = Normalize(doc.Main.Actions);

            double dur = ActualDuration(doc, metaDuration);
            double normDur = Math.Ceiling(dur);              // metadata.duration = ceil(actualDuration)
            doc.DurationMs = ActualDuration(doc, normDur) * 1000;
            return doc;
        }

        static double ActionsDuration(FunscriptDoc doc)
        {
            double m = doc.Main.Actions.Count > 0 ? doc.Main.Actions[doc.Main.Actions.Count - 1].At : 0;
            foreach (var ch in doc.Channels)
                if (ch.Actions.Count > 0) m = Math.Max(m, ch.Actions[ch.Actions.Count - 1].At);
            return m / 1000;
        }

        static double ActualDuration(FunscriptDoc doc, double metaDuration)
        {
            double ad = ActionsDuration(doc);
            if (metaDuration == 0) return ad;
            if (ad > metaDuration) return ad;
            if (ad * 3 < metaDuration) return ad;
            return metaDuration;
        }

        static List<FunAction> Normalize(List<FunAction> actions)
        {
            foreach (var e in actions)
            {
                double at = JsRound(e.At); if (double.IsNaN(at) || double.IsInfinity(at)) at = 0;
                double pos = JsRound(e.Pos); if (double.IsNaN(pos) || double.IsInfinity(pos)) pos = 0;
                e.At = at;
                e.Pos = Math.Max(0, Math.Min(100, pos));
            }
            // stable sort by time, then drop duplicates (keep first)
            var sorted = actions.OrderBy(a => a.At).ToList();
            var res = new List<FunAction>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
                if (i == 0 || sorted[i - 1].At < sorted[i].At) res.Add(sorted[i]);

            var negatives = res.Where(a => a.At < 0).ToList();
            if (negatives.Count > 0)
            {
                res = res.Where(a => a.At >= 0).ToList();
                if (res.Count > 0 && res[0].At > 0)
                {
                    var last = negatives[negatives.Count - 1];
                    last.At = 0;
                    res.Insert(0, last);
                }
            }
            return res;
        }

        internal static double JsRound(double v) { return Math.Floor(v + 0.5); }

        static string ToAxis(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return "L0";
            string axis;
            if (NameToAxis.TryGetValue(idOrName, out axis)) return axis;
            return idOrName.ToUpperInvariant();
        }

        static List<FunAction> ReadActions(object o)
        {
            var list = new List<FunAction>();
            var arr = o as List<object>;
            if (arr == null) return list;
            foreach (var item in arr)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                double at = AsNumber(Get(d, "at"));
                double pos = AsNumber(Get(d, "pos"));
                list.Add(new FunAction(at, pos));
            }
            return list;
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        static string AsString(object o)
        {
            if (o == null) return null;
            if (o is string) return (string)o;
            if (o is double) return ((double)o).ToString(CultureInfo.InvariantCulture);
            return o.ToString();
        }

        static double AsNumber(object o)
        {
            if (o is double) return (double)o;
            if (o is bool) return (bool)o ? 1 : 0;
            var s = o as string;
            double v;
            if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return double.NaN;
        }

        /// <summary>funlib timeSpanToMs: "[-]HH:MM:SS.mmm" (missing parts allowed).</summary>
        static bool TryTimeSpan(object o, out double ms)
        {
            ms = 0;
            var s = o as string;
            if (s == null) { if (o is double) { ms = (double)o; return true; } return false; }
            double sign = 1;
            if (s.StartsWith("-")) { sign = -1; s = s.Substring(1); }
            var parts = s.Split(':').ToList();
            while (parts.Count < 3) parts.Insert(0, "0");
            double h, m, sec;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out h)) return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return false;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sec)) return false;
            ms = JsRound(sign * (h * 3600 + m * 60 + sec) * 1000);
            return true;
        }
    }
}
