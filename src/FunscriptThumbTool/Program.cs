using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FunscriptThumbnail;
using Microsoft.Win32;

namespace FunscriptThumbTool
{
    internal static class Program
    {
        const string ThumbHandlerIid = "{e357fccd-a995-4576-b01f-234630154e96}";
        const string Clsid = "{" + ThumbnailProvider.ClsidString + "}";
        const string FriendlyName = "Funscript Thumbnail (eroscripts style)";
        const string MarkerKey = @"Software\FunscriptThumbnail";
        // shows up in Settings -> Apps -> Installed apps and in the classic "Programs and Features"
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FunscriptThumbnail";
        const string ProductName = "Funscript 缩略图 (Windows 资源管理器)";
        const string ProductVersion = "1.1.0";
        const string Publisher = "funlib / Eroscripts (MIT) · martinAlt335";
        const string ProjectUrl = "https://github.com/martinAlt335/FunscriptWindowsPreviewExtension";
        const string ProductComments =
            "为 Windows 资源管理器添加 .funscript 热力图缩略图（详细信息窗格图标 / 大图标视图 / 工具提示）。"
            + "渲染算法移植自 Eroscripts funlib（MIT），配色为 eroscripts.com 深色模式。";

        // set by Uninstall(): the install tree could not be removed while this exe is still running,
        // so it is handed to a detached helper *after* we are done (and after any "press a key").
        static readonly List<string> PendingDelete = new List<string>();

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                // so the Chinese lines below survive a redirected pipe / a non-936 console
                try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
                string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                bool quiet = args.Any(a => a.Equals("--quiet", StringComparison.OrdinalIgnoreCase));
                switch (cmd)
                {
                    case "render": return Render(args);
                    case "install":
                    {
                        bool inProcess = args.Any(a => a.Equals("--in-process", StringComparison.OrdinalIgnoreCase));
                        int rc = IsElevated() ? Install(inProcess) : NeedAdmin();
                        return Finish(rc, quiet);
                    }
                    case "uninstall":
                    {
                        int rc = IsElevated() ? Uninstall() : NeedAdmin();
                        return Finish(rc, quiet);
                    }
                    case "shelltest": return ShellTest(args);
                    case "cachetest": return CacheTest(args);
                    case "status": return Status();
                    case "cocreate":
                    {
                        var t = Type.GetTypeFromCLSID(new Guid(ThumbnailProvider.ClsidString));
                        var o = Activator.CreateInstance(t);
                        Console.WriteLine("CoCreateInstance OK: " + (o is IThumbnailProvider ? "IThumbnailProvider" : o.GetType().FullName));
                        return 0;
                    }
                    case "comtest":
                    {
                        // exactly what Explorer does: CoCreate -> IInitializeWithStream(file stream) -> GetThumbnail(cx)
                        var t = Type.GetTypeFromCLSID(new Guid(ThumbnailProvider.ClsidString));
                        var o = Activator.CreateInstance(t);
                        System.Runtime.InteropServices.ComTypes.IStream stm;
                        SHCreateStreamOnFileEx(Path.GetFullPath(args[1]), 0 /*STGM_READ*/, 0, false, IntPtr.Zero, out stm);
                        ((IInitializeWithStream)o).Initialize(stm, 0);
                        IntPtr hbm; WTS_ALPHATYPE alpha;
                        ((IThumbnailProvider)o).GetThumbnail(uint.Parse(args.Length > 3 ? args[3] : "768"), out hbm, out alpha);
                        using (var img = Image.FromHbitmap(hbm)) { img.Save(args[2], ImageFormat.Png); Console.WriteLine("COM provider OK: " + img.Width + "x" + img.Height + " alpha=" + alpha); }
                        DeleteObject(hbm);
                        return 0;
                    }
                    case "colors":
                        Console.WriteLine(string.Join(" ", new[] { 0, 50, 100, 185, 220, 300, 400, 500, 550, 600, 750, 900, 1200 }
                            .Select(s => { var c = SpeedColorProbe.Get(s); return s + ":#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); })));
                        return 0;
                    default:
                        Console.WriteLine("FunscriptThumbTool " + ProductVersion);
                        Console.WriteLine("  render <in.funscript> <out.png> [maxSide=1380] [--light|--dark|--bg=RRGGBB]");
                        Console.WriteLine("                                                   render with the same code Explorer uses");
                        Console.WriteLine("  install [--in-process] [--quiet]                 register machine-wide (UAC prompt)");
                        Console.WriteLine("  uninstall [--quiet]                              remove everything (UAC prompt)");
                        Console.WriteLine("  status                                           show registration state");
                        Console.WriteLine("  shelltest <file> <out.png> [size=512]            ask Windows Shell for the thumbnail");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex);
                return 2;
            }
        }

        /// <summary>
        /// Keeps the console readable when the tool was launched by a double-click (Explorer /
        /// Settings) rather than from an existing shell, then lets go of any directory that could
        /// only be removed after this process exits.
        /// </summary>
        static int Finish(int rc, bool quiet)
        {
            if (!quiet && !Console.IsInputRedirected && !Console.IsOutputRedirected)
            {
                Console.WriteLine();
                Console.WriteLine(rc == 0 ? "Done. Press any key to close this window..." : "Failed with code " + rc + ". Press any key to close this window...");
                try { Console.ReadKey(true); } catch { }
            }
            foreach (var dir in PendingDelete) ScheduleTreeDelete(dir);
            return rc;
        }

        // ------------------------------------------------------------------ render
        static int Render(string[] args)
        {
            if (args.Length < 3) { Console.Error.WriteLine("usage: render <in> <out.png> [maxSide] [--light|--dark|--bg=RRGGBB]"); return 1; }
            int side = 1380;
            var o = new RenderOptions();
            for (int i = 3; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--bg=", StringComparison.OrdinalIgnoreCase))
                {
                    string hex = a.Substring(5).TrimStart('#');
                    if (hex.Length == 6)
                        o.Background = Color.FromArgb(
                            Convert.ToInt32(hex.Substring(0, 2), 16),
                            Convert.ToInt32(hex.Substring(2, 2), 16),
                            Convert.ToInt32(hex.Substring(4, 2), 16));
                }
                else if (a.Equals("--light", StringComparison.OrdinalIgnoreCase)) o.Background = RenderOptions.LightPage;
                else if (a.Equals("--dark", StringComparison.OrdinalIgnoreCase)) o.Background = RenderOptions.DarkPage;
                else { int v; if (int.TryParse(a, out v)) side = v; }
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = FunscriptDoc.Parse(ThumbnailProvider.DecodeText(File.ReadAllBytes(args[1])));
            using (var bmp = HeatmapRenderer.RenderFit(doc, side, o))
            {
                bmp.Save(args[2], ImageFormat.Png);
                Console.WriteLine("{0}  {1}x{2}  axes={3}  duration={4}ms  bg=#{5:x2}{6:x2}{7:x2}  ({8} ms)",
                    args[2], bmp.Width, bmp.Height, 1 + doc.Channels.Count, doc.DurationMs,
                    o.Background.R, o.Background.G, o.Background.B, sw.ElapsedMilliseconds);
            }
            return 0;
        }

        // ------------------------------------------------------------------ install / uninstall (HKLM)
        static RegistryKey Hklm() { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); }

        // Where the DLL lives. Explorer extracts thumbnails with the *browsing* user's token, and
        // the installer may well have been run by a different account, so the DLL has to be
        // readable by every user on the machine -- never a profile folder.
        // %ProgramData% is the conventional machine-wide, world-readable location. Installing there
        // also means ONE install covers every account, which is what makes copying this folder to
        // another PC a single-step operation.
        static string ProgramDataRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FunscriptThumbnail");
        }

        // fallback only, for the (rare) case where ProgramData cannot be written
        static string PerUserRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunscriptThumbnail");
        }

