using System;

namespace Aureo;
internal static class VideoProfile
{
    public static int Frames(int index) => new[] { 30, 60, 120 }[Math.Clamp(index, 0, 2)];
    public static int Height(int index) => new[] { 0, 720, 1080, 1440, 2160 }[Math.Clamp(index, 0, 4)];
    public static int Bitrate(int quality, int fps, int width, int height)
    {
        double pixels = Math.Max(1L, (long)width * height);
        double baseline = new[] { 8_000_000, 16_000_000, 28_000_000 }[Math.Clamp(quality, 0, 2)];
        return (int)Math.Clamp(baseline * pixels / (1920d * 1080) * fps / 60d, 4_000_000, 160_000_000);
    }
}
