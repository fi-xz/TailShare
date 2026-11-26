using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TailShare;

// "Send with Tailscale" in the Windows 11 top-level File Explorer context menu.
//
// Out-of-proc COM server: package\AppxManifest.xml declares this CLSID as a com:ExeServer
// (TailShare.exe -Embedding) and points desktop4:FileExplorerContextMenus at it. Explorer starts
// the exe the first time a menu is built, and calls GetState/Invoke across processes (the shell
// interfaces involved all have system proxy/stubs). Invoke just launches a fresh
// "TailShare.exe <paths...>" (or "--paths-file <list>" for long selections), which runs the
// normal picker/send flow; the server then idles out.
//
// Why not an in-proc DLL (com:SurrogateServer): Explorer would load it into explorer.exe, which
// needs a native DLL (C++ / NativeAOT, neither buildable without the MSVC toolchain) or would
// drag the CoreCLR into Explorer via comhost (framework-dependent only, one CLR per process).

/// <summary>Hosts the <see cref="ExplorerCommand"/> class factory while Explorer needs it.</summary>
public static partial class ExplorerCommandServer
{
    public const string Clsid = "6B3C1C5E-8F7A-4E2D-9B1A-3D5E7F9A2C41";

    /// <summary>The server exits once no COM call has arrived for this long.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    private static long _lastActivity = Environment.TickCount64;

    internal static void Touch() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);

    public static bool IsServerInvocation(string[] args) =>
        args.Any(a => a.Equals("-Embedding", StringComparison.OrdinalIgnoreCase) ||
                      a.Equals("/Embedding", StringComparison.OrdinalIgnoreCase));

    public static int Run()
    {
        // Main is [STAThread]; serve from an MTA thread so calls arrive on RPC threads and no
        // message pump is needed.
        int exitCode = 0;
        var thread = new Thread(() => exitCode = Serve()) { IsBackground = false };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    private static int Serve()
    {
        var wrappers = new StrategyBasedComWrappers();
        nint factory = wrappers.GetOrCreateComInterfaceForObject(new ClassFactory(), CreateComInterfaceFlags.None);
        var clsid = new Guid(Clsid);
        int hr = Native.CoRegisterClassObject(clsid, factory, Native.CLSCTX_LOCAL_SERVER,
            Native.REGCLS_MULTIPLEUSE, out uint cookie);
        if (hr < 0)
        {
            Log.Write($"explorer-command: CoRegisterClassObject failed 0x{hr:X8}");
            Marshal.Release(factory);
            return hr;
        }
        Log.Write("explorer-command: server started");

        Touch();
        while (Environment.TickCount64 - Interlocked.Read(ref _lastActivity) < IdleTimeout.TotalMilliseconds)
            Thread.Sleep(1000);

        Native.CoRevokeClassObject(cookie);
        Marshal.Release(factory);
        Log.Write("explorer-command: server idle, exiting");
        return 0;
    }

    [GeneratedComClass]
    internal sealed partial class ClassFactory : IClassFactory
    {
        private static readonly StrategyBasedComWrappers Wrappers = new();

        public int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject)
        {
            ppvObject = 0;
            Touch();
            if (pUnkOuter != 0) return Native.CLASS_E_NOAGGREGATION;

            nint unk = Wrappers.GetOrCreateComInterfaceForObject(new ExplorerCommand(), CreateComInterfaceFlags.None);
            try
            {
                return Marshal.QueryInterface(unk, riid, out ppvObject);
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        public int LockServer(bool fLock)
        {
            Touch();
            return 0;
        }
    }
}

[GeneratedComClass]
internal sealed partial class ExplorerCommand : IExplorerCommand
{
    private const string Title = "Send with Tailscale";

    /// <summary>Above this, paths go through a temp list file instead of the command line (limit is 32767).</summary>
    private const int MaxCommandLineChars = 16_000;

    private static readonly Guid CanonicalName = new("E1F0B6A2-4C3D-4B8E-A5F7-2D9C8B1E6A30");

    public int GetTitle(IShellItemArray? items, out nint name)
    {
        ExplorerCommandServer.Touch();
        name = Marshal.StringToCoTaskMemUni(Title);
        return 0;
    }

    public int GetIcon(IShellItemArray? items, out nint icon)
    {
        ExplorerCommandServer.Touch();
        // Read on every call: the server stays alive across menus, and the theme can change meanwhile.
        var path = AppIcons.IcoPath(AppIcons.AppsUseLightTheme());
        if (path is null)
        {
            icon = 0;
            return Native.E_NOTIMPL;
        }
        icon = Marshal.StringToCoTaskMemUni(path);
        return 0;
    }

    public int GetToolTip(IShellItemArray? items, out nint infoTip)
    {
        infoTip = 0;
        return Native.E_NOTIMPL;
    }

    public int GetCanonicalName(out Guid guid)
    {
        guid = CanonicalName;
        return 0;
    }

    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        ExplorerCommandServer.Touch();
        // Folders are rejected by design (Taildrop sends files): hide unless at least one file is selected.
        state = Native.ECS_HIDDEN;
        try
        {
            if (items is not null && ForEachItem(items).Any(IsFile))
                state = Native.ECS_ENABLED;
            Log.Write($"explorer-command: GetState -> {(state == Native.ECS_ENABLED ? "shown" : "hidden")}");
        }
        catch (Exception ex)
        {
            Log.Write($"explorer-command: GetState failed: {ex.Message}");
        }
        return 0;
    }

    public int Invoke(IShellItemArray? items, nint bindCtx)
    {
        ExplorerCommandServer.Touch();
        try
        {
            var paths = items is null ? [] : ForEachItem(items).Select(FileSystemPath).OfType<string>().ToList();
            Log.Write($"explorer-command: Invoke with {paths.Count} item(s)");
            if (paths.Count > 0) Launch(paths);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write($"explorer-command: Invoke failed: {ex}");
            return ex.HResult < 0 ? ex.HResult : Native.E_FAIL;
        }
    }

    public int GetFlags(out uint flags)
    {
        flags = Native.ECF_DEFAULT;
        return 0;
    }

    public int EnumSubCommands(out nint enumerator)
    {
        enumerator = 0;
        return Native.E_NOTIMPL;
    }

    private static IEnumerable<IShellItem> ForEachItem(IShellItemArray items)
    {
        Marshal.ThrowExceptionForHR(items.GetCount(out uint count));
        for (uint i = 0; i < count; i++)
        {
            if (items.GetItemAt(i, out var item) >= 0 && item is not null)
                yield return item;
        }
    }

    /// <summary>A file-system file. Zips and other "browsable" files have SFGAO_FOLDER and SFGAO_STREAM.</summary>
    private static bool IsFile(IShellItem item)
    {
        const uint mask = Native.SFGAO_FILESYSTEM | Native.SFGAO_FOLDER | Native.SFGAO_STREAM;
        if (item.GetAttributes(mask, out uint attrs) < 0) return false;
        return (attrs & Native.SFGAO_FILESYSTEM) != 0 &&
               ((attrs & Native.SFGAO_FOLDER) == 0 || (attrs & Native.SFGAO_STREAM) != 0);
    }

    private static string? FileSystemPath(IShellItem item)
    {
        if (item.GetDisplayName(Native.SIGDN_FILESYSPATH, out nint psz) < 0 || psz == 0) return null;
        try { return Marshal.PtrToStringUni(psz); }
        finally { Marshal.FreeCoTaskMem(psz); }
    }

    private static void Launch(List<string> paths)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (paths.Sum(p => p.Length + 3) <= MaxCommandLineChars)
        {
            foreach (var p in paths) psi.ArgumentList.Add(p);
        }
        else
        {
            // Read and deleted by Program.ExpandPathsFile.
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "TailShare")).FullName;
            var list = Path.Combine(dir, $"paths-{Guid.NewGuid():N}.txt");
            File.WriteAllLines(list, paths);
            psi.ArgumentList.Add(Program.PathsFileSwitch);
            psi.ArgumentList.Add(list);
        }

        // Explorer is the foreground app; let the picker take focus.
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        using var process = Process.Start(psi);
        Log.Write($"explorer-command: launched pid {process?.Id} ({psi.ArgumentList.Count} arg(s))");
    }
}

