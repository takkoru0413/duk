using System.IO;
using System.Reflection;
using System.Windows;
using duk.Core;

namespace duk.App;

public class ExtensionContext(MainWindow window) : IExtensionContext
{
    public string?  GetCurrentFilePath()   => window.CurrentFilePath;
    public string?  GetCurrentFolderPath() => window.CurrentFolderPath;
    public string   GetEditorText()        => window.Editor.Text;
    public void     SetEditorText(string t) => window.Dispatcher.Invoke(() => window.Editor.Text = t);
    public int      GetCursorLine()        => window.Editor.TextArea.Caret.Line;
    public int      GetCursorColumn()      => window.Editor.TextArea.Caret.Column;
    public void     OpenFile(string path)  => window.Dispatcher.Invoke(() => window.OpenFileByPath(path));
    public void     SaveCurrentFile()      => window.Dispatcher.Invoke(() => window.SaveFile_Click(window, new()));
    public void     ShowInfo(string m)     => window.Dispatcher.Invoke(() => MessageBox.Show(m, "duk", MessageBoxButton.OK, MessageBoxImage.Information));
    public void     ShowWarning(string m)  => window.Dispatcher.Invoke(() => MessageBox.Show(m, "duk", MessageBoxButton.OK, MessageBoxImage.Warning));
    public void     ShowError(string m)    => window.Dispatcher.Invoke(() => MessageBox.Show(m, "duk", MessageBoxButton.OK, MessageBoxImage.Error));

    public void SetStatusMessage(string message, int durationMs = 3000)
    {
        window.Dispatcher.Invoke(() =>
        {
            window.StatusSession.Text = message;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            timer.Tick += (_, _) => { window.StatusSession.Text = "● オフライン"; timer.Stop(); };
            timer.Start();
        });
    }

    public void RegisterMenuItem(string menu, string label, Action handler) =>
        window.RegisterExtensionMenuItem(menu, label, handler);

    public void RegisterCommand(string label, Action handler) =>
        window.RegisterExtensionCommand(label, handler);

    public async Task<IEnumerable<duk.Core.Lsp.CompletionItem>?> GetCompletionsAsync(int line, int col)
    {
        var path = window.CurrentFilePath;
        if (path == null || window.Lsp == null) return null;
        var list = await window.Lsp.GetCompletionsAsync(path, line, col);
        return list?.Items;
    }

    public async Task<duk.Core.Lsp.Hover?> GetHoverAsync(int line, int col)
    {
        var path = window.CurrentFilePath;
        if (path == null || window.Lsp == null) return null;
        return await window.Lsp.GetHoverAsync(path, line, col);
    }

    public async Task<duk.Core.Lsp.Location[]?> GetDefinitionAsync(int line, int col)
    {
        var path = window.CurrentFilePath;
        if (path == null || window.Lsp == null) return null;
        return await window.Lsp.GoToDefinitionAsync(path, line, col);
    }

    public IEnumerable<duk.Core.Lsp.Diagnostic> GetCurrentDiagnostics() =>
        window.GetDiagnosticsForFile(window.CurrentFilePath ?? "");
}

public class ExtensionLoader
{
    private readonly List<IExtension> _loaded = [];
    private readonly string _extDir;

    public ExtensionLoader()
    {
        _extDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "duk", "extensions");
        Directory.CreateDirectory(_extDir);
    }

    public IReadOnlyList<IExtension> Loaded => _loaded;
    public string ExtensionsDir => _extDir;

    public void LoadAll(IExtensionContext ctx)
    {
        foreach (var dll in Directory.GetFiles(_extDir, "*.dll"))
        {
            try
            {
                var asm = Assembly.LoadFrom(dll);
                foreach (var type in asm.GetExportedTypes())
                {
                    if (!typeof(IExtension).IsAssignableFrom(type) || type.IsAbstract) continue;
                    var ext = (IExtension)Activator.CreateInstance(type)!;
                    ext.Activate(ctx);
                    _loaded.Add(ext);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"拡張機能の読み込みに失敗しました:\n{dll}\n\n{ex.Message}",
                    "拡張機能エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public void UnloadAll()
    {
        foreach (var ext in _loaded)
            try { ext.Deactivate(); } catch { }
        _loaded.Clear();
    }
}
