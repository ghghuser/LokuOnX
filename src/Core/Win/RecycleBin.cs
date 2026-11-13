using System.Runtime.InteropServices;

namespace Recorder.Core.Win;

/// <summary>ファイルをごみ箱へ移す（Windows 専用）。確認やエラーのダイアログは出さず、結果だけ返す。</summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;     // これでごみ箱へ移る（無いと完全削除になる）
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct op);

    /// <summary>1つずつごみ箱へ移す。移せなかったファイルは「パス → 理由」で返す（全部成功なら空）。</summary>
    public static List<string> Send(IEnumerable<string> paths)
    {
        var failed = new List<string>();
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var op = new ShFileOpStruct
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",   // 区切りは \0、末尾は \0\0
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
            };
            int code;
            try { code = SHFileOperation(ref op); }
            catch (Exception ex) { failed.Add($"{Path.GetFileName(path)}（{ex.Message}）"); continue; }
            if (code != 0 || op.fAnyOperationsAborted || File.Exists(path))
                failed.Add(Loc.Get("recyclebin.s01", Path.GetFileName(path), code));
        }
        return failed;
    }
}
