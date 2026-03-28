using System;
using System.IO;

namespace AliceThird;

/// <summary>
/// デバッグログ。alice.ini に Debug=1 を記載すると有効になる。
/// ログは alice_debug.log に追記される。
/// </summary>
internal static class DebugLog
{
    private static string? _logPath;
    private static readonly object _lock = new();

    public static bool Enabled => _logPath != null;

    public static void Enable(string dir)
    {
        _logPath = Path.Combine(dir, "alice_debug.log");
        // 起動時にクリア
        File.WriteAllText(_logPath, $"=== AliceThird Debug Log === {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
    }

    public static void Log(string message)
    {
        if (_logPath == null) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (_lock)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); }
            catch { /* ログ失敗は無視 */ }
        }
    }
}
