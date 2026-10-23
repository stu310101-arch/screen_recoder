using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenRecorderLib;
using Forms = System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Aureo;
public sealed record SourceItem(string Label, string Device, IntPtr Handle, Rectangle Bounds);
public sealed record MicrophoneItem(string Label, string Device);
public sealed class Preferences {
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Amos錄影與截圖");
    public string PictureFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Amos錄影與截圖");
    public int Fps { get; set; } = 1;
    public int Quality { get; set; } = 1;
    public int Api { get; set; }
    public int Resolution { get; set; }
    public double MicrophoneVolume { get; set; } = 100;
    public string MicrophoneDevice { get; set; } = "";
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; }
    public bool Cursor { get; set; } = true;
    public bool Countdown { get; set; } = true;
}
public partial class MainWindow : Window
{
    readonly CaptureSession session = new();
    internal CaptureState CaptureStatus => session.State;
    internal bool IsPhotographing => photographing;
    internal string CurrentRecordingPath => session.FilePath;
    internal void SetTestDestinations(string folder) {
        if (!previewOnly) throw new InvalidOperationException("Test destinations require preview mode");
        preferences.Folder = Path.Combine(folder, "Videos"); preferences.PictureFolder = Path.Combine(folder, "Pictures");
        FolderLabel.Text = preferences.Folder; PictureFolderLabel.Text = preferences.PictureFolder;
        CountdownCheck.IsChecked = false; preferences.Countdown = false;
        SystemCheck.IsChecked = false; preferences.SystemAudio = false;
        MicCheck.IsChecked = false; preferences.Microphone = false;
        FpsCombo.SelectedIndex = 1; ResolutionCombo.SelectedIndex = 0;
    }
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AureoRecorder", "settings.json");
    Preferences preferences = new();
    string mode = "Screen";
    Rectangle? region;
    FloatingBar? bar;
    SourcePicker? sourcePicker;
    internal FloatingBar? Toolbar => bar;
    internal SourcePicker? ToolbarSourcePicker => sourcePicker;
    internal string SourceMode => mode;
    internal Rectangle? SelectedRegion => region;
    internal Func<Rectangle, Rectangle?>? RegionSelectionOverride { get; set; }
    Forms.NotifyIcon? tray;
    System.Drawing.Icon? trayIcon;
    bool preparing, cancelPrepare, exitAfterStop, photographing, selectingSource;
    IntPtr hwnd;
    int previewTicks;
    readonly MicrophoneMonitor microphoneMonitor = new();
    readonly bool previewOnly;
    bool settingsReady;
    string? monitorError;
    double meterValue;
    public MainWindow(bool previewOnly = false)
    {
        this.previewOnly = previewOnly;
        InitializeComponent();
        try { if (!previewOnly && File.Exists(settingsPath)) preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsPath)) ?? new(); } catch { }
        if (string.IsNullOrWhiteSpace(preferences.Folder)) preferences.Folder = new Preferences().Folder;
        FpsCombo.SelectedIndex = Math.Clamp(preferences.Fps, 0, 2); QualityCombo.SelectedIndex = Math.Clamp(preferences.Quality, 0, 2); ApiCombo.SelectedIndex = Math.Clamp(preferences.Api, 0, 1);
        ResolutionCombo.SelectedIndex = Math.Clamp(preferences.Resolution, 0, 4);
        MicVolumeSlider.Value = double.IsFinite(preferences.MicrophoneVolume) ? Math.Clamp(preferences.MicrophoneVolume, 0, 100) : 100;
        MicVolumeLabel.Text = $"{MicVolumeSlider.Value:0}%";
        var microphones = new List<MicrophoneItem> { new("Windows 預設麥克風", "") };
        try {
            using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            foreach (var device in devices.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active)) {
                using (device) microphones.Add(new MicrophoneItem(device.FriendlyName, device.ID));
            }
        } catch { }
        MicDeviceCombo.ItemsSource = microphones;
        MicDeviceCombo.SelectedIndex = Math.Max(0, microphones.FindIndex(d => d.Device == preferences.MicrophoneDevice));
        SystemCheck.IsChecked = preferences.SystemAudio; MicCheck.IsChecked = preferences.Microphone; CursorCheck.IsChecked = preferences.Cursor; CountdownCheck.IsChecked = preferences.Countdown;
        FolderLabel.Text = preferences.Folder;
        if (string.IsNullOrWhiteSpace(preferences.PictureFolder)) preferences.PictureFolder = new Preferences().PictureFolder;
        PictureFolderLabel.Text = preferences.PictureFolder;
        settingsReady = true;
        session.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateState));
        session.Finished += (message, ok) => Dispatcher.BeginInvoke(new Action(() => Finish(message, ok)));
        timer.Tick += (_, _) => {
            try { session.ApplyPendingChanges(); } catch (Exception ex) { StatusText.Text = "錄製設定失敗：" + ex.Message; }
            UpdateMicrophoneMonitor();
            bar?.Update();
            if (session.State != CaptureState.Idle) {
                ReadyLabel.Text = session.State == CaptureState.Paused ? "Ⅱ  已暫停" : "●  錄製中";
                if (mode == "Window" && SourceCombo.SelectedItem is SourceItem s && (!Native.IsWindow(s.Handle) || Native.IsIconic(s.Handle))) {
                    if (!Native.IsWindow(s.Handle)) { StatusText.Text = "來源視窗已關閉，正在停止並儲存。"; session.Stop(); }
                    else StatusText.Text = "來源視窗已最小化，可能暫停更新；請還原視窗以繼續擷取。";
                }
            }
            else if (!preparing && IsVisible && ++previewTicks % 12 == 0 && mode != "Window") RefreshPreview();
        };
        SourceInitialized += (_, _) => {
            hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            if (previewOnly) return;
            bool first = Native.RegisterHotKey(hwnd, 1, 0x4006, 0x78);
            bool second = Native.RegisterHotKey(hwnd, 2, 0x4006, 0x79);
            if (!first || !second) StatusText.Text = "部分快捷鍵被其他程式佔用；請使用浮動控制列。";
            Native.Exclude(this); CreateTray();
        };
        Loaded += (_, _) => { UpdateMode(); RefreshSources(); UpdateEncodingHint(); timer.Start(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) microphoneMonitor.Dispose(); };
        Closing += (_, e) => {
            if (selectingSource) { e.Cancel = true; return; }
            if (photographing) { e.Cancel = true; StatusText.Text = "正在儲存截圖，請稍候。"; return; }
            if (preparing) { cancelPrepare = true; e.Cancel = true; return; }
            if (session.State != CaptureState.Idle) { e.Cancel = true; exitAfterStop = true; session.Stop(); return; }
            SavePreferences(); timer.Stop(); microphoneMonitor.Dispose(); CloseSourcePicker(); bar?.Close(); tray?.Dispose(); trayIcon?.Dispose(); session.Dispose();
            Native.UnregisterHotKey(hwnd, 1); Native.UnregisterHotKey(hwnd, 2); System.Windows.Application.Current.Shutdown();
        };
    }
    IntPtr WndProc(IntPtr window, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if ((uint)msg == Native.ShowMainMessage) { Restore(); handled = true; return IntPtr.Zero; }
        if (msg == 0x0312) {
            handled = true;
            if (wparam.ToInt32() == 1 && !photographing && !selectingSource) { if (session.State == CaptureState.Idle) _ = StartRecording(); else session.PauseResume(); }
            if (wparam.ToInt32() == 2) { cancelPrepare = true; session.Stop(); }
        }
        return IntPtr.Zero;
    }
    void CreateTray() {
        trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Amos錄影與截圖", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("開啟 Amos錄影與截圖", null, (_, _) => Restore());
        menu.Items.Add("暫停 / 繼續", null, (_, _) => session.PauseResume());
        menu.Items.Add("停止並儲存", null, (_, _) => { cancelPrepare = true; session.Stop(); });
        menu.Items.Add("結束", null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Restore();
    }
    void SavePreferences() {
        if (!settingsReady) return;
        preferences.Fps = FpsCombo.SelectedIndex; preferences.Quality = QualityCombo.SelectedIndex; preferences.Api = ApiCombo.SelectedIndex;
        preferences.SystemAudio = SystemCheck.IsChecked == true; preferences.Microphone = MicCheck.IsChecked == true;
        preferences.Cursor = CursorCheck.IsChecked == true; preferences.Countdown = CountdownCheck.IsChecked == true;
        preferences.Resolution = ResolutionCombo.SelectedIndex; preferences.MicrophoneVolume = MicVolumeSlider.Value;
        preferences.MicrophoneDevice = (MicDeviceCombo.SelectedItem as MicrophoneItem)?.Device ?? "";
        if (previewOnly) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }
    void UpdateMode() {
        foreach (var button in new[] { ScreenMode, RegionMode, WindowMode }) {
            bool selected = (string)button.Tag == mode;
            button.Background = (Brush)new BrushConverter().ConvertFrom(selected ? "#2B261C" : "#191C21")!;
            button.BorderBrush = (Brush)new BrushConverter().ConvertFrom(selected ? "#C8A96D" : "#34373E")!;
            button.Foreground = selected ? (Brush)FindResource("Gold") : Brushes.LightGray;
        }
        SelectRegionButton.Visibility = mode == "Region" ? Visibility.Visible : Visibility.Collapsed;
        ApiCombo.IsEnabled = mode != "Window";
        StatusText.Text = mode switch {
            "Window" => "只錄指定視窗。請保持視窗開啟；最小化可能停止畫面更新。",
            "Region" => "固定錄製選定範圍，程式或分頁切換會一起錄下。",
            _ => "螢幕模式會錄下切換程式與分頁後的畫面。"
        };
    }
    internal List<SourceItem> ListSources(string sourceMode) {
        var items = new List<SourceItem>();
        if (sourceMode == "Window") {
            int own = Environment.ProcessId;
            foreach (var w in Recorder.GetWindows().Where(w => w.Pid != own && !w.IsMinmimized())) {
                Native.GetWindowRect(w.Handle, out var r);
                if (r.Right > r.Left && r.Bottom > r.Top) items.Add(new SourceItem(w.Title, "", w.Handle, Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)));
            }
        } else {
            int i = 1;
            foreach (var display in Forms.Screen.AllScreens.OrderByDescending(x => x.Primary))
                items.Add(new SourceItem($"螢幕 {i++}{(display.Primary ? " · 主螢幕" : "")}  /  {display.Bounds.Width} × {display.Bounds.Height}", display.DeviceName, IntPtr.Zero, display.Bounds));
        }
        return items;
    }
    void RefreshSources() {
        if (preparing || photographing || selectingSource || session.State != CaptureState.Idle) return;
        try {
            var previous = SourceCombo.SelectedItem as SourceItem;
            var items = ListSources(mode);
            SourceCombo.ItemsSource = items;
            SourceCombo.SelectedIndex = items.Count == 0 ? -1 : Math.Max(0, items.FindIndex(x => mode == "Window" ? x.Handle == previous?.Handle : x.Device == previous?.Device));
            if (items.Count == 0) { SourceInfo.Text = "沒有可錄製的視窗，請先開啟程式再重新整理。"; Preview.Source = null; PreviewPlaceholder.Visibility = Visibility.Visible; }
        } catch (Exception ex) { StatusText.Text = "無法列出來源：" + ex.Message; }
    }
    void RefreshPreview() {
        if (previewOnly) { Preview.Source = null; PreviewPlaceholder.Visibility = Visibility.Visible; PreviewBadge.Text = "介面預覽"; return; }
        if (!IsLoaded || session.State != CaptureState.Idle || preparing || SourceCombo.SelectedItem is not SourceItem source) return;
        if (mode == "Window") { Preview.Source = null; PreviewPlaceholder.Visibility = Visibility.Visible; PreviewBadge.Text = "指定視窗 · Windows Graphics Capture"; SourceInfo.Text = "指定視窗  /  " + source.Label; return; }
        var rect = source.Bounds;
        if (mode == "Region" && region is Rectangle crop) rect = new Rectangle(source.Bounds.X + crop.X, source.Bounds.Y + crop.Y, crop.Width, crop.Height);
        SourceInfo.Text = mode == "Region" && region is null ? "尚未框選  /  按「框選範圍」開始" : $"{rect.Width} × {rect.Height} px  /  {(mode == "Region" ? "固定範圍" : "完整螢幕")}";
        PreviewBadge.Text = mode == "Region" ? "範圍預覽" : "螢幕預覽";
        try {
            using var full = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(full)) g.CopyFromScreen(rect.Location, System.Drawing.Point.Empty, rect.Size);
            int w = Math.Min(960, rect.Width); int h = Math.Max(1, rect.Height * w / rect.Width);
            using var small = new Bitmap(full, w, h); using var stream = new MemoryStream();
            small.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp); stream.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            Preview.Source = image; PreviewPlaceholder.Visibility = Visibility.Collapsed;
        } catch { Preview.Source = null; PreviewPlaceholder.Visibility = Visibility.Visible; }
    }
    async Task StartRecording() {
        if (preparing || photographing || selectingSource) return;
        if (session.State != CaptureState.Idle) { ShowBar(); Hide(); return; }
        if (SourceCombo.SelectedItem is not SourceItem source) { StatusText.Text = "請先選擇有效的錄製來源。"; return; }
        if (mode == "Region" && region is null) { PickRegion(); if (region is null) return; }
        if (mode == "Window" && (!Native.IsWindow(source.Handle) || Native.IsIconic(source.Handle))) { StatusText.Text = "請還原要錄製的視窗，或重新選擇來源。"; return; }
        CloseSourcePicker(); SavePreferences(); ShowBar(); Hide(); preparing = true; cancelPrepare = false; bar?.SetPreparing(true); UpdateState();
        try {
            if (preferences.Countdown) {
                for (int i = 3; i > 0; i--) { StatusText.Text = $"{i} 秒後開始錄製… Ctrl + Shift + F10 取消"; bar?.SetNotice($"{i} 秒"); await Task.Delay(1000); if (cancelPrepare) return; }
            }
            var recordingSource = CreateCaptureSource();
            session.DisposeRecorder();
            Hide();
            await Task.Delay(250);
            if (cancelPrepare) return;
            var size = OutputDimensions();
            int frames = VideoProfile.Frames(FpsCombo.SelectedIndex);
            session.Start(recordingSource, preferences.Folder, frames, VideoProfile.Bitrate(QualityCombo.SelectedIndex, frames, size.Width, size.Height), preferences.SystemAudio, preferences.Microphone, preferences.Cursor, VideoProfile.Height(ResolutionCombo.SelectedIndex), (float)(MicVolumeSlider.Value / 100), preferences.MicrophoneDevice);
            ShowBar();
        } catch (Exception ex) { Restore(); StatusText.Text = "無法開始錄製：" + ex.Message; }
        finally { preparing = false; bar?.SetPreparing(false); UpdateState(); if (cancelPrepare && session.State == CaptureState.Idle) StatusText.Text = "已取消倒數。"; }
    }
    void ShowBar() {
        if (bar is null) {
            bar = new FloatingBar(session, Restore, StartRecording, TakeScreenshot, ToggleToolbarMicrophone, OpenToolbarSources,
                () => MicCheck.IsChecked == true, () => mode switch { "Region" => "範圍", "Window" => "視窗", _ => "螢幕" }, ToggleToolbarSystemAudio, () => SystemCheck.IsChecked == true);
            bar.Closed += (_, _) => { CloseSourcePicker(); bar = null; };
        }
        bar.Show(); bar.Update();
        if (!bar.Excluded) StatusText.Text = "此系統無法排除控制列，控制列可能出現在影片裡。";
    }
    void CloseSourcePicker() { sourcePicker?.Close(); sourcePicker = null; }
    internal void OpenToolbarSources() {
        if (preparing || photographing || selectingSource || session.State is CaptureState.Starting or CaptureState.Stopping || bar == null) return;
        if (sourcePicker != null) { CloseSourcePicker(); return; }
        sourcePicker = new SourcePicker(mode, SourceCombo.SelectedItem as SourceItem, ListSources, ApplyToolbarSource,
            session.State != CaptureState.Idle ? session.IsWindowSource : null) { Owner = bar };
        var work = SystemParameters.WorkArea;
        sourcePicker.Left = Math.Clamp(bar.Left >= sourcePicker.Width + 10 ? bar.Left - sourcePicker.Width - 10 : bar.Left + bar.Width + 10, work.Left, Math.Max(work.Left, work.Right - sourcePicker.Width));
        sourcePicker.Top = Math.Clamp(bar.Top + 30, work.Top, Math.Max(work.Top, work.Bottom - sourcePicker.Height));
        sourcePicker.Closed += (_, _) => sourcePicker = null;
        sourcePicker.Show(); sourcePicker.Activate();
    }
    internal bool ApplyToolbarSource(string nextMode, SourceItem nextSource) {
        if (preparing || photographing || selectingSource || session.State is CaptureState.Starting or CaptureState.Stopping) return false;
        Rectangle? nextRegion = null;
        try {
            var currentSources = ListSources(nextMode);
            var valid = currentSources.FirstOrDefault(x => nextMode == "Window" ? x.Handle == nextSource.Handle : x.Device == nextSource.Device)
                ?? throw new InvalidOperationException("來源已不可用，請重新選擇。");
            if (nextMode == "Region") {
                selectingSource = true; bar?.SetSelecting(true); Hide(); bar?.Hide();
                try {
                    if (RegionSelectionOverride != null) nextRegion = RegionSelectionOverride(valid.Bounds);
                    else {
                        var picker = new RegionPicker(valid.Bounds);
                        if (picker.ShowDialog() == true) nextRegion = picker.Selection;
                    }
                } finally { selectingSource = false; bar?.Show(); bar?.SetSelecting(false); }
                if (nextRegion == null) return false;
            }
            if (session.State is CaptureState.Recording or CaptureState.Paused) session.SwitchSource(CreateCaptureSource(nextMode, valid, nextRegion));
            mode = nextMode; UpdateMode();
            SourceCombo.ItemsSource = currentSources; SourceCombo.SelectedItem = valid;
            region = nextRegion; RefreshPreview(); UpdateEncodingHint(); SavePreferences(); bar?.Update();
            UpdateState(); bar?.SetNotice(session.State == CaptureState.Paused ? "繼續套用" : "已切換"); return true;
        } catch (Exception ex) { StatusText.Text = ex.Message; bar?.SetNotice("來源失效"); return false; }
    }
    internal void ToggleToolbarMicrophone() {
        if (preparing || selectingSource || session.State is CaptureState.Starting or CaptureState.Stopping) return;
        bool enable = MicCheck.IsChecked != true;
        try {
            if (enable && !previewOnly && session.State == CaptureState.Idle) {
                using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                string selected = (MicDeviceCombo.SelectedItem as MicrophoneItem)?.Device ?? "";
                using var device = string.IsNullOrEmpty(selected) ? devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console) : devices.GetDevice(selected);
                if (device.State != NAudio.CoreAudioApi.DeviceState.Active) throw new InvalidOperationException("麥克風尚未連接或已停用。");
            }
            MicCheck.IsChecked = enable;
            bar?.SetNotice(MicCheck.IsChecked == enable ? (enable ? "人聲開" : "人聲關") : "無麥克風");
        } catch (Exception ex) { StatusText.Text = "無法開啟麥克風：" + ex.Message; bar?.SetNotice("無麥克風"); }
        bar?.Update();
    }
    internal void ToggleToolbarSystemAudio() {
        if (preparing || selectingSource || session.State is CaptureState.Starting or CaptureState.Stopping) return;
        bool enable = SystemCheck.IsChecked != true;
        SystemCheck.IsChecked = enable;
        bar?.SetNotice(SystemCheck.IsChecked == enable ? (enable ? "系統音開" : "系統音關") : "無音訊裝置");
        bar?.Update();
    }
    void SystemAudioChanged(object sender, RoutedEventArgs e) {
        if (!settingsReady) return;
        try { session.SetSystemAudio(SystemCheck.IsChecked == true); SavePreferences(); }
        catch (Exception ex) {
            settingsReady = false; SystemCheck.IsChecked = false; settingsReady = true;
            StatusText.Text = ex.Message; bar?.SetNotice("音訊失敗"); SavePreferences();
        }
        bar?.Update();
    }
    void UpdateState() {
        bool idle = session.State == CaptureState.Idle && !preparing && !photographing && !selectingSource;
        foreach (var control in new Control[] { ScreenMode, RegionMode, WindowMode, SourceCombo, SelectRegionButton, FpsCombo, QualityCombo, ResolutionCombo, CursorCheck, CountdownCheck, ApiCombo, SaveFolderButton, PictureFolderButton, MicDeviceCombo }) control.IsEnabled = idle;
        SystemCheck.IsEnabled = MicCheck.IsEnabled = MicVolumeSlider.IsEnabled = !preparing && !selectingSource && session.State is CaptureState.Idle or CaptureState.Recording or CaptureState.Paused;
        if (mode == "Window") ApiCombo.IsEnabled = false;
        StartButton.IsEnabled = !preparing && !photographing && session.State != CaptureState.Stopping;
        StartButton.Content = "開始使用";
        ReadyLabel.Text = preparing ? "◷  準備開始" : session.State switch { CaptureState.Idle => "●  準備就緒", CaptureState.Paused => "Ⅱ  已暫停", CaptureState.Stopping => "◷  儲存中", _ => "●  錄製中" };
        bar?.Update();
    }
    void Finish(string message, bool ok) {
        session.DisposeRecorder(); bar?.Update();
        if (exitAfterStop) { if (!ok) MessageBox.Show("錄影儲存失敗：" + message, "Amos錄影與截圖"); Close(); return; }
        if (!ok) Restore(); else bar?.SetNotice("已儲存"); StatusText.Text = ok ? "已儲存：" + Path.GetFileName(message) : "錄製失敗：" + message + "；可降低影格率／解析度，或切換擷取引擎。";
        if (ok && tray != null) { tray.BalloonTipTitle = "Amos錄影與截圖 · 錄影已儲存"; tray.BalloonTipText = Path.GetFileName(message); tray.ShowBalloonTip(4000); }
        UpdateState();
    }
    void Restore() { CloseSourcePicker(); Show(); WindowState = WindowState.Normal; Activate(); }
    void PickRegion() {
        if (SourceCombo.SelectedItem is not SourceItem s) return;
        Hide(); var picker = new RegionPicker(s.Bounds);
        if (picker.ShowDialog() == true) region = picker.Selection;
        Restore(); RefreshPreview(); UpdateEncodingHint();
    }
    void TitleDrag(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is TextBlock or Grid) { if (e.ClickCount == 2) MaximizeClick(sender, e); else DragMove(); } }
    void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CloseClick(object sender, RoutedEventArgs e) => Close();
    void ModeClick(object sender, RoutedEventArgs e) { if (session.State != CaptureState.Idle || preparing) return; mode = (string)((Button)sender).Tag; region = null; UpdateMode(); RefreshSources(); }
    void SourceChanged(object sender, SelectionChangedEventArgs e) { region = null; RefreshPreview(); UpdateEncodingHint(); }
    void RefreshClick(object sender, RoutedEventArgs e) => RefreshSources();
    void SelectRegionClick(object sender, RoutedEventArgs e) => PickRegion();
    void StartClick(object sender, RoutedEventArgs e) => BeginUsing();
    public void BeginUsing() {
        if (preparing || photographing || selectingSource) return;
        if (SourceCombo.SelectedItem is not SourceItem) { StatusText.Text = "請先選擇來源。"; return; }
        if (mode == "Region" && region is null) { PickRegion(); if (region is null) return; }
        SavePreferences(); ShowBar(); Hide();
    }
    RecordingSourceBase CreateCaptureSource() {
        if (SourceCombo.SelectedItem is not SourceItem source) throw new InvalidOperationException("請先選擇來源。");
        return CreateCaptureSource(mode, source, region);
    }
    RecordingSourceBase CreateCaptureSource(string captureMode, SourceItem source, Rectangle? crop) {
        if (captureMode == "Window") {
            if (!Native.IsWindow(source.Handle) || Native.IsIconic(source.Handle)) throw new InvalidOperationException("請還原來源視窗，或重新選擇來源。");
            return new WindowRecordingSource(source.Handle) { IsCursorCaptureEnabled = preferences.Cursor, IsBorderRequired = true };
        }
        var display = new DisplayRecordingSource(source.Device) { RecorderApi = preferences.Api == 0 ? RecorderApi.DesktopDuplication : RecorderApi.WindowsGraphicsCapture, IsCursorCaptureEnabled = preferences.Cursor, IsBorderRequired = true };
        if (captureMode == "Region" && crop is Rectangle r) display.SourceRect = new ScreenRect(r.X, r.Y, r.Width, r.Height);
        return display;
    }
    async Task TakeScreenshot() {
        if (photographing || preparing || selectingSource || session.State is CaptureState.Starting or CaptureState.Stopping) return;
        CloseSourcePicker(); photographing = true; bar?.SetPhotographing(true); UpdateState();
        string path = Path.Combine(preferences.PictureFolder, "Amos_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
        try {
            SavePreferences();
            if (session.State == CaptureState.Recording) await session.SnapshotAsync(path);
            else {
                var source = CreateCaptureSource();
                Hide(); await Task.Delay(150);
                await ScreenshotCapture.SaveAsync(source, path, preferences.Cursor);
            }
            bar?.SetNotice("已截圖"); StatusText.Text = "圖片已儲存：" + Path.GetFileName(path);
        } catch (Exception ex) { bar?.SetNotice("失敗"); Restore(); StatusText.Text = "截圖失敗：" + ex.Message; }
        finally { photographing = false; bar?.SetPhotographing(false); UpdateState(); }
    }
    void PictureFolderClick(object sender, RoutedEventArgs e) {
        try { Directory.CreateDirectory(preferences.PictureFolder); Process.Start(new ProcessStartInfo("explorer.exe", preferences.PictureFolder) { UseShellExecute = true }); } catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    void ChangePictureFolderClick(object sender, RoutedEventArgs e) {
        using var dialog = new Forms.FolderBrowserDialog { Description = "選擇截圖儲存資料夾", UseDescriptionForTitle = true, SelectedPath = preferences.PictureFolder };
        if (dialog.ShowDialog(new NativeWindowOwner(hwnd)) == Forms.DialogResult.OK) {
            preferences.PictureFolder = dialog.SelectedPath; PictureFolderLabel.Text = preferences.PictureFolder; SavePreferences();
        }
    }
    void FolderClick(object sender, RoutedEventArgs e) { try { Directory.CreateDirectory(preferences.Folder); Process.Start(new ProcessStartInfo("explorer.exe", preferences.Folder) { UseShellExecute = true }); } catch (Exception ex) { StatusText.Text = ex.Message; } }
    void ChangeFolderClick(object sender, RoutedEventArgs e) {
        if (session.State != CaptureState.Idle || preparing) return;
        using var dialog = new Forms.FolderBrowserDialog { Description = "選擇影片儲存資料夾", UseDescriptionForTitle = true, SelectedPath = preferences.Folder };
        if (dialog.ShowDialog(new NativeWindowOwner(hwnd)) == Forms.DialogResult.OK) {
            preferences.Folder = dialog.SelectedPath; FolderLabel.Text = preferences.Folder; SavePreferences();
            StatusText.Text = "之後的影片將儲存到選定資料夾。";
        }
    }
    public void OpenMicrophonePanel() { if (MicPanel.Visibility != Visibility.Visible) MicSidebarClick(this, new RoutedEventArgs()); }
    void MicSidebarClick(object sender, RoutedEventArgs e)
    {
        bool open = MicPanel.Visibility != Visibility.Visible;
        double oldWidth = ActualWidth;
        MicPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MicColumn.Width = new GridLength(open ? 250 : 0);
        MinWidth = open ? Math.Min(1130, SystemParameters.WorkArea.Width) : 880;
        if (WindowState == WindowState.Normal) Width = Math.Clamp(oldWidth + (open ? 250 : -250), MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        if (!open) microphoneMonitor.Dispose();
    }
    void MicSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!settingsReady) return;
        monitorError = null;
        try { session.SetMicrophone(MicCheck.IsChecked == true, (float)(MicVolumeSlider.Value / 100)); SavePreferences(); }
        catch (Exception ex) { MicCheck.IsChecked = false; StatusText.Text = ex.Message; }
        UpdateMicrophoneMonitor();
    }
    void MicVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MicVolumeLabel != null) MicVolumeLabel.Text = $"{e.NewValue:0}%";
        if (!settingsReady) return;
        try { session.SetMicrophone(MicCheck.IsChecked == true, (float)(e.NewValue / 100)); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    void MicDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!settingsReady) return;
        microphoneMonitor.Dispose(); monitorError = null; SavePreferences();
    }
    void UpdateMicrophoneMonitor()
    {
        if (!settingsReady) return;
        bool enabled = MicCheck.IsChecked == true;
        if (previewOnly || !IsVisible || MicPanel.Visibility != Visibility.Visible || !enabled) {
            microphoneMonitor.Dispose(); MicMeter.Value = 0; meterValue = 0;
            MicMeterLabel.Text = enabled ? "開啟側欄時顯示音量" : "麥克風未錄製";
            return;
        }
        if (monitorError != null) { MicMeterLabel.Text = monitorError; return; }
        try {
            if (!microphoneMonitor.IsRunning) microphoneMonitor.Start((MicDeviceCombo.SelectedItem as MicrophoneItem)?.Device ?? "");
            double peak = microphoneMonitor.Peak * MicVolumeSlider.Value / 100;
            meterValue = Math.Max(peak, meterValue * 0.65); MicMeter.Value = Math.Min(100, meterValue * 100);
            MicMeterLabel.Text = peak > 0.001 ? $"{20 * Math.Log10(peak):0.0} dB · 輸入音量" : "等待說話…";
        } catch (Exception ex) { microphoneMonitor.Dispose(); monitorError = "無法讀取麥克風，請檢查裝置及 Windows 麥克風權限。"; MicMeterLabel.Text = monitorError; MicMeter.ToolTip = ex.Message; }
    }
    System.Drawing.Size OutputDimensions()
    {
        if (session.State != CaptureState.Idle && session.OutputSize is System.Drawing.Size activeSize) return activeSize;
        int height = VideoProfile.Height(ResolutionCombo.SelectedIndex);
        if (height > 0) return new System.Drawing.Size(height * 16 / 9, height);
        if (mode == "Region" && region is Rectangle r) return r.Size;
        return (SourceCombo.SelectedItem as SourceItem)?.Bounds.Size ?? new System.Drawing.Size(1920, 1080);
    }
    void VideoSettingsChanged(object sender, SelectionChangedEventArgs e) => UpdateEncodingHint();
    void UpdateEncodingHint()
    {
        if (!IsLoaded || EncodingHint == null || ResolutionCombo == null || FpsCombo == null || QualityCombo == null) return;
        var size = OutputDimensions(); int fps = VideoProfile.Frames(FpsCombo.SelectedIndex);
        string note = "";
        var sourceSize = mode == "Region" && region is Rectangle r ? r.Size : (SourceCombo.SelectedItem as SourceItem)?.Bounds.Size;
        if (VideoProfile.Height(ResolutionCombo.SelectedIndex) > 0 && sourceSize.HasValue && (size.Width > sourceSize.Value.Width || size.Height > sourceSize.Value.Height)) note = " · 來源較小，會放大輸出";
        EncodingHint.Text = $"{size.Width} × {size.Height} · {fps} FPS 目標 · 約 {VideoProfile.Bitrate(QualityCombo.SelectedIndex, fps, size.Width, size.Height) / 1_000_000} Mbps{note}";
    }
    void HelpClick(object sender, RoutedEventArgs e) => MessageBox.Show(
        "螢幕：錄製選定螢幕；切換程式、瀏覽器分頁都會跟著錄。\n\n範圍：選擇螢幕後拖曳框選，固定錄下該區域目前顯示的內容。範圍不跟隨視窗移動。\n\n視窗：只錄選定程式。遮住或切換到其他程式仍擷取來源視窗；請避免最小化或關閉來源。瀏覽器會錄到目前在該視窗中顯示的分頁。\n\n遊戲：建議螢幕模式、60 FPS、Desktop Duplication。黑畫面時可改用無邊框視窗或 Windows Graphics Capture。受保護內容可能無法錄製。\n\nCtrl + Shift + F9：開始／暫停／繼續\nCtrl + Shift + F10：停止並儲存（也可取消倒數）\n\n按「開始使用」只會進入小側邊工具列，再按紅色錄影鍵才開始錄影。相機鍵可截圖，錄影中也可使用；暫停錄影時截圖會擷取來源目前的畫面。來源鍵可直接選螢幕、範圍或視窗，錄影中可切換其他螢幕或範圍；視窗模式可切換其他視窗，同一影片不中斷。螢幕與視窗模式之間請先停止再選。影片解析度在開始時固定，切換時等比縮放，必要時補黑邊。暫停時變更會於繼續後套用。麥克風鍵直接開關人聲，不會開啟設定面板；喇叭鍵直接開關系統音訊，與麥克風分開控制；主介面也可勾選「系統音訊」。金色表示開啟，斜線表示關閉。頂部可拖曳，最下方可開啟設定。工具列會從擷取畫面排除。\n\n麥克風側欄可選輸入裝置、開關人聲與調整音量。錄影中可修改開關及音量，裝置須在開始前選定。指定視窗的系統聲音仍包含所有應用程式。錄製途中請避免拔除音訊裝置或變更螢幕配置。\n\n影片儲存為 MP4、截圖為 PNG，兩種資料夾可分別選擇。預設使用 Windows 影片／AUREO 與圖片／AUREO。最高可選 120 FPS 及 2160p；實際流暢度取決於硬體，較小來源會放大輸出。停止錄影後請等待儲存完成。關閉主程式會先停止並儲存。", "Amos錄影與截圖 · 使用說明", MessageBoxButton.OK, MessageBoxImage.Information);
}
internal sealed class NativeWindowOwner : Forms.IWin32Window
{
    public IntPtr Handle { get; }
    public NativeWindowOwner(IntPtr handle) => Handle = handle;
}
