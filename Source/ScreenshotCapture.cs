using System;
using System.IO;
using System.Threading.Tasks;
using ScreenRecorderLib;

namespace Aureo;
internal static class ScreenshotCapture
{
    public static async Task SaveAsync(RecordingSourceBase source, string path, bool cursor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = RecorderOptions.Default;
        options.SourceOptions = new SourceOptions { RecordingSources = { source } };
        options.OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Screenshot };
        options.AudioOptions = new AudioOptions { IsAudioEnabled = false };
        options.MouseOptions = new MouseOptions { IsMousePointerEnabled = cursor };
        options.SnapshotOptions = new SnapshotOptions { SnapshotFormat = ImageFormat.PNG };
        using var recorder = Recorder.CreateRecorder(options);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.OnRecordingComplete += (_, _) => done.TrySetResult();
        recorder.OnRecordingFailed += (_, e) => done.TrySetException(new InvalidOperationException(e.Error));
        recorder.Record(path);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
        if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("截圖檔案未成功儲存。");
    }
}
