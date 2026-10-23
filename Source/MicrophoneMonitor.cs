using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Aureo;
// Shared-mode capture keeps the Windows input meter active. No audio is stored here.
internal sealed class MicrophoneMonitor : IDisposable
{
    MMDeviceEnumerator? enumerator;
    MMDevice? device;
    WasapiCapture? capture;
    public float Peak => device?.AudioMeterInformation.MasterPeakValue ?? 0;
    public bool IsRunning => capture != null;
    public void Start(string deviceId)
    {
        if (capture != null) return;
        try {
            enumerator = new MMDeviceEnumerator();
            device = string.IsNullOrEmpty(deviceId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console) : enumerator.GetDevice(deviceId);
            capture = new WasapiCapture(device);
            capture.StartRecording();
        } catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        capture?.Dispose(); capture = null;
        device?.Dispose(); device = null;
        enumerator?.Dispose(); enumerator = null;
    }
}