        static string[] InstallRoots() { return new[] { ProgramDataRoot(), PerUserRoot() }; }

        // folders written by earlier builds that are no longer used
        static string[] LegacyRoots()
        {
            return new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FunscriptThumbnail") };
        }

        static string ResolveInstallRoot(out string note)
        {
            var programData = ProgramDataRoot();
            try
            {
                Directory.CreateDirectory(programData);
                // prove it is actually writable and world-readable before committing to it
                string probe = Path.Combine(programData, ".writecheck-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(probe);
                Directory.Delete(probe, true);
                note = "machine-wide, readable by every user";
                return programData;
            }
            catch (Exception ex)
            {
                note = "per-user fallback -- " + ex.GetType().Name + " writing to " + programData;
                return PerUserRoot();
            }
        }

        static string[] SafeDirs(string parent, string pattern)
        {
            try { return Directory.Exists(parent) ? Directory.GetDirectories(parent, pattern) : new string[0]; }
            catch { return new string[0]; }
        }

        static bool TryDeleteTree(string path)
        {
            try { if (Directory.Exists(path)) { Directory.Delete(path, true); return true; } }
            catch { /* still mapped into a running dllhost/explorer; the next install retries */ }
            return false;
        }

        // only remove a legacy folder if it is genuinely ours and holds nothing but our build dirs
        static bool TryDeleteLegacyRoot(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return false;
                var entries = Directory.GetFileSystemEntries(path);
                bool ours = entries.Length == 0;
                foreach (var e in entries)
                {
                    string name = Path.GetFileName(e);
                    if (Directory.Exists(e) && name.StartsWith("bin-", StringComparison.OrdinalIgnoreCase)) { ours = true; continue; }
                    if (string.Equals(name, "FunscriptThumbnail.dll", StringComparison.OrdinalIgnoreCase)) { ours = true; continue; }
                    return false;   // unknown content: leave it alone
                }
                if (!ours) return false;
                Directory.Delete(path, true);
                return true;
            }
            catch { return false; }
        }

