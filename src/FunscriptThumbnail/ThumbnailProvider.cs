using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace FunscriptThumbnail
{
    public enum WTS_ALPHATYPE { WTSAT_UNKNOWN = 0, WTSAT_RGB = 1, WTSAT_ARGB = 2 }

    [ComImport, Guid("e357fccd-a995-4576-b01f-234630154e96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        void GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    /// <summary>
    /// Windows Shell thumbnail provider for .funscript files. Explorer shows the result in the
    /// Details pane header, in large/extra-large icon views, and in tooltips.
    /// </summary>
    [ComVisible(true)]
    [Guid(ThumbnailProvider.ClsidString)]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("FunscriptThumbnail.Provider")]
    public sealed class ThumbnailProvider : IThumbnailProvider, IInitializeWithStream
    {
        public const string ClsidString = "6E3A2F41-6B1D-4C3E-9F27-2B8D5A7C1E90";
        const long MaxFileBytes = 64L * 1024 * 1024;

        byte[] _data;

        public void Initialize(IStream pstream, uint grfMode)
        {
            if (_data != null) throw new COMException("Already initialized", unchecked((int)0x8007139F)); // HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED)
            try
            {
                Diag.Log("Initialize pid=" + System.Diagnostics.Process.GetCurrentProcess().ProcessName);
                _data = ReadAll(pstream);
            }
            finally
            {
                if (pstream != null) Marshal.ReleaseComObject(pstream);
            }
        }

        public void GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha)
        {
            phbmp = IntPtr.Zero;
            pdwAlpha = WTS_ALPHATYPE.WTSAT_RGB;
            Diag.Log("GetThumbnail cx=" + cx + " pid=" + System.Diagnostics.Process.GetCurrentProcess().ProcessName);
            if (_data == null) throw new COMException("Not initialized", unchecked((int)0x8000FFFF));

            string json = DecodeText(_data);
            var doc = FunscriptDoc.Parse(json);
            int side = (int)Math.Max(16, Math.Min(cx, 4096));
            using (var bmp = HeatmapRenderer.RenderFit(doc, side, new RenderOptions()))
            {
                phbmp = bmp.GetHbitmap(Color.White);
            }
        }

        public static string DecodeText(byte[] data)
        {
            // UTF-8 (with or without BOM) / UTF-16 BOM
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            int off = (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) ? 3 : 0;
            return Encoding.UTF8.GetString(data, off, data.Length - off);
        }

        static byte[] ReadAll(IStream stream)
        {
            var ms = new MemoryStream();
            var buf = new byte[81920];
            IntPtr pRead = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                while (true)
                {
                    Marshal.WriteInt32(pRead, 0);
                    stream.Read(buf, buf.Length, pRead);
                    int n = Marshal.ReadInt32(pRead);
                    if (n <= 0) break;
                    ms.Write(buf, 0, n);
                    if (ms.Length > MaxFileBytes) throw new COMException("File too large", unchecked((int)0x80004005));
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pRead);
            }
            return ms.ToArray();
        }
    }
}
