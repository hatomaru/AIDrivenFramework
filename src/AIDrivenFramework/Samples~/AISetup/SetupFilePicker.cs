using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Optional OS file selection. Paths remain editable on platforms without this dialog.</summary>
static class SetupFilePicker
{
    public static bool Available => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
    public static string Choose(bool model, string title)
    {
        if (!Available) return null;
        IntPtr buffer = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var dialog = new OpenFileName
            {
                Size = Marshal.SizeOf(typeof(OpenFileName)), Owner = GetActiveWindow(),
                Filter = model ? "GGUF\0*.gguf\0\0" : "llama-server\0llama-server.exe\0Executable\0*.exe\0\0",
                File = buffer, MaxFile = 32768, Title = title, FilterIndex = 1,
                Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008 // Explorer, existing file/path, preserve working directory.
            };
            return GetOpenFileName(ref dialog) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenFileName
    {
        public int Size; public IntPtr Owner, Instance;
        public string Filter, CustomFilter; public int MaxCustomFilter, FilterIndex;
        public IntPtr File; public int MaxFile;
        public string FileTitle; public int MaxFileTitle;
        public string InitialDirectory, Title; public int Flags;
        public short FileOffset, FileExtension; public string DefaultExtension;
        public IntPtr CustomData, Hook; public string Template;
        public IntPtr Reserved; public int ReservedSize, FlagsEx;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetOpenFileName(ref OpenFileName value);
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
}
