using System.Text;
using System.Windows;

namespace duk.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Shift-JIS (CP932) などのエンコーディングを有効化
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        base.OnStartup(e);
    }
}
