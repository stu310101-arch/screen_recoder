using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace Aureo;
public sealed class RegionPicker : Window
{
    readonly Canvas canvas = new();
    readonly System.Windows.Shapes.Path shade = new() { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle outline = new() { Stroke = new SolidColorBrush(Color.FromRgb(213, 183, 124)), StrokeThickness = 2, IsHitTestVisible = false };
    readonly TextBlock hint = new() { Text = "拖曳選取錄製範圍  ·  Esc 取消", Foreground = Brushes.White, FontSize = 20, Background = new SolidColorBrush(Color.FromArgb(230, 13, 15, 18)), Padding = new Thickness(16), IsHitTestVisible = false };
    Point origin; bool dragging;
    readonly Drawing.Rectangle bounds;
    public Drawing.Rectangle? Selection { get; private set; }
    public RegionPicker(Drawing.Rectangle screenBounds)
    {
        bounds = screenBounds;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); Topmost = true; ShowInTaskbar = false;
        Cursor = Cursors.Cross; Content = canvas; canvas.Children.Add(shade); canvas.Children.Add(outline); canvas.Children.Add(hint);
        Canvas.SetLeft(hint, 32); Canvas.SetTop(hint, 32);
        SourceInitialized += (_, _) => { Native.Exclude(this); Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040); };
        Loaded += (_, _) => { UpdateShade(Rect.Empty); Activate(); Focus(); };
        SizeChanged += (_, _) => UpdateShade(Rect.Empty);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
        MouseLeftButtonDown += (_, e) => { origin = e.GetPosition(canvas); dragging = true; canvas.CaptureMouse(); };
        MouseMove += (_, e) => { if (dragging) UpdateSelection(e.GetPosition(canvas)); };
        MouseLeftButtonUp += (_, e) => {
            if (!dragging) return; dragging = false; canvas.ReleaseMouseCapture();
            var r = new Rect(origin, e.GetPosition(canvas));
            var transform = PresentationSource.FromVisual(this)!.CompositionTarget.TransformToDevice;
            var p1 = transform.Transform(r.TopLeft); var p2 = transform.Transform(r.BottomRight);
            int x = Math.Clamp((int)Math.Round(p1.X), 0, bounds.Width - 1), y = Math.Clamp((int)Math.Round(p1.Y), 0, bounds.Height - 1);
            int width = Math.Min((int)Math.Round(p2.X) - x, bounds.Width - x) & ~1;
            int height = Math.Min((int)Math.Round(p2.Y) - y, bounds.Height - y) & ~1;
            if (width < 64 || height < 64) { hint.Text = "範圍至少需要 64 × 64 像素，請重新拖曳。"; return; }
            Selection = new Drawing.Rectangle(x, y, width, height); DialogResult = true;
        };
    }
    void UpdateSelection(Point end) {
        end.X = Math.Clamp(end.X, 0, ActualWidth); end.Y = Math.Clamp(end.Y, 0, ActualHeight);
        var r = new Rect(origin, end); outline.Width = r.Width; outline.Height = r.Height;
        Canvas.SetLeft(outline, r.X); Canvas.SetTop(outline, r.Y); UpdateShade(r);
        var m = PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice ?? Matrix.Identity;
        hint.Text = $"{(int)(r.Width * m.M11)} × {(int)(r.Height * m.M22)}  ·  放開滑鼠確認  ·  Esc 取消";
    }
    void UpdateShade(Rect hole) {
        var full = new RectangleGeometry(new Rect(0, 0, Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1)));
        shade.Data = hole.IsEmpty ? full : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole));
    }
}