        static int Install(bool inProcess)
        {
            var asm = typeof(ThumbnailProvider).Assembly;
            string src = asm.Location;
            string rootNote;
            string root = ResolveInstallRoot(out rootNote);
            // a fresh folder per install: Explorer's COM surrogate may still hold the previous DLL open
            string dir = Path.Combine(root, "bin-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(src));
            File.Copy(src, dest, true);
            string codeBase = new Uri(dest).AbsoluteUri;
            string version = asm.GetName().Version.ToString();

            // Keep a copy of ourselves next to the DLL. The Uninstall button in
            // Settings -> Apps points here, so uninstalling still works after the user throws away
            // the folder they unzipped from -- which is the normal way this thing gets installed.
            // It has to sit in the SAME folder as FunscriptThumbnail.dll: the tool loads that
            // assembly on startup, so a copy at the root of the install tree would not even start.
            string uninstallerExe = Path.Combine(dir, Path.GetFileName(EntryExePath()));
            try
            {
                string me = EntryExePath();
                if (!string.Equals(me, uninstallerExe, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(me, uninstallerExe, true);
                    if (File.Exists(me + ".config")) File.Copy(me + ".config", uninstallerExe + ".config", true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  note: could not place the uninstaller in " + dir + " (" + ex.GetType().Name + ")");
                uninstallerExe = EntryExePath();
            }

            // the development build of 1.1.0 briefly parked an exe at the root, where it could not
            // start; drop those leftovers so nobody clicks a broken copy
            foreach (var leftover in new[]
            {
                Path.Combine(root, Path.GetFileName(EntryExePath())),
                Path.Combine(root, Path.GetFileName(EntryExePath()) + ".config"),
            })
            {
                try
                {
                    if (File.Exists(leftover) && !string.Equals(leftover, EntryExePath(), StringComparison.OrdinalIgnoreCase))
                        File.Delete(leftover);
                }
                catch { }
            }

            string iconPath = Path.Combine(root, "funscript.ico");
            try { IconWriter.Write(iconPath); }
            catch { iconPath = null; }

            // Debug builds registered the handler under HKCU. A HKCU CLSID shadows the machine-wide
            // one, so a leftover entry would silently keep Windows pointed at the dead old path.
            using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            {
                hkcu.DeleteSubKeyTree(@"Software\Classes\CLSID\" + Clsid, false);
                hkcu.DeleteSubKeyTree(@"Software\Classes\.funscript\ShellEx\" + ThumbHandlerIid, false);
            }

            using (var hk = Hklm())
            {
                using (var k = hk.CreateSubKey(@"Software\Classes\CLSID\" + Clsid))
                {
                    k.SetValue("", FriendlyName);
                    // Default: let Windows host the handler in the isolated thumbnail surrogate
                    // (dllhost.exe). That keeps a rendering or parsing bug from taking explorer.exe
                    // down with it, and it is verified working for this handler on Windows 10/11.
                    // "install --in-process" forces the old in-explorer hosting for troubleshooting.
                    // Either way, never leave a stale value behind from a previous install.
                    if (inProcess) k.SetValue("DisableProcessIsolation", 1, RegistryValueKind.DWord);
                    else k.DeleteValue("DisableProcessIsolation", false);
                    using (var p = k.CreateSubKey("ProgId")) p.SetValue("", "FunscriptThumbnail.Provider");
                    using (var c = k.CreateSubKey(@"Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}")) { }
                    using (var inproc = k.CreateSubKey("InprocServer32"))
                    {
                        inproc.SetValue("", "mscoree.dll");
                        inproc.SetValue("ThreadingModel", "Both");
                        WriteManaged(inproc, asm.FullName, codeBase);
                        using (var v = inproc.CreateSubKey(version)) WriteManaged(v, asm.FullName, codeBase);
                    }
                }
                using (var k = hk.CreateSubKey(@"Software\Classes\.funscript\ShellEx\" + ThumbHandlerIid)) k.SetValue("", Clsid);
                using (var k = hk.CreateSubKey(@"Software\Classes\SystemFileAssociations\.funscript\ShellEx\" + ThumbHandlerIid)) k.SetValue("", Clsid);
                using (var k = hk.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved")) k.SetValue(Clsid, FriendlyName);

                // no app-icon overlay / no frame on the thumbnail (only if not configured already)
                using (var marker = hk.CreateSubKey(MarkerKey))
                using (var ext = hk.CreateSubKey(@"Software\Classes\.funscript"))
                {
                    marker.SetValue("InstallDir", dir);
                    if (ext.GetValue("TypeOverlay") == null) { ext.SetValue("TypeOverlay", ""); marker.SetValue("SetTypeOverlay", 1); }
                    if (ext.GetValue("Treatment") == null) { ext.SetValue("Treatment", 0, RegistryValueKind.DWord); marker.SetValue("SetTreatment", 1); }
                }

                WriteUninstallEntry(hk, root, uninstallerExe, iconPath);
            }

            // best-effort cleanup of older copies, in every root we have ever used
            foreach (var r in InstallRoots())
            {
                foreach (var old in SafeDirs(r, "bin-*").Where(d => !string.Equals(d, dir, StringComparison.OrdinalIgnoreCase)))
                {
                    TryDeleteTree(old);
                }
            }
            foreach (var legacy in LegacyRoots())
            {
                if (TryDeleteLegacyRoot(legacy)) Console.WriteLine("  cleaned up legacy folder " + legacy);
            }

            SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero);
            Console.WriteLine("Installed for all users on this PC.");
            Console.WriteLine("  CLSID     " + Clsid);
            Console.WriteLine("  DLL       " + dest);
            Console.WriteLine("  Location  " + rootNote);
            Console.WriteLine("  Hosting   " + (inProcess ? "in-process (--in-process)" : "isolated surrogate (dllhost), the Windows default"));
            Console.WriteLine("  Uninstall 设置 → 应用 → 安装的应用 里可以卸载（也可以运行 \"" + uninstallerExe + "\" uninstall）");
            return 0;
        }

        /// <summary>
        /// The machine-wide Add/Remove Programs entry. Windows only lists an app when it finds a
        /// key here with a DisplayName (and without SystemComponent), and it only offers an
        /// "Uninstall" button when UninstallString is set. NoModify/NoRepair keep it to that one
        /// button -- there is nothing here for Windows to repair or change.
        /// </summary>
        static void WriteUninstallEntry(RegistryKey hk, string root, string exe, string icon)
        {
            string displayIcon = !string.IsNullOrEmpty(icon) && File.Exists(icon) ? icon : exe;
            using (var k = hk.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", ProductName);
                k.SetValue("DisplayVersion", ProductVersion);
                k.SetValue("Publisher", Publisher);
                k.SetValue("InstallLocation", root);
                k.SetValue("UninstallString", "\"" + exe + "\" uninstall");
                k.SetValue("QuietUninstallString", "\"" + exe + "\" uninstall --quiet");
                k.SetValue("DisplayIcon", displayIcon);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", TreeSizeKb(root), RegistryValueKind.DWord);
                k.SetValue("URLInfoAbout", ProjectUrl);
                k.SetValue("Comments", ProductComments);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("WindowsInstaller", 0, RegistryValueKind.DWord);
                // SystemComponent=1 is exactly what hides an entry from Settings -> Apps
                k.DeleteValue("SystemComponent", false);
            }
        }

        static int TreeSizeKb(string dir)
        {
            try
            {
                long total = 0;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                return (int)Math.Max(1, Math.Min(int.MaxValue, total / 1024));
            }
            catch { return 1; }
        }

        static string EntryExePath()
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            if (asm != null && !string.IsNullOrEmpty(asm.Location)) return asm.Location;
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        /// <summary>
        /// A folder holding the running exe cannot be deleted by that exe, so hand the job to a
        /// short-lived hidden shell once this process has exited.
        /// </summary>
        static void ScheduleTreeDelete(string dir)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"" + dir + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch { }
        }

        static void WriteManaged(RegistryKey k, string assemblyFullName, string codeBase)
        {
            k.SetValue("Class", typeof(ThumbnailProvider).FullName);
            k.SetValue("Assembly", assemblyFullName);
            k.SetValue("RuntimeVersion", "v4.0.30319");
            k.SetValue("CodeBase", codeBase);
        }

        static int Uninstall()
        {
            using (var hk = Hklm())
            {
                hk.DeleteSubKeyTree(@"Software\Classes\CLSID\" + Clsid, false);
                DeleteIfOurs(hk, @"Software\Classes\.funscript\ShellEx\" + ThumbHandlerIid);
                DeleteIfOurs(hk, @"Software\Classes\SystemFileAssociations\.funscript\ShellEx\" + ThumbHandlerIid);
                using (var k = hk.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved", true))
                    if (k != null) k.DeleteValue(Clsid, false);

                using (var marker = hk.OpenSubKey(MarkerKey))
                {
                    if (marker != null)
                    {
                        using (var ext = hk.OpenSubKey(@"Software\Classes\.funscript", true))
                        {
                            if (ext != null)
                            {
                                if (marker.GetValue("SetTypeOverlay") != null) ext.DeleteValue("TypeOverlay", false);
                                if (marker.GetValue("SetTreatment") != null) ext.DeleteValue("Treatment", false);
                            }
                        }
                    }
                }
                hk.DeleteSubKeyTree(MarkerKey, false);
                // the Settings -> Apps entry goes too, otherwise Windows would keep listing an app
                // whose Uninstall button points at a file that is no longer there
                hk.DeleteSubKeyTree(UninstallKey, false);
                PruneEmpty(hk, @"Software\Classes\.funscript\ShellEx");
                PruneEmpty(hk, @"Software\Classes\.funscript");
                PruneEmpty(hk, @"Software\Classes\SystemFileAssociations\.funscript\ShellEx");
                PruneEmpty(hk, @"Software\Classes\SystemFileAssociations\.funscript");
            }
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

            // also clear any HKCU shadow left behind by the debug builds
            using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            {
                hkcu.DeleteSubKeyTree(@"Software\Classes\CLSID\" + Clsid, false);
                hkcu.DeleteSubKeyTree(@"Software\Classes\.funscript\ShellEx\" + ThumbHandlerIid, false);
            }

            foreach (var legacy in LegacyRoots())
                if (TryDeleteLegacyRoot(legacy)) Console.WriteLine("Removed " + legacy);

            foreach (var root in InstallRoots())
            {
                if (!Directory.Exists(root)) continue;
                if (TryDeleteTree(root)) Console.WriteLine("Removed " + root);
                else
                {
                    // typically because this very exe lives in there (that is how the Settings
                    // uninstall button runs us); Finish() hands it to a helper after we exit
                    Console.WriteLine("Removing " + root + " ...");
                    PendingDelete.Add(root);
                }
            }
            Console.WriteLine("Uninstalled.");
            return 0;
        }

        static void DeleteIfOurs(RegistryKey hk, string path)
        {
            using (var k = hk.OpenSubKey(path))
            {
                if (k == null) return;
                if (!string.Equals(k.GetValue("") as string, Clsid, StringComparison.OrdinalIgnoreCase)) return;
            }
            hk.DeleteSubKeyTree(path, false);
        }

        static void PruneEmpty(RegistryKey hk, string path)
        {
            using (var k = hk.OpenSubKey(path))
            {
                if (k == null) return;
                if (k.SubKeyCount > 0 || k.ValueCount > 0) return;
            }
            hk.DeleteSubKey(path, false);
        }

        static int Status()
        {
            using (var hk = Hklm())
            using (var k = hk.OpenSubKey(@"Software\Classes\CLSID\" + Clsid + @"\InprocServer32"))
            using (var sfa = hk.OpenSubKey(@"Software\Classes\SystemFileAssociations\.funscript\ShellEx\" + ThumbHandlerIid))
            using (var e = hk.OpenSubKey(@"Software\Classes\.funscript\ShellEx\" + ThumbHandlerIid))
            using (var c = hk.OpenSubKey(@"Software\Classes\CLSID\" + Clsid))
            {
                Console.WriteLine("CLSID registered : " + (k != null ? "yes" : "no"));
                Console.WriteLine(".funscript hook  : " + (e != null ? "yes  " + e.GetValue("") : "no"));
                Console.WriteLine("SystemFileAssoc. : " + (sfa != null ? "yes" : "no"));
                if (c != null)
                    Console.WriteLine("Hosting          : " + (Convert.ToInt32(c.GetValue("DisableProcessIsolation", 0)) == 1
                        ? "in-process, inside the calling process (DisableProcessIsolation=1)"
                        : "isolated surrogate dllhost.exe (Windows default)"));
                if (k != null)
                {
                    var codeBase = k.GetValue("CodeBase") as string;
                    Console.WriteLine("CodeBase         : " + codeBase);
                    var uri = codeBase != null && codeBase.StartsWith("file:///") ? codeBase.Substring(8) : null;
                    var path = uri != null ? Uri.UnescapeDataString(uri).Replace('/', '\\') : null;
                    Console.WriteLine("DLL exists       : " + (path != null && System.IO.File.Exists(path)));
                    if (path != null && !System.IO.File.Exists(path))
                        Console.WriteLine("  ^ the registered file is gone: run install again");
                }
                using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var dir = hkcu.OpenSubKey(@"Software\Classes\CLSID\" + Clsid))
                    if (dir != null) Console.WriteLine("stale HKCU CLSID : present (shadowing!)");
            }

            using (var hk = Hklm())
            using (var u = hk.OpenSubKey(UninstallKey))
            {
                Console.WriteLine("Apps entry       : " + (u != null ? "yes  " + u.GetValue("DisplayName") : "no  (Settings -> Apps will not list it)"));
                if (u != null)
                {
                    var cmd = u.GetValue("UninstallString") as string;
                    Console.WriteLine("uninstall cmd    : " + cmd);
                    if (!string.IsNullOrEmpty(cmd))
                    {
                        var exe = cmd.Trim().Trim('"');
                        int q = exe.IndexOf('"');
                        if (q > 0) exe = exe.Substring(0, q);
                        exe = exe.Trim('"');
                        Console.WriteLine("uninstaller file : " + (System.IO.File.Exists(exe) ? "exists" : "MISSING  <-- the Settings button would fail"));
                    }
                }
            }

            foreach (var legacy in LegacyRoots())
                if (Directory.Exists(legacy)) Console.WriteLine("legacy folder    : " + legacy + "  (inert; the installer cleans it)");
            foreach (var root in InstallRoots())
                Console.WriteLine("install root     : " + root + "  (" + SafeDirs(root, "bin-*").Length + " build folder(s))");
            return 0;
        }

        // ------------------------------------------------------------------ shell test
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateStreamOnFileEx(string file, int grfMode, int attrs, bool create, IntPtr template,
            out System.Runtime.InteropServices.ComTypes.IStream stream);

        [ComImport, Guid("50EF4544-AC9F-4A8E-B21B-8A26180DB13F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IThumbnailCache
        {
            [PreserveSig] int GetThumbnail([MarshalAs(UnmanagedType.Interface)] object pShellItem, int cxyRequestedThumbSize, uint flags,
                [MarshalAs(UnmanagedType.Interface)] out object ppvThumb, out uint pOutFlags, out WTS_THUMBNAILID pThumbnailID);
            [PreserveSig] int GetThumbnailByID(WTS_THUMBNAILID id, int cxy, out object ppvThumb, out uint flags);
        }

        [ComImport, Guid("091162a4-bc96-411f-aae8-c5122cd03363"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISharedBitmap
        {
            [PreserveSig] int GetBitmap(out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WTS_THUMBNAILID { public long a, b; }



        static int CacheTest(string[] args)
        {
            if (args.Length < 3) { Console.Error.WriteLine("usage: cachetest <file> <out.png> [size]"); return 1; }
            int size = args.Length > 3 ? int.Parse(args[3]) : 768;
            var t = Type.GetTypeFromCLSID(new Guid("50EF4544-AC9F-4A8E-B21B-8A26180DB13F"));   // LocalThumbnailCache
            var cache = (IThumbnailCache)Activator.CreateInstance(t);
            IShellItemImageFactory item;
            SHCreateItemFromParsingName(Path.GetFullPath(args[1]), IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out item);
            object thumb; uint outFlags; WTS_THUMBNAILID id;
            int hr = cache.GetThumbnail(item, size, 0x1 /*WTS_FORCEEXTRACTION*/, out thumb, out outFlags, out id);
            if (hr != 0) { Console.Error.WriteLine("LocalThumbnailCache.GetThumbnail failed: 0x" + hr.ToString("X8")); return 3; }
            IntPtr hbm;
            ((ISharedBitmap)thumb).GetBitmap(out hbm);
            try
            {
                using (var img = Image.FromHbitmap(hbm))
                {
                    img.Save(args[2], ImageFormat.Png);
                    Console.WriteLine("thumbnail cache OK: " + img.Width + "x" + img.Height + " flags=" + outFlags + " -> " + args[2]);
                }
            }
            finally { DeleteObject(hbm); }
            return 0;
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        static int ShellTest(string[] args)
        {
            if (args.Length < 3) { Console.Error.WriteLine("usage: shelltest <file> <out.png> [size]"); return 1; }
            // also log to <out>.log so the result is visible when launched in another (non-elevated) console
            var log = new StringWriter();
            var origOut = Console.Out; var origErr = Console.Error;
            Console.SetOut(new TeeWriter(origOut, log)); Console.SetError(new TeeWriter(origErr, log));
            try { return ShellTestCore(args); }
            catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }
            finally
            {
                Console.SetOut(origOut); Console.SetError(origErr);
                File.WriteAllText(args[2] + ".log", "elevated=" + IsElevated() + "\r\n" + log);
            }
        }

        static int NeedAdmin()
        {
            // app.manifest declares requireAdministrator, so Windows should have raised a UAC
            // prompt before we ever got here. This only fires if the manifest was stripped.
            Console.Error.WriteLine("Administrator rights are required (the handler is registered machine-wide).");
            return 5;
        }

        static bool IsElevated()
        {
            var p = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        sealed class TeeWriter : TextWriter
        {
            readonly TextWriter _a, _b;
            public TeeWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }
            public override System.Text.Encoding Encoding { get { return _a.Encoding; } }
            public override void Write(char value) { _a.Write(value); _b.Write(value); }
            public override void Write(string value) { _a.Write(value); _b.Write(value); }
            public override void WriteLine(string value) { _a.WriteLine(value); _b.WriteLine(value); }
        }

        static int ShellTestCore(string[] args)
        {
            int size = args.Length > 3 ? int.Parse(args[3]) : 512;
            IShellItemImageFactory f;
            SHCreateItemFromParsingName(Path.GetFullPath(args[1]), IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
            IntPtr hbm;
            int hr = f.GetImage(new SIZE(size, size), 0x8 /*SIIGBF_THUMBNAILONLY*/, out hbm);
            if (hr != 0) { Console.Error.WriteLine("GetImage(THUMBNAILONLY) failed: 0x" + hr.ToString("X8") + "  (no thumbnail provider answered)"); return 3; }
            try
            {
                using (var img = Image.FromHbitmap(hbm)) { img.Save(args[2], ImageFormat.Png); Console.WriteLine("shell thumbnail OK: " + img.Width + "x" + img.Height + " -> " + args[2]); }
            }
            finally { DeleteObject(hbm); }
            return 0;
        }
    }
}
