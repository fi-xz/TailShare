using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace TailShare;

/// <summary>Device picker; after a pick it switches to a "sending" state until the caller closes it.</summary>
public sealed partial class PeerPickerWindow : Window
{
    private readonly TaskCompletionSource<Peer?> _pick = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly IntPtr _hwnd;
    private bool _sending;
    private bool _closed;

    public PeerPickerWindow(IReadOnlyList<Peer> peers, string summary, string? preferredName)
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        SystemBackdrop = new MicaBackdrop();
        // Keep the caption in step with the app theme so the theme-specific icon stays visible.
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        UpdateTitleBarIcon();
        Root.ActualThemeChanged += (_, _) => UpdateTitleBarIcon();
        SummaryText.Text = summary;
        PeerList.ItemsSource = peers;
        PeerList.SelectedItem =
            peers.FirstOrDefault(p => p.IsOnline && p.Name == preferredName) ??
            peers.FirstOrDefault(p => p.IsOnline);
        // SelectionChanged doesn't fire for this initial selection.
        SendButton.IsEnabled = PeerList.SelectedItem is Peer { IsOnline: true };

        // handledEventsToo: ListView may swallow Enter.
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown), true);
        Closed += (_, _) =>
        {
            _closed = true;
            _pick.TrySetResult(null);
            _cancel.Cancel();
        };

        SizeAndCenter(400, 500);
    }

    /// <summary>Cancelled when the user cancels or closes the window while sending.</summary>
    public CancellationToken CancellationToken => _cancel.Token;

    /// <summary>Shows the window; completes with the chosen peer, or null if dismissed.</summary>
    public Task<Peer?> PickAsync()
    {
        Activate();
        SetForegroundWindow(_hwnd);
        PeerList.Focus(FocusState.Programmatic);
        return _pick.Task;
    }

    public void ShowSending(Peer peer, int fileCount)
    {
        _sending = true;
        PeerList.Visibility = Visibility.Collapsed;
        SendButton.Visibility = Visibility.Collapsed;
        SendingPanel.Visibility = Visibility.Visible;
        SendingText.Text = $"Sending {(fileCount == 1 ? "1 file" : $"{fileCount} files")} to {peer.Name}…";
    }

    public void CloseIfOpen()
    {
        if (_closed) return;
        _closed = true;
        Close();
    }

    private void TrySend()
    {
        if (!_sending && PeerList.SelectedItem is Peer { IsOnline: true } peer)
            _pick.TrySetResult(peer);
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => TrySend();

    private void PeerList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => TrySend();

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            TrySend();
            e.Handled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_sending)
        {
            _pick.TrySetResult(null);
            return;
        }
        CancelButton.IsEnabled = false;
        SendingText.Text = "Cancelling…";
        _cancel.Cancel();
    }

    private void PeerList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SendButton.IsEnabled = PeerList.SelectedItem is Peer { IsOnline: true };

    /// <summary>Title bar icon matching the window's theme (the taskbar uses the package logo instead).</summary>
    private void UpdateTitleBarIcon()
    {
        if (AppIcons.IcoPath(Root.ActualTheme == ElementTheme.Light) is { } path)
            AppWindow.SetIcon(path);
    }

    private void SizeAndCenter(int width, int height)
    {
        var scale = GetDpiForWindow(_hwnd) / 96.0;
        var size = new SizeInt32((int)(width * scale), (int)(height * scale));
        AppWindow.Resize(size);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (area.Height - size.Height) / 2));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
