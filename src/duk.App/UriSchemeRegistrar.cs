using Microsoft.Win32;

namespace duk.App;

public static class UriSchemeRegistrar
{
    private const string SchemeKey = @"Software\Classes\duk";

    public static void Register()
    {
        var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
        using var key = Registry.CurrentUser.CreateSubKey(SchemeKey);
        key.SetValue("", "URL:duk Protocol");
        key.SetValue("URL Protocol", "");
        using var cmd = key.CreateSubKey(@"shell\open\command");
        cmd.SetValue("", $"\"{exePath}\" \"%1\"");
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SchemeKey);
        return key?.GetValue("URL Protocol") != null;
    }

    // duk://join/ABC-123-XYZ → "ABC-123-XYZ"
    public static string? ParseRoomCode(string uri)
    {
        try
        {
            var u = new Uri(uri);
            if (u.Scheme != "duk") return null;
            if (u.Host != "join")  return null;
            return u.AbsolutePath.TrimStart('/');
        }
        catch { return null; }
    }
}
