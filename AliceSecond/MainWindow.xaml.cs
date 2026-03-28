using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AliceThird;

public partial class MainWindow : Window, IAliceUI
{
    // --- 状態 ---
    private readonly SemaphoreSlim _clickSem = new(0, 1);
    private TaskCompletionSource<string?>? _commandTcs;
    private bool _waitingForCommand = false;
    private int _selectedCommandIndex = -1;

    private readonly Dictionary<int, Image> _spriteImages = new();
    private CancellationTokenSource _cts = new();

    // テキストスタイル（インタープリタから SetTextStyle で更新）
    private Color _textColor = Colors.White;
    private bool _bold = false;
    private bool _italic = false;
    private string _fontName = "MS Gothic";

    private MediaClock? _bgmClock;

    public MainWindow()
    {
        InitializeComponent();
    }

    public async void StartScript(string path)
    {
        var interp = new Interpreter(this);
        interp.LoadScript(path);
        try
        {
            // スレッドプールで実行（UI スレッドをブロックしない）
            await Task.Run(() => interp.RunAsync(_cts.Token), _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show($"スクリプトエラー:\n{ex.Message}", "AliceSecond",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==========================================================
    // IAliceUI 実装
    // ==========================================================

    public Task AppendText(string text, bool newline)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            if (text.Length > 0)
            {
                TxtBlock.Inlines.Add(new Run(text)
                {
                    Foreground  = new SolidColorBrush(_textColor),
                    FontFamily  = new FontFamily(_fontName),
                    FontWeight  = _bold   ? FontWeights.Bold   : FontWeights.Normal,
                    FontStyle   = _italic ? FontStyles.Italic  : FontStyles.Normal,
                    FontSize    = 13,
                });
            }
            if (newline)
                TxtBlock.Inlines.Add(new LineBreak());

            TxtScroller.ScrollToBottom();
        }).Task;
    }

    public Task ShowImage(string path)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            if (!File.Exists(path)) return;
            var bmp = LoadBitmap(path);
            PicImage.Source = bmp;
        }).Task;
    }

    public Task WaitForClick() => _clickSem.WaitAsync(_cts.Token);

    public Task<string?> ShowCommands(List<(string Text, string Label)> commands, string? cancelLabel)
    {
        _commandTcs = new TaskCompletionSource<string?>();
        _waitingForCommand = true;

        Dispatcher.InvokeAsync(() =>
        {
            ComPanel.Children.Clear();
            _selectedCommandIndex = 0;
            foreach (var (text, label) in commands)
            {
                var btn = new Button
                {
                    Content     = text,
                    Tag         = label,
                    Margin      = new Thickness(2, 3, 2, 3),
                    Padding     = new Thickness(8, 4, 8, 4),
                    Foreground  = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(100, 130, 180)),
                    FontFamily  = new FontFamily("MS Gothic"),
                    FontSize    = 13,
                    Cursor      = Cursors.Hand,
                };
                btn.Click += CommandButton_Click;
                ComPanel.Children.Add(btn);
            }
            UpdateCommandSelection();
        });

        return _commandTcs.Task.ContinueWith(t =>
        {
            _waitingForCommand = false;
            Dispatcher.InvokeAsync(() => ComPanel.Children.Clear());
            return t.Result;
        });
    }

    private void CommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && _commandTcs != null)
            _commandTcs.TrySetResult((string)btn.Tag);
    }

    private void UpdateCommandSelection()
    {
        for (int i = 0; i < ComPanel.Children.Count; i++)
        {
            if (ComPanel.Children[i] is Button btn)
            {
                btn.Background = i == _selectedCommandIndex
                    ? new SolidColorBrush(Color.FromRgb(0, 60, 120))   // 選択中
                    : new SolidColorBrush(Color.FromRgb(0, 0, 60));     // 非選択
            }
        }
    }

    private void ConfirmSelectedCommand()
    {
        if (!_waitingForCommand) return;
        if (_selectedCommandIndex >= 0 &&
            _selectedCommandIndex < ComPanel.Children.Count &&
            ComPanel.Children[_selectedCommandIndex] is Button btn)
        {
            _commandTcs?.TrySetResult((string)btn.Tag);
        }
    }

    public Task LoadSprite(int n, string path)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            if (!File.Exists(path)) return;
            var bmp = LoadBitmap(path);

            // BMP ファイルはパレット0番（黒）を透過色として扱う（旧来互換）
            BitmapSource source = path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                ? ApplyBlackColorKey(bmp)
                : bmp;

            if (!_spriteImages.TryGetValue(n, out var img))
            {
                img = new Image { Visibility = Visibility.Collapsed };
                PicCanvas.Children.Add(img);
                _spriteImages[n] = img;
            }
            img.Source = source;
            img.Width  = source.PixelWidth;
            img.Height = source.PixelHeight;
        }).Task;
    }

    public void FreeSprite(int n)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_spriteImages.Remove(n, out var img))
                PicCanvas.Children.Remove(img);
        });
    }

    public Task ShowSprite(int n, int x, int y)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            if (_spriteImages.TryGetValue(n, out var img))
            {
                System.Windows.Controls.Canvas.SetLeft(img, x);
                System.Windows.Controls.Canvas.SetTop(img, y);
                img.Visibility = Visibility.Visible;
            }
        }).Task;
    }

    public void HideSprite(int n)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_spriteImages.TryGetValue(n, out var img))
                img.Visibility = Visibility.Collapsed;
        });
    }

    public async Task Flash(bool isWhite, int ms)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            FlashOverlay.Fill    = isWhite ? Brushes.White : Brushes.Black;
            FlashOverlay.Opacity = 1.0;
        });
        await Task.Delay(ms);
        await Dispatcher.InvokeAsync(() =>
        {
            FlashOverlay.Opacity = 0.0;
        });
    }

    public void PlayMidi(string path)
    {
        Dispatcher.InvokeAsync(() =>
        {
            StopBgm();
            if (!File.Exists(path)) return;
            var timeline = new MediaTimeline(new Uri(path, UriKind.Absolute))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            _bgmClock = (MediaClock)timeline.CreateClock();
            BgmPlayer.Clock = _bgmClock;
            _bgmClock.Controller?.Begin();
        });
    }

    private void StopBgm()
    {
        if (_bgmClock != null)
        {
            _bgmClock.Controller?.Stop();
            BgmPlayer.Clock = null;
            _bgmClock = null;
        }
    }

    public void PlayWav(string path)
    {
        if (!File.Exists(path)) return;
        var player = new System.Media.SoundPlayer(path);
        player.Play();
    }

    private Uri? _currentAviUri;

    public async Task PlayAvi(string path)
    {
        DebugLog.Log($"PlayAvi(UI): enter path='{path}' exists={File.Exists(path)}");
        if (!File.Exists(path))
        {
            DebugLog.Log("PlayAvi(UI): file not found, abort");
            return;
        }

        var uri = new Uri(path, UriKind.Absolute);
        var tcs = new TaskCompletionSource();
        bool sameFile = (_currentAviUri?.AbsolutePath == uri.AbsolutePath);
        DebugLog.Log($"PlayAvi(UI): sameFile={sameFile} currentUri='{_currentAviUri}'");

        await Dispatcher.InvokeAsync(() =>
        {
            DebugLog.Log($"PlayAvi(UI): dispatched. VideoPlayer.Source='{VideoPlayer.Source}' HasVideo={VideoPlayer.HasVideo}");
            VideoPlayer.Stop();
            VideoPlayer.Visibility = Visibility.Visible;

            void OnVideoEnded(object? s, RoutedEventArgs e)
            {
                DebugLog.Log("PlayAvi(UI): MediaEnded fired → tcs.SetResult");
                VideoPlayer.MediaEnded -= OnVideoEnded;
                tcs.TrySetResult();
            }

            VideoPlayer.MediaEnded += OnVideoEnded;

            if (sameFile)
            {
                // 同じファイルは先頭にシークして再生
                DebugLog.Log("PlayAvi(UI): same file → seek to Zero + Play");
                VideoPlayer.Position = TimeSpan.Zero;
                VideoPlayer.Play();
            }
            else
            {
                // 新しいファイルは MediaOpened を待ってから再生
                void OnMediaOpened(object? s, RoutedEventArgs e)
                {
                    DebugLog.Log("PlayAvi(UI): MediaOpened fired → Play");
                    VideoPlayer.MediaOpened -= OnMediaOpened;
                    VideoPlayer.Play();
                }

                void OnMediaFailed(object? s, ExceptionRoutedEventArgs e)
                {
                    DebugLog.Log($"PlayAvi(UI): MediaFailed! {e.ErrorException?.Message}");
                    VideoPlayer.MediaFailed -= OnMediaFailed;
                    VideoPlayer.MediaEnded -= OnVideoEnded;
                    // 再生失敗はスキップして続行（スクリプトを止めない）
                    VideoPlayer.Visibility = Visibility.Collapsed;
                    tcs.TrySetResult();
                }

                VideoPlayer.MediaOpened += OnMediaOpened;
                VideoPlayer.MediaFailed += OnMediaFailed;
                DebugLog.Log($"PlayAvi(UI): setting Source='{uri}'");
                VideoPlayer.Source = uri;
                _currentAviUri = uri;
            }
        });

        DebugLog.Log("PlayAvi(UI): awaiting tcs.Task");
        await tcs.Task;
        DebugLog.Log("PlayAvi(UI): tcs.Task completed");

        await Dispatcher.InvokeAsync(() =>
        {
            VideoPlayer.Stop();
            VideoPlayer.Visibility = Visibility.Collapsed;
            DebugLog.Log("PlayAvi(UI): VideoPlayer hidden");
        });
    }

    public void SetTextStyle(Color color, bool bold, bool italic, string font)
    {
        _textColor = color;
        _bold      = bold;
        _italic    = italic;
        _fontName  = font;
    }

    // T/G 命令は言語仕様上定義されているが、1ウィンドウ実装では無視する。
    // 独立した3ウィンドウ実装（あるいはタイヤ付きコマンドウィンドウ実装）では有効にすること。
    public void MoveWindow(int x, int y, int windowType) { }

    public (int x, int y) GetWindowPosition(int windowType) => (0, 0);

    public void Exit()
    {
        Dispatcher.InvokeAsync(Close);
    }

    // ==========================================================
    // UI イベントハンドラ
    // ==========================================================

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_waitingForCommand && _clickSem.CurrentCount == 0)
            _clickSem.Release();
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 右クリック＝コマンドのキャンセル
        if (_waitingForCommand)
            _commandTcs?.TrySetResult(null);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Return:
            case Key.Space:
                if (_waitingForCommand)
                    ConfirmSelectedCommand();
                else if (_clickSem.CurrentCount == 0)
                    _clickSem.Release();
                break;

            case Key.Up:
                if (_waitingForCommand && ComPanel.Children.Count > 0)
                {
                    _selectedCommandIndex =
                        (_selectedCommandIndex - 1 + ComPanel.Children.Count)
                        % ComPanel.Children.Count;
                    UpdateCommandSelection();
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (_waitingForCommand && ComPanel.Children.Count > 0)
                {
                    _selectedCommandIndex =
                        (_selectedCommandIndex + 1) % ComPanel.Children.Count;
                    UpdateCommandSelection();
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                if (_waitingForCommand)
                    _commandTcs?.TrySetResult(null);
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        StopBgm();
        base.OnClosed(e);
    }

    // ==========================================================
    // ユーティリティ
    // ==========================================================

    private static BitmapImage LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource    = new Uri(path, UriKind.Absolute);
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// BMP スプライト用カラーキー透過：純黒 (R=0,G=0,B=0) のピクセルを透明にする。
    /// 旧来エンジン（AliceSecond 等）はパレット0番＝黒を透過色として使用していた。
    /// </summary>
    private static BitmapSource ApplyBlackColorKey(BitmapSource source)
    {
        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width  = formatted.PixelWidth;
        int height = formatted.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            // BGRA 順。B=0, G=0, R=0 の純黒ピクセルを透明化
            if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0)
                pixels[i + 3] = 0;
        }

        var result = BitmapSource.Create(
            width, height,
            source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null,
            pixels, stride);
        result.Freeze();
        return result;
    }
}
