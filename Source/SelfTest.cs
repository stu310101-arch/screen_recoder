using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenRecorderLib;

namespace Aureo;
internal static class SelfTest
{
    public static async void Run(string[] args)
    {
        string folder = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "Aureo-self-test");
        Directory.CreateDirectory(folder);
        var reports = new List<object>();
        var label = new TextBlock { FontSize = 48, Text = "AUREO CAPTURE TEST", Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var target = new Window { Title = "Aureo test target", Width = 800, Height = 500, Left = 60, Top = 60, Content = label, Background = Brushes.DarkGoldenrod };
        int tick = 0;
        var animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        animation.Tick += (_, _) => { tick++; label.Text = "AUREO TEST " + tick; target.Background = tick % 10 < 5 ? Brushes.DarkGoldenrod : Brushes.DarkSlateBlue; };
        try {
            target.Show(); animation.Start(); await Task.Delay(1000);
            foreach (string mode in (args.Length > 2 ? new[] { args[2] } : new[] { "display", "region", "window", "wgc", "audio" })) {
                if (mode == "mic-options") {
                    var audio = new CaptureAudioSource("test-device");
                    audio.IsAudioCaptureEnabled = false; audio.Volume = 0.25f;
                    if (audio.IsAudioCaptureEnabled || Math.Abs(audio.Volume - 0.25f) > 0.001) throw new Exception("Microphone options failed");
                    audio.IsAudioCaptureEnabled = true; audio.Volume = 1;
                    if (!audio.IsAudioCaptureEnabled || audio.Volume != 1) throw new Exception("Microphone toggle failed");
                    reports.Add(new { mode, success = true, captureEnabledAndVolume = true }); continue;
                }
                using var capture = new CaptureSession();
                var done = new TaskCompletionSource<(string message, bool success)>();
                capture.Finished += (m, ok) => done.TrySetResult((m, ok));
                RecordingSourceBase source;
                if (mode is "window" or "shot-window") source = new WindowRecordingSource(new WindowInteropHelper(target).Handle);
                else {
                    var d = DisplayRecordingSource.MainMonitor;
                    d.RecorderApi = mode is "wgc" or "shot-wgc" ? RecorderApi.WindowsGraphicsCapture : RecorderApi.DesktopDuplication;
                    if (mode is "region" or "shot-region") d.SourceRect = new ScreenRect(80, 80, 640, 360);
                    source = d;
                }
                if (mode.StartsWith("shot-")) {
                    Window? coverShot = null;
                    if (mode == "shot-window") { coverShot = new Window { Background = Brushes.Crimson, Left = 60, Top = 60, Width = 800, Height = 500, Topmost = true }; coverShot.Show(); await Task.Delay(300); }
                    string picture = Path.Combine(folder, mode + ".png");
                    await ScreenshotCapture.SaveAsync(source, picture, false);
                    reports.Add(new { mode, success = true, picture, bytes = new FileInfo(picture).Length });
                    coverShot?.Close(); continue;
                }
                int fps = mode is "120fps" or "4k120" ? 120 : mode == "4k" ? 30 : 60;
                int outputHeight = mode == "120fps" ? 720 : mode is "4k" or "4k120" ? 2160 : 0;
                int bitrate = VideoProfile.Bitrate(1, fps, outputHeight == 0 ? 1920 : outputHeight * 16 / 9, outputHeight == 0 ? 1080 : outputHeight);
                capture.Start(source, folder, fps, bitrate, mode == "audio", false, true, outputHeight);
                for (int i = 0; i < 200 && capture.State == CaptureState.Starting && !done.Task.IsCompleted; i++) await Task.Delay(50);
                if (capture.State != CaptureState.Recording) throw new Exception(mode + " failed to start recording");
                await Task.Delay(1800);
                string? snapshot = null;
                if (mode is "display" or "120fps" or "4k" or "4k120") {
                    snapshot = Path.Combine(folder, "during-recording.png");
                    await capture.SnapshotAsync(snapshot);
                    if (capture.State != CaptureState.Recording) throw new Exception("Snapshot interrupted recording");
                }
                if (mode == "display") {
                    var floating = new FloatingBar(capture, () => { }); floating.Show();
                    await Task.Delay(200); floating.Update();
                    floating.UpdateLayout();
                    if (!floating.Excluded) throw new Exception("Floating controller capture exclusion failed");
                    var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)floating.ActualWidth, (int)floating.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    image.Render(floating); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using (var output = File.Create(Path.Combine(folder, "floating.png"))) encoder.Save(output);
                    floating.Hide();
                    capture.Finished += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(floating.Close));
                }
                capture.PauseResume();
                for (int i = 0; i < 40 && capture.State != CaptureState.Paused; i++) await Task.Delay(50);
                bool paused = capture.State == CaptureState.Paused;
                var before = capture.Elapsed;
                await Task.Delay(1200);
                bool frozen = Math.Abs((capture.Elapsed - before).TotalMilliseconds) < 100;
                if (mode == "window") {
                    await ScreenshotCapture.SaveAsync(new WindowRecordingSource(new WindowInteropHelper(target).Handle), Path.Combine(folder, "while-paused.png"), false);
                    if (capture.State != CaptureState.Paused) throw new Exception("Screenshot changed paused state");
                }
                if (mode == "microphone") capture.SetMicrophone(true, 0.25f);
                capture.PauseResume();
                for (int i = 0; i < 40 && capture.State != CaptureState.Recording; i++) await Task.Delay(50);
                capture.ApplyPendingChanges();
                Window? cover = null;
                if (mode == "window") { cover = new Window { Title = "Aureo occlusion test", Background = Brushes.Crimson, Left = 60, Top = 60, Width = 800, Height = 500, Topmost = true }; cover.Show(); }
                await Task.Delay(1800);
                if (mode is "window" or "region") {
                    snapshot = Path.Combine(folder, "during-recording.png");
                    await capture.SnapshotAsync(snapshot);
                    if (capture.State != CaptureState.Recording) throw new Exception("Snapshot interrupted recording");
                }
                if (mode == "microphone") { capture.SetMicrophone(false, 0); await Task.Delay(500); }
                capture.Stop();
                var winner = await Task.WhenAny(done.Task, Task.Delay(20000));
                if (winner != done.Task) throw new Exception(mode + " timed out finalizing");
                var result = await done.Task;
                reports.Add(new { mode, fps, outputHeight, snapshot, result.success, result.message, paused, timerFrozenDuringPause = frozen, activeSeconds = capture.Elapsed.TotalSeconds, bytes = result.success ? new FileInfo(result.message).Length : 0 });
                cover?.Close();
                if (!result.success) throw new Exception(result.message);
                await Task.Delay(500);
            }
            File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { success = true, tests = reports }, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception e) { File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { success = false, error = e.ToString(), tests = reports }, new JsonSerializerOptions { WriteIndented = true })); }
        finally { animation.Stop(); target.Close(); System.Windows.Application.Current.Shutdown(); }
    }
}
