using System.Text;
using System.Windows;

namespace duk.App;

public partial class App : Application
{
    public static string? StartupJoinCode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // duk:// URI スキームをWindowsに登録
        if (!UriSchemeRegistrar.IsRegistered())
            UriSchemeRegistrar.Register();

        // コマンドライン引数から duk://join/ROOM-CODE を解析
        if (e.Args.Length > 0 && e.Args[0].StartsWith("duk://", StringComparison.OrdinalIgnoreCase))
            StartupJoinCode = UriSchemeRegistrar.ParseRoomCode(e.Args[0]);

        base.OnStartup(e);
    }
}
