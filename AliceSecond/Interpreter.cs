using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace AliceThird;

public class Interpreter(IAliceUI ui)
{
    private List<string> _lines = [];
    private int _pc;

    // ラベル・サブルーチンインデックス
    private Dictionary<string, int> _labels      = new(StringComparer.Ordinal);
    private Dictionary<string, int> _subroutines = new(StringComparer.Ordinal); // name → { 行
    private Dictionary<int, int>    _subEnds      = new();                       // { 行 → } 行

    // 変数・フラグ
    private int[]    _vars    = new int[256];
    private string[] _strVars = new string[256];
    private bool[]   _flags   = new bool[256];

    // サブルーチンスタック（最大255段）
    private Stack<int> _callStack = new();

    // コマンドテーブル
    private List<(string Text, string Label)> _commandTable = [];

    private string _scriptDir = ".";

    // テキスト行間ウェイト（ミリ秒）
    private int _textDelay = 0;

    // テキストスタイル状態
    private Color  _color  = Colors.White;
    private bool   _bold   = false;
    private bool   _italic = false;
    private string _font   = "MS Gothic";

    // ==========================================================
    // スクリプト読み込み
    // ==========================================================

    public void LoadScript(string path)
    {
        _scriptDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        _callStack.Clear();

        try
        {
            _lines = [.. File.ReadAllLines(path, new UTF8Encoding(false, true))];
        }
        catch
        {
            var enc = CodePagesEncodingProvider.Instance.GetEncoding(932)
                      ?? Encoding.GetEncoding(932);
            _lines = [.. File.ReadAllLines(path, enc)];
        }

        IndexScript();
        LoadIni();
    }

