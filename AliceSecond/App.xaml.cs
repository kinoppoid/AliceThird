using System.IO;
using System.Windows;

namespace AliceThird;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? scriptPath = null;
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            scriptPath = Path.GetFullPath(e.Args[0]);

        if (scriptPath == null)
        {
            // Look for index.alc next to the exe
            var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.alc");
            if (File.Exists(candidate))
                scriptPath = candidate;
        }

        var window = new MainWindow();
        window.Show();

        if (scriptPath != null)
            window.StartScript(scriptPath);
        else
            MessageBox.Show("index.alc が見つかりません。\n.alc ファイルをドロップして起動してください。",
                            "AliceSecond", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
