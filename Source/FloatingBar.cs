using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Aureo;
public sealed class FloatingBar : Window
{
    readonly TextBlock time = new() { Text = "待命", FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(228, 212, 180)), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 9) };
    readonly Button record, camera, pause, mic, source, speaker;
    readonly TextBlock sourceLabel;
    readonly Func<bool> microphoneEnabled;
    readonly Func<bool> systemAudioEnabled;
    readonly Func<string> currentMode;
    bool? paintedMicrophone, paintedSystem;
    internal Button SystemAudioButton => speaker;
    internal Button PauseButton => pause;
    internal Button MicrophoneButton => mic;
    internal Button SourceButton => source;
    internal Button RecordButton => record;
    internal Button CameraButton => camera;
    readonly CaptureSession session;
    bool preparing, photographing, selecting;
    DateTime noticeUntil;
    string notice = "";
    public bool Excluded { get; private set; }
    public FloatingBar(CaptureSession capture, Action showMain, Func<Task>? start = null, Func<Task>? screenshot = null, Action? microphone = null, Action? selectSource = null, Func<bool>? microphoneEnabled = null, Func<string>? currentMode = null, Action? systemAudio = null, Func<bool>? systemAudioEnabled = null)
    {
        session = capture;
        this.microphoneEnabled = microphoneEnabled ?? (() => false);
        this.systemAudioEnabled = systemAudioEnabled ?? (() => false);
        this.currentMode = currentMode ?? (() => "螢幕");
        Foreground = new SolidColorBrush(Color.FromRgb(241, 240, 236)); FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        Title = "Amos錄影與截圖 · 擷取工具列"; Width = 76; Height = 434; WindowStyle = WindowStyle.None;
        AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowInTaskbar = false;
        Left = SystemParameters.WorkArea.Right - Width - 20; Top = SystemParameters.WorkArea.Top + 90;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(19, 21, 25)), BorderBrush = new SolidColorBrush(Color.FromRgb(130, 108, 67)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(9, 5, 9, 9) };
        var column = new StackPanel();
        var grip = new TextBlock { Text = "⠿", Foreground = new SolidColorBrush(Color.FromRgb(130, 108, 67)), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Cursor = System.Windows.Input.Cursors.SizeAll };
        grip.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) showMain(); else DragMove(); };
        column.Children.Add(grip); column.Children.Add(time);
        source = MakeButton("", "選擇來源 · 螢幕／範圍／視窗");
        source.Height = 49;
        var sourceContent = new StackPanel();
        sourceContent.Children.Add(new TextBlock { Text = "\uE7F4", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center });
        sourceLabel = new TextBlock { Text = this.currentMode(), FontFamily = new FontFamily("Microsoft JhengHei UI"), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
        sourceContent.Children.Add(sourceLabel); source.Content = sourceContent;
        source.Click += (_, _) => selectSource?.Invoke();
        record = MakeButton("\uE7C8", "開始錄製", Brushes.IndianRed);
        camera = MakeButton("\uE722", "截圖 · 儲存 PNG", new SolidColorBrush(Color.FromRgb(213, 183, 124)));
        pause = MakeButton("\uE769", "暫停 / 繼續");
        mic = MakeButton("\uE720", "麥克風 · 點一下開啟或關閉");
        speaker = MakeButton("\uE767", "系統音訊 · 點一下開啟或關閉");
        var restore = MakeButton("\uE8A7", "開啟設定");
        record.Click += async (_, _) => { if (session.State == CaptureState.Idle) { if (start != null) await start(); } else session.Stop(); };
        camera.Click += async (_, _) => { if (screenshot != null) await screenshot(); };
        pause.Click += (_, _) => session.PauseResume();
        mic.Click += (_, _) => { microphone?.Invoke(); Update(); };
        speaker.Click += (_, _) => { systemAudio?.Invoke(); Update(); };
        restore.Click += (_, _) => showMain();
        foreach (var button in new[] { source, record, camera, pause, mic, speaker, restore }) column.Children.Add(button);
        border.Child = column; Content = border;
        SourceInitialized += (_, _) => Excluded = Native.Exclude(this);
        Closing += (_, e) => { if (session.State != CaptureState.Idle) { e.Cancel = true; Hide(); } };
        Update();
    }
    static Button MakeButton(string icon, string tip, Brush? color = null) => new() { Content = icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 19, Foreground = color ?? new SolidColorBrush(Color.FromRgb(199, 197, 191)), ToolTip = tip, Height = 43, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 5) };
    public void SetSelecting(bool value) { selecting = value; Update(); }
    public void SetPreparing(bool value) { preparing = value; Update(); }
    public void SetPhotographing(bool value) { photographing = value; Update(); }
    public void SetNotice(string value) { notice = value; noticeUntil = DateTime.UtcNow.AddSeconds(2); Update(); }
    public void Update() {
        time.Text = session.State == CaptureState.Idle ? "待命" : session.Elapsed.ToString(@"mm\:ss");
        if (session.State == CaptureState.Stopping) time.Text = "儲存中";
        if (session.State == CaptureState.Starting || preparing) time.Text = "準備中";
        if (DateTime.UtcNow < noticeUntil) time.Text = notice;
        source.IsEnabled = !preparing && !photographing && !selecting && session.State is CaptureState.Idle or CaptureState.Recording or CaptureState.Paused;
        sourceLabel.Text = currentMode();
        source.ToolTip = source.IsEnabled ? "選擇來源 · 錄影中也可切換" : "請等待目前操作完成";
        mic.IsEnabled = !preparing && !selecting && session.State is CaptureState.Idle or CaptureState.Recording or CaptureState.Paused;
        speaker.IsEnabled = mic.IsEnabled;
        bool enabled = microphoneEnabled();
        mic.ToolTip = enabled ? "麥克風已開啟 · 點一下關閉" : "麥克風已關閉 · 點一下開啟";
        if (paintedMicrophone != enabled) {
            paintedMicrophone = enabled;
            mic.Background = new SolidColorBrush(enabled ? Color.FromRgb(55, 46, 29) : Color.FromRgb(32, 35, 41));
            mic.Foreground = new SolidColorBrush(enabled ? Color.FromRgb(225, 194, 134) : Color.FromRgb(133, 138, 147));
            var glyph = new Grid { Width = 23, Height = 23 };
            glyph.Children.Add(new TextBlock { Text = "\uE720", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            if (!enabled) glyph.Children.Add(new System.Windows.Shapes.Line { X1 = 2, Y1 = 21, X2 = 21, Y2 = 2, Stroke = new SolidColorBrush(Color.FromRgb(170, 116, 112)), StrokeThickness = 2 });
            mic.Content = glyph;
        }
        bool sound = systemAudioEnabled();
        speaker.ToolTip = sound ? "系統音訊已開啟 · 點一下關閉" : "系統音訊已關閉 · 點一下開啟";
        if (paintedSystem != sound) {
            paintedSystem = sound;
            speaker.Background = new SolidColorBrush(sound ? Color.FromRgb(55, 46, 29) : Color.FromRgb(32, 35, 41));
            speaker.Foreground = new SolidColorBrush(sound ? Color.FromRgb(225, 194, 134) : Color.FromRgb(133, 138, 147));
            var glyph = new Grid { Width = 23, Height = 23 };
            glyph.Children.Add(new TextBlock { Text = "\uE767", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            if (!sound) glyph.Children.Add(new System.Windows.Shapes.Line { X1 = 2, Y1 = 21, X2 = 21, Y2 = 2, Stroke = new SolidColorBrush(Color.FromRgb(170, 116, 112)), StrokeThickness = 2 });
            speaker.Content = glyph;
        }
        bool paused = session.State == CaptureState.Paused;
        record.Content = session.State == CaptureState.Idle ? "\uE7C8" : "\uE71A";
        record.ToolTip = session.State == CaptureState.Idle ? "開始錄製" : "停止錄製並儲存";
        record.IsEnabled = !preparing && !photographing && !selecting && session.State is CaptureState.Idle or CaptureState.Recording or CaptureState.Paused;
        camera.IsEnabled = !preparing && !photographing && !selecting && session.State is CaptureState.Idle or CaptureState.Recording or CaptureState.Paused;
        pause.Content = paused ? "\uE768" : "\uE769";
        pause.ToolTip = paused ? "繼續錄製" : "暫停錄製";
        pause.IsEnabled = !photographing && session.State is CaptureState.Recording or CaptureState.Paused;
    }
}
