using System;
using System.Collections.Generic;
using System.Linq;

namespace FunscriptThumbnail
{
    /// <summary>A line between two actions with a (possibly merged) absolute speed. Port of funlib actionsToLines.</summary>
    internal sealed class FunLine
    {
        public FunAction A, B;
        public double Speed;       // [2] in funlib (abs speed, may be overwritten by merging)
        public double AbsSpeed;
        public int SpeedSign;
        public double Dat;
        public FunLine(FunAction a, FunAction b, double speed) { A = a; B = b; Speed = speed; }
    }

    internal sealed class ScriptStats
    {
        public string Duration;
        public int Actions;
        public int MaxSpeed;
        public int AvgSpeed;
    }

    /// <summary>Faithful port of the analysis helpers from Eroscripts/funlib (MIT) utils/manipulations.ts.</summary>
    internal static class Stats
    {
        public static double SpeedBetween(FunAction a, FunAction b)
        {
            if (a == null || b == null) return 0;
            if (a.At == b.At) return 0;
            return (b.Pos - a.Pos) / (b.At - a.At) * 1000;
        }

        /// <summary>-1 / 0 / 1</summary>
        public static int IsPeak(List<FunAction> actions, int index)
        {
            var action = actions[index];
            var prev = index > 0 ? actions[index - 1] : null;
            var next = index + 1 < actions.Count ? actions[index + 1] : null;
            if (prev == null && next == null) return 1;
            if (prev == null) return 1;
            if (next == null) return -1;
            double speedTo = SpeedBetween(prev, action);
            double speedFrom = SpeedBetween(action, next);
            if (Math.Sign(speedTo) == Math.Sign(speedFrom)) return 0;
            if (speedTo > speedFrom) return 1;
            if (speedTo < speedFrom) return -1;
            return 0;
        }

        public static List<FunLine> ActionsToLines(List<FunAction> actions)
        {
            var res = new List<FunLine>(Math.Max(0, actions.Count - 1));
            for (int i = 1; i < actions.Count; i++)
            {
                var p = actions[i - 1]; var e = actions[i];
                double speed = SpeedBetween(p, e);
                var l = new FunLine(p, e, Math.Abs(speed))
                {
                    AbsSpeed = Math.Abs(speed),
                    SpeedSign = Math.Sign(speed),
                    Dat = e.At - p.At,
                };
                if (p.At < e.At) res.Add(l);
            }
            return res;
        }

        public static List<FunAction> ActionsToZigzag(List<FunAction> actions)
        {
            var res = new List<FunAction>();
            for (int i = 0; i < actions.Count; i++)
                if (IsPeak(actions, i) != 0) res.Add(actions[i].Clone());
            return res;
        }

        public static void MergeLinesSpeed(List<FunLine> lines, double mergeLimit)
        {
            if (mergeLimit == 0) return;
            int j = 0;
            for (int i = 0; i < lines.Count - 1; i = j + 1)
            {
                for (j = i; j < lines.Count - 1; j++)
                    if (lines[i].SpeedSign != lines[j + 1].SpeedSign) break;
                if (i == j) continue;
                double sumDat = 0, sumW = 0;
                for (int k = i; k <= j; k++) { sumDat += lines[k].Dat; sumW += lines[k].AbsSpeed * lines[k].Dat; }
                if (sumDat > mergeLimit) continue;
                double avg = sumW / sumDat;
                for (int k = i; k <= j; k++) lines[k].Speed = avg;
            }
        }

        public static double AverageSpeed(List<FunAction> actions)
        {
            var zigzag = ActionsToZigzag(actions);
            var fast = new List<FunAction>();
            for (int i = 0; i < zigzag.Count; i++)
                if (i > 0 && Math.Abs(SpeedBetween(zigzag[i - 1], zigzag[i])) > 30) fast.Add(zigzag[i]);

            double num = 0, den = 0;
            for (int i = 0; i < fast.Count; i++)
            {
                var prev = i > 0 ? fast[i - 1] : null;
                var next = i + 1 < fast.Count ? fast[i + 1] : null;
                double speedTo = prev != null ? Math.Abs(SpeedBetween(prev, fast[i])) : 0;
                double datNext = next != null ? next.At - fast[i].At : 0;
                num += speedTo * datNext;
                den += datNext;
            }
            return num / (den == 0 ? 1 : den);
        }

        public static double RequiredMaxSpeed(List<FunAction> actions)
        {
            if (actions.Count < 2) return 0;
            int n = actions.Count;
            var peak = new int[n];
            for (int i = 0; i < n; i++) peak[i] = IsPeak(actions, i);
            // nextPeakAfter[i] = first idx > i with peak != 0, or -1
            var nextPeakAfter = new int[n];
            int nxt = -1;
            for (int i = n - 1; i >= 0; i--) { nextPeakAfter[i] = nxt; if (peak[i] != 0) nxt = i; }

            var required = new List<KeyValuePair<double, double>>();
            int nextPeakIndex = 0;
            for (int i = 0; i < n; i++)
            {
                var a = actions[i];
                if (nextPeakIndex == i)
                {
                    nextPeakIndex = nextPeakAfter[i];
                    if (nextPeakIndex == -1) break;
                }
                var np = actions[nextPeakIndex];
                required.Add(new KeyValuePair<double, double>(Math.Abs(SpeedBetween(a, np)), np.At - a.At));
            }
            // sort by speed descending (funlib: stable ascending sort, then reverse)
            var sorted = required.OrderBy(e => e.Key).ToList();
            sorted.Reverse();
            foreach (var e in sorted) if (e.Value >= 50) return e.Key;
            return 0;
        }

        public static string SecondsToDuration(double seconds)
        {
            seconds = FunscriptDoc.JsRound(seconds);
            long s = (long)seconds;
            if (s < 3600) return (s / 60) + ":" + (s % 60).ToString("00");
            return (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        public static ScriptStats ToStats(List<FunAction> actions, double durationSeconds)
        {
            int peaks = 0;
            for (int i = 0; i < actions.Count; i++) if (IsPeak(actions, i) != 0) peaks++;
            return new ScriptStats
            {
                Duration = SecondsToDuration(durationSeconds),
                Actions = peaks,
                MaxSpeed = (int)FunscriptDoc.JsRound(RequiredMaxSpeed(actions)),
                AvgSpeed = (int)FunscriptDoc.JsRound(AverageSpeed(actions)),
            };
        }
    }
}
