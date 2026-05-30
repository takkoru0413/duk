using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace duk.App;

public partial class SessionDialog : Window
{
    private readonly SessionManager _session;
    private readonly MainWindow     _mainWindow;
    private string? _pendingJoinCode;

    public SessionDialog(MainWindow mainWindow, SessionManager session, string? joinCode = null)
    {
        InitializeComponent();
        _mainWindow    = mainWindow;
        _session       = session;
        _pendingJoinCode = joinCode;

        _session.Connected    += OnConnected;
        _session.Disconnected += OnDisconnected;
        _session.MemberJoined += OnMemberJoined;
        _session.MemberLeft   += OnMemberLeft;
        _session.TextChanged  += OnTextChanged;

        // リンクから開いた場合は自動でJoin
        if (joinCode != null)
        {
            RoomCodeBox.Text = joinCode;
            Loaded += async (_, _) => await DoJoin(joinCode);
        }
    }

    // ========== ボタン操作 ==========

    private async void CreateSession_Click(object sender, RoutedEventArgs e)
    {
        ShowConnecting("セッションを作成中...");
        try
        {
            var code = await _session.CreateSessionAsync(UserNameBox.Text.Trim());
            ShowHostPanel(code);
        }
        catch (Exception ex)
        {
            ShowInit();
            MessageBox.Show($"接続失敗:\n{ex.Message}\n\n※ シグナリングサーバーが未設定のため、\n現在はローカルでのみ動作します。",
                "接続エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            // オフラインでもルームコードだけ生成してUIを見せる（デモ用）
            ShowOfflineDemo();
        }
    }

    private async void JoinSession_Click(object sender, RoutedEventArgs e)
    {
        var code = RoomCodeBox.Text.Trim().ToUpper();
        if (code.Length < 3) return;
        await DoJoin(code);
    }

    private async Task DoJoin(string code)
    {
        ShowConnecting($"ルーム {code} に接続中...");
        try
        {
            await _session.JoinSessionAsync(code, UserNameBox.Text.Trim());
            ShowMemberPanel(code);
        }
        catch (Exception ex)
        {
            ShowInit();
            MessageBox.Show($"接続失敗:\n{ex.Message}", "接続エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Leave_Click(object sender, RoutedEventArgs e)
    {
        await _session.LeaveAsync();
        ShowInit();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(InviteLinkBox.Text);
        ((Button)sender).Content = "✓ コピー済";
    }

    private void RoomCodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) JoinSession_Click(sender, new RoutedEventArgs());
    }

    // ========== UI切替 ==========

    private void ShowInit()
    {
        InitPanel.Visibility       = Visibility.Visible;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        HostPanel.Visibility       = Visibility.Collapsed;
        MemberPanel.Visibility     = Visibility.Collapsed;
        LeaveBtn.Visibility        = Visibility.Collapsed;
    }

    private void ShowConnecting(string msg)
    {
        ConnectingText.Text        = msg;
        InitPanel.Visibility       = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Visible;
        HostPanel.Visibility       = Visibility.Collapsed;
        MemberPanel.Visibility     = Visibility.Collapsed;
    }

    private void ShowHostPanel(string code)
    {
        InitPanel.Visibility       = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        HostPanel.Visibility       = Visibility.Visible;
        MemberPanel.Visibility     = Visibility.Collapsed;
        LeaveBtn.Visibility        = Visibility.Visible;

        var link = $"{SessionManager.InviteBaseUrl}?room={code}";
        InviteLinkBox.Text    = link;
        RoomCodeDisplay.Text  = code;
        MembersList.Children.Clear();
        AddMember(UserNameBox.Text.Trim() + " (あなた)", "#4ec9b0");
    }

    private void ShowOfflineDemo()
    {
        var fakeCode = "ABC-123-XYZ";
        ShowHostPanel(fakeCode);
        InviteLinkBox.Text = $"（サーバー未設定）duk://join/{fakeCode}";
    }

    private void ShowMemberPanel(string code)
    {
        InitPanel.Visibility       = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        HostPanel.Visibility       = Visibility.Collapsed;
        MemberPanel.Visibility     = Visibility.Visible;
        LeaveBtn.Visibility        = Visibility.Visible;

        JoinedRoomText.Text = $"ルームに参加しました";
        JoinedRoomCode.Text = $"コード: {code}";
    }

    private void AddMember(string name, string colorHex = "#cccccc")
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock
        {
            Text = "●",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
            FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = name, Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            FontSize = 13, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center
        });
        MembersList.Children.Add(row);
    }

    // ========== イベントハンドラー ==========

    private void OnConnected(object? s, EventArgs e) =>
        Dispatcher.Invoke(() => _mainWindow.StatusSession.Text = "● 接続済");

    private void OnDisconnected(object? s, EventArgs e) =>
        Dispatcher.Invoke(() =>
        {
            _mainWindow.StatusSession.Text = "● オフライン";
            ShowInit();
        });

    private void OnMemberJoined(object? s, string name) =>
        Dispatcher.Invoke(() =>
        {
            AddMember(name);
            _mainWindow.StatusSession.Text = $"● {name} が参加";
        });

    private void OnMemberLeft(object? s, string name) =>
        Dispatcher.Invoke(() =>
        {
            // メンバーリストから削除（簡易）
            var toRemove = MembersList.Children.OfType<StackPanel>()
                .FirstOrDefault(sp => sp.Children.OfType<TextBlock>()
                    .Any(tb => tb.Text.Contains(name)));
            if (toRemove != null) MembersList.Children.Remove(toRemove);
        });

    private void OnTextChanged(object? s, string text) =>
        Dispatcher.Invoke(() =>
        {
            // テキスト変更を受信してエディターに反映（ループ防止）
            if (_mainWindow.Editor.Text != text)
                _mainWindow.Editor.Text = text;
        });

    protected override void OnClosed(EventArgs e)
    {
        _session.Connected    -= OnConnected;
        _session.Disconnected -= OnDisconnected;
        _session.MemberJoined -= OnMemberJoined;
        _session.MemberLeft   -= OnMemberLeft;
        _session.TextChanged  -= OnTextChanged;
        base.OnClosed(e);
    }
}
