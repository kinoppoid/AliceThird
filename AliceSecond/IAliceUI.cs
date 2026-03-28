using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;

namespace AliceThird;

public interface IAliceUI
{
    /// <summary>テキストウィンドウにテキストを追加する</summary>
    Task AppendText(string text, bool newline);

    /// <summary>PICウィンドウに画像を表示する</summary>
    Task ShowImage(string path);

    /// <summary>クリック/Enterを待つ</summary>
    Task WaitForClick();

    /// <summary>コマンドテーブルを表示して選択を待つ。キャンセル時はnullを返す</summary>
    Task<string?> ShowCommands(List<(string Text, string Label)> commands, string? cancelLabel);

    /// <summary>スプライトをロードする</summary>
    Task LoadSprite(int n, string path);

    /// <summary>スプライトを解放する</summary>
    void FreeSprite(int n);

    /// <summary>スプライトを指定座標に表示する</summary>
    Task ShowSprite(int n, int x, int y);

    /// <summary>スプライトを非表示にする</summary>
    void HideSprite(int n);

    /// <summary>PICウィンドウをフラッシュする</summary>
    Task Flash(bool isWhite, int ms);

    /// <summary>MIDIをループ再生する</summary>
    void PlayMidi(string path);

    /// <summary>WAVを再生する</summary>
    void PlayWav(string path);

    /// <summary>AVIを再生して終了まで待つ</summary>
    Task PlayAvi(string path);

    /// <summary>テキストスタイルを変更する</summary>
    void SetTextStyle(Color color, bool bold, bool italic, string font);

    /// <summary>
    /// ウィンドウを移動する (m=0:PIC / 1:TXT / 2:COM)。
    /// 実装依存: 1ウィンドウ構成では no-op でよい。独立3ウィンドウ実装では有効にすること。
    /// </summary>
    void MoveWindow(int x, int y, int windowType);

    /// <summary>
    /// ウィンドウ位置を取得する (m=0:PIC / 1:TXT / 2:COM)。
    /// 実装依存: 1ウィンドウ構成では (0,0) を返してよい。
    /// </summary>
    (int x, int y) GetWindowPosition(int windowType);

    /// <summary>ウィンドウを閉じる</summary>
    void Exit();
}
