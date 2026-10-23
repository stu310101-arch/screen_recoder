using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ScreenRecorderLib;

namespace Aureo;
public enum CaptureState { Idle, Starting, Recording, Paused, Stopping }
public sealed class CaptureSession : IDisposable
{
    Recorder? recorder;
    CaptureAudioSource? microphoneSource;
    LoopbackAudioSource? systemSource;
    RecordingSourceBase? recordingSource;
    ScreenSize? canvasSize;
    bool microphonePending, systemPending, sourcePending;
    public bool IsWindowSource => recordingSource is WindowRecordingSource;
    public System.Drawing.Size? OutputSize => canvasSize == null ? null : new System.Drawing.Size((int)canvasSize.Width, (int)canvasSize.Height);
    public CaptureState State { get; private set; }
    public string FilePath { get; private set; } = "";
    readonly Stopwatch clock = new();
    public TimeSpan Elapsed => clock.Elapsed;
    public event Action? Changed;
    public event Action<string, bool>? Finished;
    public void Start(RecordingSourceBase source, string folder, int fps, int bitrate, bool system, bool mic, bool cursor, int outputHeight = 0, float microphoneVolume = 1, string microphoneDevice = "")
    {
        if (State != CaptureState.Idle) return;
        Directory.CreateDirectory(folder);
        // Fail before hiding the UI if the destination cannot be written.
        string probe = Path.Combine(folder, ".aureo-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        var options = RecorderOptions.Default;
        var size = Recorder.GetOutputDimensionsForRecordingSources(new[] { source }).CombinedOutputSize;
        if (size == null || size.Width < 2 || size.Height < 2) throw new InvalidOperationException("無法取得錄製來源的尺寸，請重新選擇。");
        canvasSize = outputHeight > 0 ? new ScreenSize(outputHeight * 16 / 9, outputHeight) : new ScreenSize((int)size.Width & ~1, (int)size.Height & ~1);
        PrepareSource(source);
        recordingSource = source;
        options.SourceOptions = new SourceOptions { RecordingSources = { source } };
        options.OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video, OutputFrameSize = canvasSize };
        options.VideoEncoderOptions = new VideoEncoderOptions {
            Framerate = fps, Bitrate = bitrate, IsFixedFramerate = true,
            IsHardwareEncodingEnabled = true, IsMp4FastStartEnabled = true,
            Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.UnconstrainedVBR, EncoderProfile = H264Profile.High }
        };
        microphoneSource = null;
        if (!string.IsNullOrEmpty(microphoneDevice)) microphoneSource = new CaptureAudioSource(microphoneDevice);
        else {
            try {
                using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                using var device = devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console);
                microphoneSource = new CaptureAudioSource(device.ID);
            }
            catch (Exception ex) { if (mic) throw new InvalidOperationException("找不到可用的預設麥克風，請在側欄選擇裝置。", ex); }
        }
        if (mic && microphoneSource == null) throw new InvalidOperationException("找不到麥克風，請連接裝置或在麥克風側欄關閉錄音。");
        systemSource = null;
        try {
            using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Console);
            systemSource = new LoopbackAudioSource(device.ID) { IsAudioCaptureEnabled = system, Volume = system ? 1 : 0 };
        } catch (Exception ex) { if (system) throw new InvalidOperationException("找不到預設播放裝置，請接上耳機／喇叭，或關閉系統聲音。", ex); }
        // Initialize both available devices, even when muted, so either can be enabled mid-recording.
        options.AudioOptions = new AudioOptions { IsAudioEnabled = systemSource != null || microphoneSource != null, Channels = AudioChannels.Stereo, Bitrate = AudioBitrate.bitrate_192kbps };
        if (systemSource != null) options.AudioOptions.AudioSources.Add(systemSource);
        if (microphoneSource != null) {
            microphoneSource.IsAudioCaptureEnabled = mic;
            microphoneSource.Volume = Math.Clamp(microphoneVolume, 0, 1);
            options.AudioOptions.AudioSources.Add(microphoneSource);
        }
        microphonePending = systemPending = sourcePending = false;
        options.MouseOptions = new MouseOptions { IsMousePointerEnabled = cursor, IsMouseClicksDetected = false };
        FilePath = Path.Combine(folder, "Amos_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".mp4");
        recorder = Recorder.CreateRecorder(options);
        recorder.OnStatusChanged += (_, e) => {
            switch (e.Status.ToString()) {
                case "Recording": if (State != CaptureState.Stopping) { State = CaptureState.Recording; clock.Start(); } break;
                case "Paused": if (State != CaptureState.Stopping) { State = CaptureState.Paused; clock.Stop(); } break;
                case "Finishing": State = CaptureState.Stopping; clock.Stop(); break;
            }
            Changed?.Invoke();
        };
        recorder.OnRecordingComplete += (_, e) => Complete(e.FilePath, true);
        recorder.OnRecordingFailed += (_, e) => Complete(e.Error, false);
        State = CaptureState.Starting; clock.Reset(); Changed?.Invoke();
        try { recorder.Record(FilePath); } catch { DisposeRecorder(); State = CaptureState.Idle; throw; }
    }
    void Complete(string message, bool success) { clock.Stop(); State = CaptureState.Idle; Changed?.Invoke(); Finished?.Invoke(message, success); }
    public void PauseResume() {
        if (State == CaptureState.Recording) recorder?.Pause();
        else if (State == CaptureState.Paused) recorder?.Resume();
    }
    public void Stop() {
        if (State is CaptureState.Recording or CaptureState.Paused or CaptureState.Starting) {
            State = CaptureState.Stopping; clock.Stop(); Changed?.Invoke(); recorder?.Stop();
        }
    }
    public async Task SnapshotAsync(string path)
    {
        ApplyPendingChanges();
        var current = recorder ?? throw new InvalidOperationException("錄影尚未啟動。");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SnapshotSavedEventArgs> saved = (_, _) => done.TrySetResult();
        current.OnSnapshotSaved += saved;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!current.TakeSnapshot(path)) throw new InvalidOperationException("目前無法截圖，請稍後再試。");
            await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("截圖檔案未成功儲存。");
        }
        finally { current.OnSnapshotSaved -= saved; }
    }
    public void SetMicrophone(bool enabled, float volume)
    {
        if (State == CaptureState.Idle) return;
        if (microphoneSource == null) { if (enabled) throw new InvalidOperationException("本次錄影開始時沒有麥克風，請停止後連接裝置再重新錄製。"); return; }
        microphoneSource.IsAudioCaptureEnabled = enabled;
        microphoneSource.Volume = Math.Clamp(volume, 0, 1);
        microphonePending = true; ApplyPendingChanges();
    }
    public void SetSystemAudio(bool enabled)
    {
        if (State == CaptureState.Idle) return;
        if (systemSource == null) { if (enabled) throw new InvalidOperationException("本次錄影開始時沒有播放裝置，請停止後連接耳機／喇叭再錄製。"); return; }
        systemSource.IsAudioCaptureEnabled = enabled;
        systemSource.Volume = enabled ? 1 : 0;
        systemPending = true; ApplyPendingChanges();
    }
    void PrepareSource(RecordingSourceBase source)
    {
        // Null cannot clear a native crop. Always send an explicit rectangle, including full-screen.
        if (source.SourceRect == null) {
            var size = Recorder.GetOutputDimensionsForRecordingSources(new[] { source }).CombinedOutputSize;
            if (size == null || size.Width < 2 || size.Height < 2) throw new InvalidOperationException("錄製來源已失效。");
            source.SourceRect = new ScreenRect(0, 0, size.Width, size.Height);
        }
        source.OutputSize = canvasSize;
        source.Position = new ScreenPoint(0, 0);
        source.Stretch = StretchMode.Uniform;
    }
    public void SwitchSource(RecordingSourceBase next)
    {
        if (State is not (CaptureState.Recording or CaptureState.Paused)) throw new InvalidOperationException("請等待錄影準備完成。");
        PrepareSource(next);
        // Reuse the original source and ID: native dynamic updates replace its input without restarting the encoder.
        if (recordingSource is DisplayRecordingSource display && next is DisplayRecordingSource nextDisplay) {
            display.DeviceName = nextDisplay.DeviceName;
            display.SourceRect = nextDisplay.SourceRect;
            // In 7.0.1, DD dirty rectangles retain pixels outside a changed crop. Changing
            // the tiny source preview forces a full canvas refresh without restarting video.
            if (display.RecorderApi == RecorderApi.DesktopDuplication) {
                display.VideoFramePreviewSize = new ScreenSize(2, 2);
                display.IsVideoFramePreviewEnabled = !display.IsVideoFramePreviewEnabled;
            }
        } else if (recordingSource is WindowRecordingSource window && next is WindowRecordingSource nextWindow) {
            window.Handle = nextWindow.Handle;
            window.SourceRect = nextWindow.SourceRect;
        } else throw new InvalidOperationException("錄影中可切換同類來源；螢幕與範圍可互換，視窗模式可切換其他視窗。跨模式請停止後再選。");
        sourcePending = true; ApplyPendingChanges();
    }
    public void ApplyPendingChanges()
    {
        if (State != CaptureState.Recording || recorder == null) return;
        var changes = recorder.GetDynamicOptionsBuilder();
        if (sourcePending && recordingSource != null) changes.SetUpdatedRecordingSource(recordingSource);
        if (microphonePending && microphoneSource != null) changes.SetUpdatedAudioSource(microphoneSource);
        if (systemPending && systemSource != null) changes.SetUpdatedAudioSource(systemSource);
        if ((sourcePending || microphonePending || systemPending) && changes.Apply()) sourcePending = microphonePending = systemPending = false;
    }
    public void DisposeRecorder() { recorder?.Dispose(); recorder = null; recordingSource = null; canvasSize = null; systemSource = null; microphoneSource = null; sourcePending = microphonePending = systemPending = false; }
    public void Dispose() { DisposeRecorder(); }
}
