using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace Synqora.Services;

/// <summary>Native notification-area icon. No taskbar injection or background polling.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly IntPtr window;
    private readonly Subclass callback;
    private readonly Action show, connect, disconnect, quit;
    private readonly Func<bool> minimize;
    private readonly uint explorerRestart;
    private Data data;
    public TrayIcon(IntPtr window, string iconPath, Action show, Action connect, Action disconnect, Action quit, Func<bool> minimize) {
        this.window = window; this.show = show; this.connect = connect; this.disconnect = disconnect; this.quit = quit; this.minimize = minimize;
        callback = Message;
        data = new Data { Size = (uint)Marshal.SizeOf<Data>(), Window = window, Id = 1, Flags = 1 | 2 | 4, Message = 0x8000 + 81, Icon = LoadImage(IntPtr.Zero, iconPath, 1, 32, 32, 0x10), Tip = "Synqora · companion ready", Info = "", InfoTitle = "" };
        explorerRestart = RegisterWindowMessage("TaskbarCreated");
        if (!SetWindowSubclass(window, callback, 83, UIntPtr.Zero)) throw new IOException("Windows could not create the tray control.");
        Add();
    }
    private void Add() { ShellNotifyIcon(0, ref data); data.Version = 4; ShellNotifyIcon(4, ref data); }
    public void Status(string value) { data.Tip = ("Synqora · " + value)[..Math.Min(127, ("Synqora · " + value).Length)]; ShellNotifyIcon(1, ref data); }
    private IntPtr Message(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr user) {
        if (message == explorerRestart) Add();
        if (message == 5 && wparam.ToUInt64() == 1 && minimize()) { showHidden?.Invoke(); }
        if (message == data.Message) {
            int action = (int)(lparam.ToInt64() & 0xffff);
            if (action is 0x400 or 0x401 or 0x203) show();
            if (action is 0x7b or 0x205) Menu();
        }
        return DefSubclassProc(hwnd, message, wparam, lparam);
    }
    public Action? showHidden { private get; set; }
    private void Menu() {
        var menu = CreatePopupMenu();
        try {
            AppendMenu(menu, 0, 1, "Open Synqora"); AppendMenu(menu, 0, 2, "Connect a phone"); AppendMenu(menu, 0, 3, "Disconnect"); AppendMenu(menu, 0x800, 0, ""); AppendMenu(menu, 0, 4, "Quit Synqora");
            GetCursorPos(out var point); SetForegroundWindow(window);
            switch (TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, window, IntPtr.Zero)) { case 1: show(); break; case 2: show(); connect(); break; case 3: disconnect(); break; case 4: quit(); break; }
        } finally { DestroyMenu(menu); }
    }
    public void Dispose() { ShellNotifyIcon(2, ref data); RemoveWindowSubclass(window, callback, 83); if (data.Icon != IntPtr.Zero) DestroyIcon(data.Icon); GC.KeepAlive(callback); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Data { public uint Size; public IntPtr Window; public uint Id, Flags, Message; public IntPtr Icon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip; public uint State, StateMask; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info; public uint Version; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle; public uint InfoFlags; public Guid Guid; public IntPtr Balloon; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate IntPtr Subclass(IntPtr h, uint m, UIntPtr w, IntPtr l, UIntPtr id, UIntPtr data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] private static extern bool ShellNotifyIcon(uint message, ref Data data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr h, Subclass proc, uint id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr h, Subclass proc, uint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr h, uint m, UIntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr h, string path, uint type, int x, int y, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr h, uint flags, uint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr h, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
}