#region COM interfaces (only the slots used are typed precisely; order must match the SDK headers)

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

[GeneratedComInterface]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
internal partial interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IShellItemArray? psiItemArray, out nint ppszName);
    [PreserveSig] int GetIcon(IShellItemArray? psiItemArray, out nint ppszIcon);
    [PreserveSig] int GetToolTip(IShellItemArray? psiItemArray, out nint ppszInfotip);
    [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
    [PreserveSig] int GetState(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);
    [PreserveSig] int Invoke(IShellItemArray? psiItemArray, nint pbc);
    [PreserveSig] int GetFlags(out uint pFlags);
    [PreserveSig] int EnumSubCommands(out nint ppEnum);
}

[GeneratedComInterface]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
internal partial interface IShellItemArray
{
    [PreserveSig] int BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppvOut);
    [PreserveSig] int GetPropertyStore(int flags, in Guid riid, out nint ppv);
    [PreserveSig] int GetPropertyDescriptionList(nint keyType, in Guid riid, out nint ppv);
    [PreserveSig] int GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int GetCount(out uint pdwNumItems);
    [PreserveSig] int GetItemAt(uint dwIndex, out IShellItem? ppsi);
    [PreserveSig] int EnumItems(out nint ppenumShellItems);
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    [PreserveSig] int BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);
    [PreserveSig] int GetParent(out nint ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out nint ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(nint psi, uint hint, out int piOrder);
}

#endregion

internal static partial class Native
{
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

    public const uint CLSCTX_LOCAL_SERVER = 0x4;
    public const uint REGCLS_MULTIPLEUSE = 1;

    public const uint ECS_ENABLED = 0x0;
    public const uint ECS_HIDDEN = 0x2;
    public const uint ECF_DEFAULT = 0x0;

    public const uint SFGAO_STREAM = 0x00400000;
    public const uint SFGAO_FOLDER = 0x20000000;
    public const uint SFGAO_FILESYSTEM = 0x40000000;
    public const uint SIGDN_FILESYSPATH = 0x80058000;

    public const int ASFW_ANY = -1;

    [LibraryImport("ole32.dll")]
    public static partial int CoRegisterClassObject(in Guid rclsid, nint pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    [LibraryImport("ole32.dll")]
    public static partial int CoRevokeClassObject(uint dwRegister);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int dwProcessId);
}
