using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Aureo;
public partial class App : System.Windows.Application
{
    Mutex? instanceMutex;
    bool ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "Aureo-error.log"), DateTime.Now + " " + args.Exception + Environment.NewLine); } catch { }
            MessageBox.Show("發生錯誤：" + args.Exception.Message, "Amos錄影與截圖", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        if (e.Args.Length > 0 && e.Args[0] == "--toolbar-target") { ToolbarSelfTest.ShowTarget(e.Args.Length > 1); return; }
        if (e.Args.Length > 0 && e.Args[0] == "--toolbar-test") { ToolbarSelfTest.Run(e.Args); return; }
        if (e.Args.Length > 0 && e.Args[0] == "--self-test") { SelfTest.Run(e.Args); return; }
        if (!(e.Args.Length > 0 && e.Args[0] == "--preview-export")) {
            instanceMutex = new Mutex(true, @"Local\AureoRecorder.Desktop.v1", out ownsMutex);
            if (!ownsMutex) {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("AureoRecorder")) {
                    try { Native.AllowSetForegroundWindow((uint)process.Id); } catch { }
                    finally { process.Dispose(); }
                }
                Native.PostMessage(new IntPtr(0xffff), Native.ShowMainMessage, IntPtr.Zero, IntPtr.Zero);
                Shutdown(); return;
            }
        }
        bool preview = e.Args.Length > 0 && e.Args[0] == "--preview-export";
        var window = new MainWindow(preview);
        MainWindow = window;
        if (e.Args.Length > 1 && e.Args[0] == "--preview-export") {
            window.Loaded += async (_, _) => {
                if (e.Args.Length > 2 && e.Args[2] == "advanced") window.AdvancedSettings.IsExpanded = true;
                if (e.Args.Length > 2 && e.Args[2] == "microphone") { window.OpenMicrophonePanel(); window.FpsCombo.SelectedIndex = 2; window.ResolutionCombo.SelectedIndex = 4; }
                if (e.Args.Length > 2 && e.Args[2] == "toolbar") {
                    window.BeginUsing();
                    await System.Threading.Tasks.Task.Delay(250);
                    FloatingBar? floating = null;
                    foreach (Window item in Windows) if (item is FloatingBar found) floating = found;
                    if (window.CaptureStatus != CaptureState.Idle || window.IsVisible) throw new InvalidOperationException("BeginUsing unexpectedly started recording or kept main window visible");
                    if (floating == null) throw new InvalidOperationException("BeginUsing did not show toolbar");
                    floating.UpdateLayout();
                    var shot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)floating.ActualWidth, (int)floating.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    shot.Render(floating);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
                    using (var output = File.Create(e.Args[1])) png.Save(output);
                    File.WriteAllText(e.Args[1] + ".json", "{\"toolbarVisible\":true,\"recordingStarted\":false}");
                    window.Close(); return;
                }
                await System.Threading.Tasks.Task.Delay(1200);
                window.Preview.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 25 };
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = File.Create(e.Args[1])) encoder.Save(stream);
                window.Close();
            };
        }
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