    private void IndexScript()
    {
        _labels.Clear();
        _subroutines.Clear();
        _subEnds.Clear();

        var subStack = new Stack<int>();

        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (line.Length == 0) continue;

            switch (line[0])
            {
                case ':':
                    _labels[line[1..].Trim()] = i;
                    break;
                case '{':
                    _subroutines[line[1..].Trim()] = i;
                    subStack.Push(i);
                    break;
                case '}':
                    if (subStack.Count > 0)
                        _subEnds[subStack.Pop()] = i;
                    break;
            }
        }
    }

    private void LoadIni()
    {
        var iniPath = Path.Combine(_scriptDir, "alice.ini");
        if (!File.Exists(iniPath)) return;
        foreach (var line in File.ReadAllLines(iniPath))
        {
            var t = line.Trim();
            if (t.StartsWith('#') || t.StartsWith(';')) continue;
            var eq = t.IndexOf('=');
            if (eq < 0) continue;
            var key = t[..eq].Trim();
            var val = t[(eq + 1)..].Trim();
            if (key.Equals("TextDelay", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(val, out int ms))
                _textDelay = ms;
            if (key.Equals("Debug", StringComparison.OrdinalIgnoreCase) && val == "1")
                DebugLog.Enable(_scriptDir);
        }
    }

    // ==========================================================
    // 実行ループ
    // ==========================================================

    public async Task RunAsync(CancellationToken ct = default)
    {
        _pc = 0;
        DebugLog.Log($"RunAsync start. lines={_lines.Count}");
        while (_pc < _lines.Count && !ct.IsCancellationRequested)
        {
            var line = _lines[_pc++];
            if (string.IsNullOrEmpty(line)) continue;

            char op = line[0];
            string rest = line.Length > 1 ? line[1..] : "";

            // 主要命令のみログ（全行出力するとノイズが多いため絞る）
            if (DebugLog.Enabled && "V+?WZ\\".Contains(op))
                DebugLog.Log($"PC={_pc - 1} op='{op}' rest='{rest.Trim()}'");

            // 全角文字始まり → メッセージ（自動改行）
            // ‾ (U+203E) は変数表示命令なので除く
            if (op > 0x7E && op != '\u203E')
            {
                await ui.AppendText(line, newline: true);
                if (_textDelay > 0) await Task.Delay(_textDelay, ct);
                continue;
            }

            switch (op)
            {
                // --------------------------------------------------
                // 基本制御
                // --------------------------------------------------
                case '\'': break; // コメント
                case ':':  break; // ラベル定義（インデックス済み）

                case '*': // ジャンプ / ランダムジャンプ
                    HandleJump(rest.Trim());
                    break;

                case '+': // クリック待ち
                    DebugLog.Log("WaitForClick: begin");
                    await ui.WaitForClick();
                    DebugLog.Log("WaitForClick: done");
                    break;

                case 'W': // 汎用ウェイト: Wm (mミリ秒)
                    if (int.TryParse(rest.Trim(), out int wms) && wms > 0)
                        await Task.Delay(wms, ct);
                    break;

                case 'w': // テキスト行間ウェイト設定: wm (mミリ秒)
                    if (int.TryParse(rest.Trim(), out int twms))
                        _textDelay = twms;
                    break;

                case 'Z': // 終了
                    ui.Exit();
                    return;

                // --------------------------------------------------
                // サブルーチン
                // --------------------------------------------------
                case '{': // サブルーチン定義（通常実行時はスキップ）
                {
                    string name = rest.Trim();
                    if (_subEnds.TryGetValue(_pc - 1, out int endLine))
                        _pc = endLine + 1;
                    break;
                }

                case '}': // サブルーチン終了（return）
                    if (_callStack.Count > 0)
                        _pc = _callStack.Pop();
                    else
                        return; // スタック空なら終了
                    break;

                case '\\': // サブルーチン呼び出し: \subname
                {
                    string name = rest.Trim();
                    if (_subroutines.TryGetValue(name, out int subLine) &&
                        _callStack.Count < 255)
                    {
                        _callStack.Push(_pc);
                        _pc = subLine + 1; // { の次の行から実行
                    }
                    break;
                }

                // --------------------------------------------------
                // アドレス・間接ジャンプ
                // --------------------------------------------------
                case '&': // X0 = labelのアドレス
                {
                    string lbl = rest.Trim();
                    if (_labels.TryGetValue(lbl, out int addr))
                        _vars[0] = addr;
                    break;
                }

                case 'J': // Xnの指す行にジャンプ
                    if (int.TryParse(rest.Trim(), out int jn))
                        _pc = _vars[jn];
                    break;

                // --------------------------------------------------
                // テキスト表示
                // --------------------------------------------------
                case '#': // 改行 / 改行付きメッセージ
                    await ui.AppendText(rest, newline: true);
                    if (_textDelay > 0) await Task.Delay(_textDelay, ct);
                    break;

                case '^': // 改行なしメッセージ
                    await ui.AppendText(rest, newline: false);
                    break;

                case '~':
                case '\u203E': // ‾ — 変数Xnを表示
                    if (int.TryParse(rest.Trim(), out int dispN))
                        await ui.AppendText(_vars[dispN].ToString(), newline: false);
                    break;

                case 'A': // テキスト色変更: A r g b
                {
                    var p = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 3 &&
                        byte.TryParse(p[0], out byte r) &&
                        byte.TryParse(p[1], out byte g) &&
                        byte.TryParse(p[2], out byte b))
                    {
                        _color = Color.FromRgb(r, g, b);
                        ui.SetTextStyle(_color, _bold, _italic, _font);
                    }
                    break;
                }

                case 'a': // 太字・斜体: a b i
                {
                    var p = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 2)
                    {
                        _bold   = p[0] == "1";
                        _italic = p[1] == "1";
                        ui.SetTextStyle(_color, _bold, _italic, _font);
                    }
                    break;
                }

                case 'f': // フォント指定
                    _font = rest.Trim();
                    ui.SetTextStyle(_color, _bold, _italic, _font);
                    break;

                // --------------------------------------------------
                // コマンド
                // --------------------------------------------------
                case '-': // コマンドテーブル追加: -command:label
                {
                    int colon = rest.LastIndexOf(':');
                    if (colon > 0)
                        _commandTable.Add((rest[..colon], rest[(colon + 1)..]));
                    break;
                }

                case '?': // コマンド入力＆分岐: ?[cancelLabel]
                {
                    string? cancelLabel = rest.Trim().Length > 0 ? rest.Trim() : null;
                    DebugLog.Log($"ShowCommands: begin count={_commandTable.Count} cancelLabel='{cancelLabel}'");
                    var selected = await ui.ShowCommands([.. _commandTable], cancelLabel);
                    DebugLog.Log($"ShowCommands: done selected='{selected ?? "(null)"}'");
                    _commandTable.Clear();
                    if (selected != null)
                        JumpTo(selected);
                    else if (cancelLabel != null)
                        JumpTo(cancelLabel);
                    break;
                }

                // --------------------------------------------------
                // 変数演算
                // --------------------------------------------------
                case 'I': // Xn = m
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0) _vars[n] = m;
                    break;
                }

                case 'i': // Xn = Xm
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0 && (uint)m < 256) _vars[n] = _vars[m];
                    break;
                }

                case 'P': // Xn += m
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0) _vars[n] += m;
                    break;
                }

                case 'p': // Xn += Xm
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0 && (uint)m < 256) _vars[n] += _vars[m];
                    break;
                }

                case 'M': // Xn -= m
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0) _vars[n] -= m;
                    break;
                }

                case 'm': // Xn -= Xm
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0 && (uint)m < 256) _vars[n] -= _vars[m];
                    break;
                }

                case 'K': // Xn = Xn * m
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0) _vars[n] *= m;
                    break;
                }

                case 'k': // Xn = Xn * Xm
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0 && (uint)m < 256) _vars[n] *= _vars[m];
                    break;
                }

                case 'D': // Xn = Xn / m
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0 && m != 0) _vars[n] /= m;
                    break;
                }

                case 'd': // Xn = Xn / Xm
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0 && (uint)m < 256 && _vars[m] != 0)
                        _vars[n] /= _vars[m];
                    break;
                }

                case 'C': // XnとXmを比較してX0に返す
                {
                    var (n, m) = ParseNM(rest);
                    if (n >= 0 && (uint)m < 256)
                        _vars[0] = _vars[n].CompareTo(_vars[m]) switch
                        {
                            0    => 0,
                            < 0  => 1,
                            _    => 2,
                        };
                    break;
                }

                case '@': // X0 = random(0, m-1)
                    if (int.TryParse(rest.Trim(), out int rmax) && rmax > 0)
                        _vars[0] = Random.Shared.Next(0, rmax);
                    break;

                // --------------------------------------------------
                // 条件分岐
                // --------------------------------------------------
                case '=': // Xn == m のとき jump
                    HandleConditionalJump(rest, (a, b) => a == b);
                    break;

                case '<': // Xn < m のとき jump
                    HandleConditionalJump(rest, (a, b) => a < b);
                    break;

                case '>': // Xn > m のとき jump
                    HandleConditionalJump(rest, (a, b) => a > b);
                    break;

                // --------------------------------------------------
                // フラグ
                // --------------------------------------------------
                case 'S': // フラグn番セット
                    if (int.TryParse(rest.Trim(), out int sn) && (uint)sn < 256)
                        _flags[sn] = true;
                    break;

                case 'R': // フラグn番リセット
                    if (int.TryParse(rest.Trim(), out int rn) && (uint)rn < 256)
                        _flags[rn] = false;
                    break;

                case 'N': // フラグn番反転
                    if (int.TryParse(rest.Trim(), out int nn) && (uint)nn < 256)
                        _flags[nn] = !_flags[nn];
                    break;

                case 'F': // フラグn番がセットされていたら jump: Fn:label
                {
                    int colon = rest.IndexOf(':');
                    if (colon >= 0 &&
                        int.TryParse(rest[..colon].Trim(), out int fn) &&
                        (uint)fn < 256 &&
                        _flags[fn])
                        JumpTo(rest[(colon + 1)..].Trim());
                    break;
                }

                // --------------------------------------------------
                // メディア・画像
                // --------------------------------------------------
                case '.': // 画像表示
                    await ui.ShowImage(Path.Combine(_scriptDir, "pic", rest.Trim()));
                    break;

                case '/': // BGMループ再生
                    ui.PlayMidi(ResolvePath("bgm", "midi", rest.Trim()));
                    break;

                case '%': // SE再生
                    ui.PlayWav(ResolvePath("se", "wave", rest.Trim()));
                    break;

                case 'V': // AVI再生
                {
                    var aviPath = Path.Combine(_scriptDir, "avi", rest.Trim());
                    DebugLog.Log($"PlayAvi: begin path='{aviPath}' exists={File.Exists(aviPath)}");
                    await ui.PlayAvi(aviPath);
                    DebugLog.Log("PlayAvi: done");
                    break;
                }

                case '$': // PICフラッシュ: $m1 m2
                {
                    var p = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 2 &&
                        int.TryParse(p[0], out int m1) &&
                        int.TryParse(p[1], out int m2))
                        await ui.Flash(isWhite: m1 == 1, ms: m2);
                    break;
                }

                // --------------------------------------------------
                // スプライト
                // --------------------------------------------------
                case 'L': // スプライトロード: Ln filename
                {
                    var p = rest.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 2 && int.TryParse(p[0], out int n))
                        await ui.LoadSprite(n, Path.Combine(_scriptDir, "pic", p[1]));
                    break;
                }

                case 'l': // スプライト解放: ln
                    if (int.TryParse(rest.Trim(), out int lIdx))
                        ui.FreeSprite(lIdx);
                    break;

                case 'O': // スプライト表示: On n1 n2 → (Xn1, Xn2)
                {
                    var p = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 3 &&
                        int.TryParse(p[0], out int n) &&
                        int.TryParse(p[1], out int n1) &&
                        int.TryParse(p[2], out int n2))
                        await ui.ShowSprite(n, _vars[n1], _vars[n2]);
                    break;
                }

                case 'o': // スプライト非表示: on
                    if (int.TryParse(rest.Trim(), out int oIdx))
                        ui.HideSprite(oIdx);
                    break;

                // --------------------------------------------------
                // ウィンドウ位置
                // --------------------------------------------------
                case 'T': // ウィンドウ位置変更: Tn1 n2 m
                {
                    var p = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 3 &&
                        int.TryParse(p[0], out int n1) &&
                        int.TryParse(p[1], out int n2) &&
                        int.TryParse(p[2], out int m))
                        ui.MoveWindow(_vars[n1], _vars[n2], m);
                    break;
                }

                case 'G': // ウィンドウ位置取得: Gn1 n2 m
                {
                    var p = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 3 &&
                        int.TryParse(p[0], out int n1) &&
                        int.TryParse(p[1], out int n2) &&
                        int.TryParse(p[2], out int m))
                    {
                        var (x, y) = ui.GetWindowPosition(m);
                        _vars[n1] = x;
                        _vars[n2] = y;
                    }
                    break;
                }

                // --------------------------------------------------
                // ファイル・外部プロセス
                // --------------------------------------------------
                case '_': // 別ファイルの読み込み: _filename
                {
                    string path = Path.Combine(_scriptDir, rest.Trim());
                    if (File.Exists(path))
                    {
                        LoadScript(path);
                        _pc = 0; // 新ファイルの先頭から実行
                    }
                    break;
                }

                // --------------------------------------------------
                // 拡張命令
                // --------------------------------------------------
                case 'E':
                    await HandleECommand(rest);
                    break;
            }
        }
    }

    // ==========================================================
    // ヘルパー
    // ==========================================================

    private void HandleJump(string rest)
    {
        var targets = rest.Split(':');
        if (targets.Length == 1)
            JumpTo(targets[0]);
        else
            JumpTo(targets[Random.Shared.Next(targets.Length)]);
    }

    private void JumpTo(string label)
    {
        label = label.Trim();
        if (_labels.TryGetValue(label, out int line))
            _pc = line;
    }

    private void HandleConditionalJump(string rest, Func<int, int, bool> pred)
    {
        int colon = rest.LastIndexOf(':');
        if (colon < 0) return;
        string label = rest[(colon + 1)..].Trim();
        var parts = rest[..colon].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        if (!int.TryParse(parts[0], out int varIdx)) return;
        if (!int.TryParse(parts[1], out int literal)) return;
        if (pred(_vars[varIdx], literal))
            JumpTo(label);
    }

    // primary フォルダを優先し、なければ fallback フォルダを探す
    private string ResolvePath(string primary, string fallback, string filename)
    {
        var primaryPath = Path.Combine(_scriptDir, primary, filename);
        if (File.Exists(primaryPath)) return primaryPath;
        return Path.Combine(_scriptDir, fallback, filename);
    }

    private static (int n, int m) ParseNM(string rest)
    {
        var p = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 2 &&
            int.TryParse(p[0], out int n) &&
            int.TryParse(p[1], out int m))
            return (n, m);
        return (-1, 0);
    }

    private async Task HandleECommand(string rest)
    {
        if (rest.Length < 4) return;
        string code = rest[..4];
        string args = rest.Length > 4 ? rest[4..].Trim() : "";

        switch (code)
        {
            case "0000": // 文字列変数nを表示
                if (int.TryParse(args, out int n0))
                    await ui.AppendText(_strVars[n0] ?? "", newline: false);
                break;

            case "0001": // 文字列変数n = str
            {
                var p = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2 && int.TryParse(p[0], out int n))
                    _strVars[n] = p[1];
                break;
            }

            case "0002": // ファイルの1行目を文字列変数nに読み込む
            {
                var p = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2 && int.TryParse(p[0], out int n))
                {
                    string path = Path.Combine(_scriptDir, p[1]);
                    if (File.Exists(path))
                        _strVars[n] = File.ReadLines(path).FirstOrDefault() ?? "";
                }
                break;
            }
        }
    }
}
