using System;
using System.IO;
using System.Text;

namespace FunscriptThumbnail
{
    /// <summary>
    /// Optional trace log (only written when %TEMP%\FunscriptThumbnail.log already exists).
    /// Used to confirm that Explorer really calls the handler. The file is never created by the
    /// provider itself so that nothing is written on a normal install.
    /// </summary>
    internal static class Diag
    {
        public static readonly string LogPath = Path.Combine(Path.GetTempPath(), "FunscriptThumbnail.log");

        public static void Log(string message)
        {
            try
            {
                if (!File.Exists(LogPath)) return;
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* never let diagnostics break thumbnails */ }
        }
    }
}
