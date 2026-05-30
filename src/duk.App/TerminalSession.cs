using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace duk.App;

public partial class TerminalSession
{
    private readonly Process _process;
    private readonly MainWindow _window;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    public string ShellName { get; }
    public string OutputBuffer { get; internal set; } = "";

    public TerminalSession(string shell, string workDir, MainWindow window)
    {
        _window = window;
        ShellName = shell switch
        {
            "powershell.exe" => "pwsh",
            "pwsh.exe"       => "pwsh (Core)",
            _                => "cmd"
        };

        var enc = shell.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                  shell.Contains("pwsh",        StringComparison.OrdinalIgnoreCase)
                  ? Encoding.UTF8
                  : Encoding.GetEncoding(932);

        _process = new Process
        {
            StartInfo = new ProcessStartInfo(shell)
            {
                WorkingDirectory       = workDir,
                RedirectStandardOutput = true,
                RedirectStandardInput  = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = enc,
                StandardErrorEncoding  = enc,
            },
            EnableRaisingEvents = true
        };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Append(e.Data + "\n");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Append(e.Data + "\n");
        };
        _process.Exited += (_, _) => Append("\n[プロセスが終了しました]\n");

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Append($"duk Terminal [{ShellName}] - {workDir}\n\n");
    }

    public void SendCommand(string cmd)
    {
        if (!string.IsNullOrEmpty(cmd))
        {
            _history.Insert(0, cmd);
            _historyIndex = -1;
        }
        Append($"> {cmd}\n");
        try { _process.StandardInput.WriteLine(cmd); }
        catch { }
    }

    public void SendCtrlC()
    {
        try { _process.StandardInput.Write("\x03"); }
        catch { }
        Append("^C\n");
    }

    public void ClearOutput() => OutputBuffer = "";

    public string HistoryUp()
    {
        if (_history.Count == 0) return "";
        _historyIndex = Math.Min(_historyIndex + 1, _history.Count - 1);
        return _history[_historyIndex];
    }

    public string HistoryDown()
    {
        if (_historyIndex <= 0) { _historyIndex = -1; return ""; }
        return _history[--_historyIndex];
    }

    public void Kill()
    {
        try { _process.Kill(); } catch { }
    }

    private void Append(string text)
    {
        OutputBuffer += text;
        _window.AppendToTerminal(this, text);
    }

    // ANSIエスケープコードを除去
    [GeneratedRegex(@"\x1B\[[0-9;]*[mKHJA-Za-z]|\x1B\[[0-9;]*[mKHJ]|\x0D")]
    private static partial Regex AnsiRegex();

    private static string StripAnsi(string text) => AnsiRegex().Replace(text, "");
}
