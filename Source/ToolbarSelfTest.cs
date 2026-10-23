using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Aureo;
internal static class ToolbarSelfTest
{
    public static void ShowTarget(bool blue = false)
    {
        int tick = 0;
        var label = new TextBlock { Text = "AUREO SOURCE TEST", FontSize = 34, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var target = new Window { Title = "AUREO SOURCE TEST", Width = 800, Height = 500, Left = 60, Top = 60, Content = label, Background = Brushes.DarkGoldenrod };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { tick++; label.Text = "AUREO SOURCE " + tick; target.Background = blue ? Brushes.Teal : Brushes.DarkGoldenrod; if (tick > 900) target.Close(); };
        target.Closed += (_, _) => { timer.Stop(); System.Windows.Application.Current.Shutdown(); };
        target.Show(); timer.Start();
    }
    public static async void Run(string[] args)
    {
        string folder = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "Aureo-toolbar-test");
        Directory.CreateDirectory(folder);
        Process? target = null, second = null; MainWindow? main = null;
        using var tone = new WasapiOut();
        try {
            target = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--toolbar-target") { UseShellExecute = false });
            await Wait(() => { target!.Refresh(); return target.MainWindowHandle != IntPtr.Zero; });
            var targetHandle = target!.MainWindowHandle;
            second = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--toolbar-target blue") { UseShellExecute = false });
            await Wait(() => { second!.Refresh(); return second.MainWindowHandle != IntPtr.Zero; });
            main = new MainWindow(true); main.SetTestDestinations(folder); main.Show(); await Task.Delay(300);
            main.ApiCombo.SelectedIndex = args.Length > 2 && args[2] == "wgc" ? 1 : 0;
            main.BeginUsing(); var bar = main.Toolbar ?? throw new Exception("Toolbar missing");
            Check(main.CaptureStatus == CaptureState.Idle && !main.IsVisible, "BeginUsing started recording or showed main window");
            main.MicCheck.IsChecked = false;
            Click(bar.MicrophoneButton); Check(main.MicCheck.IsChecked == true && !main.IsVisible && main.MicPanel.Visibility != Visibility.Visible, "Mic click opened settings or did not enable voice");
            Render(bar, Path.Combine(folder, "toolbar-mic-on.png"));
            Click(bar.MicrophoneButton); Check(main.MicCheck.IsChecked == false && !main.IsVisible, "Mic click did not disable voice");
            Render(bar, Path.Combine(folder, "toolbar-mic-off.png"));
            Click(bar.SourceButton);
            var picker = main.ToolbarSourcePicker ?? throw new Exception("Source selector missing");
            Check(picker.Excluded && !main.IsVisible, "Source selector not excluded or opened main window");
            Render(picker, Path.Combine(folder, "source-selector.png"));
            picker.SelectMode("Window");
            var windowSource = picker.Sources.Items.Cast<SourceItem>().First(x => x.Handle == targetHandle);
            picker.Sources.SelectedItem = windowSource; Click(picker.ConfirmButton);
            Check(main.SourceMode == "Window" && main.ToolbarSourcePicker == null && !main.IsVisible, "Window selection failed");
            await TakePicture(main, bar);

            Click(bar.SourceButton); picker = main.ToolbarSourcePicker!; picker.SelectMode("Region");
            main.RegionSelectionOverride = _ => new System.Drawing.Rectangle(80, 80, 640, 360);
            Click(picker.ConfirmButton);
            Check(main.SourceMode == "Region" && main.SelectedRegion?.Width == 640 && !main.IsVisible, "Region selection failed");
            await TakePicture(main, bar);
            var previousRegion = main.SelectedRegion;
            Click(bar.SourceButton); picker = main.ToolbarSourcePicker!; picker.SelectMode("Region");
            main.RegionSelectionOverride = _ => null; Click(picker.ConfirmButton);
            Check(main.SelectedRegion == previousRegion && picker.IsVisible && !main.IsVisible, "Cancel lost the previous selection or opened main window");
            picker.SelectMode("Screen"); Click(picker.ConfirmButton);
            Check(main.SourceMode == "Screen" && main.SelectedRegion == null && !main.IsVisible, "Screen selection failed");

            Click(bar.SourceButton); picker = main.ToolbarSourcePicker!; picker.SelectMode("Window");
            picker.Sources.SelectedItem = picker.Sources.Items.Cast<SourceItem>().First(x => x.Handle == targetHandle);
            Click(picker.ConfirmButton);
            Click(bar.RecordButton); await Wait(() => main.CaptureStatus == CaptureState.Recording);
            Check(bar.SourceButton.IsEnabled && bar.MicrophoneButton.IsEnabled && bar.SystemAudioButton.IsEnabled && main.SystemCheck.IsEnabled, "Live controls unavailable");
            string windowVideo = main.CurrentRecordingPath;
            await Task.Delay(1200); await TakePicture(main, bar);
            Choose(main, bar, "Window", second!.MainWindowHandle);
            await Task.Delay(1200); await TakePicture(main, bar);
            Choose(main, bar, "Window", targetHandle);
            await Task.Delay(1000); await TakePicture(main, bar);
            Check(main.CaptureStatus == CaptureState.Recording && !main.IsVisible, "Snapshot interrupted recording or opened main window");
            await Task.Delay(1000); Click(bar.RecordButton); await Wait(() => main.CaptureStatus == CaptureState.Idle); await Task.Delay(300);
            Check(bar.SourceButton.IsEnabled && bar.IsVisible && !main.IsVisible, "Toolbar not ready after stopping");

            Choose(main, bar, "Screen");
            main.SystemCheck.IsChecked = false;
            var signal = new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.12, Type = SignalGeneratorType.Sin };
            tone.Init(signal); tone.Play();
            Click(bar.RecordButton); await Wait(() => main.CaptureStatus == CaptureState.Recording);
            string displayVideo = main.CurrentRecordingPath;
            var segments = new List<object>();
            var elapsed = Stopwatch.StartNew();
            await Task.Delay(2200);
            segments.Add(new { time = elapsed.Elapsed.TotalSeconds, systemAudio = true });
            Click(bar.SystemAudioButton);
            Check(main.SystemCheck.IsChecked == true && !main.IsVisible, "Speaker did not enable audio directly");
            Render(bar, Path.Combine(folder, "toolbar-audio-on.png"));
            main.RegionSelectionOverride = _ => new System.Drawing.Rectangle(80, 80, 640, 360);
            Choose(main, bar, "Region");
            await Task.Delay(2500); await TakePicture(main, bar);
            Check(main.CaptureStatus == CaptureState.Recording && main.CurrentRecordingPath == displayVideo, "Region switch restarted recording");
            segments.Add(new { time = elapsed.Elapsed.TotalSeconds, systemAudio = false });
            Click(bar.SystemAudioButton);
            Check(main.SystemCheck.IsChecked == false, "Speaker did not mute audio");
            Render(bar, Path.Combine(folder, "toolbar-audio-off.png"));
            Choose(main, bar, "Screen");
            await Task.Delay(2500); await TakePicture(main, bar);
            Click(bar.PauseButton); await Wait(() => main.CaptureStatus == CaptureState.Paused);
            main.RegionSelectionOverride = _ => new System.Drawing.Rectangle(100, 110, 320, 240);
            Choose(main, bar, "Region");
            main.SystemCheck.IsChecked = true;
            Check(main.CaptureStatus == CaptureState.Paused, "Source/audio changes resumed paused recording");
            await TakePicture(main, bar);
            segments.Add(new { time = elapsed.Elapsed.TotalSeconds, systemAudio = true });
            Click(bar.PauseButton); await Wait(() => main.CaptureStatus == CaptureState.Recording);
            await Task.Delay(2500); await TakePicture(main, bar);
            Choose(main, bar, "Screen");
            await Task.Delay(1500);
            Check(main.CurrentRecordingPath == displayVideo && main.CaptureStatus == CaptureState.Recording && !main.IsVisible, "Source switch interrupted recording or opened settings");
            Click(bar.RecordButton); await Wait(() => main.CaptureStatus == CaptureState.Idle); await Task.Delay(300);
            tone.Stop();
            Check(Directory.GetFiles(Path.Combine(folder, "Videos"), "*.mp4").Length == 2, "Source changes split videos");
            File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new {
                success = true, sourceModes = new[] { "Screen", "Region", "Window" }, microphoneTogglesWithoutOpeningMain = true,
                sourceSelectorExcluded = picker.Excluded, cancelPreservesRegion = true, snapshotDuringRecording = true,
                video = main.CurrentRecordingPath, windowVideo, displayVideo, audioSegments = segments,
                liveWindowSwitch = true, liveScreenRegionSwitch = true, pausedChangesAppliedOnResume = true,
                physicalDisplayCount = System.Windows.Forms.Screen.AllScreens.Length,
                displayApi = main.ApiCombo.SelectedIndex == 0 ? "DesktopDuplication" : "WindowsGraphicsCapture",
                pictures = Directory.GetFiles(Path.Combine(folder, "Pictures"), "*.png"), microphoneHardwareTested = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { success = false, error = ex.ToString() })); }
        finally {
            if (target != null) { try { if (!target.HasExited) target.Kill(); } catch { } target.Dispose(); }
            if (second != null) { try { if (!second.HasExited) second.Kill(); } catch { } second.Dispose(); }
            main?.Close(); System.Windows.Application.Current.Shutdown();
        }
    }
    static void Choose(MainWindow main, FloatingBar bar, string mode, IntPtr handle = default)
    {
        Click(bar.SourceButton);
        var picker = main.ToolbarSourcePicker ?? throw new Exception("Live source selector missing");
        picker.SelectMode(mode);
        if (mode == "Window") picker.Sources.SelectedItem = picker.Sources.Items.Cast<SourceItem>().First(x => x.Handle == handle);
        Click(picker.ConfirmButton);
        Check(main.SourceMode == mode && main.ToolbarSourcePicker == null && !main.IsVisible, "Live source selection failed");
    }
    static async Task TakePicture(MainWindow main, FloatingBar bar)
    {
        Click(bar.CameraButton); await Wait(() => !main.IsPhotographing);
        Check(!main.IsVisible, "Screenshot showed the main window");
    }
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static async Task Wait(Func<bool> condition)
    {
        for (int i = 0; i < 300; i++) { if (condition()) return; await Task.Delay(50); }
        throw new TimeoutException("Toolbar test timed out");
    }
    static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); png.Save(stream);
    }
}
