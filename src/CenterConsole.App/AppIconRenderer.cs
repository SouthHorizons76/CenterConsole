using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CenterConsole.App;

/// <summary>
/// Renders the CenterConsole glyph: two concentric "C" rings, opening to the right like the letter.
/// The outer ring reflects microphone mute state, the inner ring reflects camera-block state - each
/// red when active (muted / blocked) and black otherwise, so the icon doubles as a status indicator
/// (the same idea as a proxy client's tray icon changing color per connection state).
/// </summary>
public static class AppIconRenderer
{
    private const double GapDegrees = 70;

    /// <summary>The two representations the app needs: <see cref="Window.Icon"/> takes an ImageSource,
    /// while H.NotifyIcon's TaskbarIcon.Icon takes a native System.Drawing.Icon (see remarks on
    /// <see cref="Render"/> for why TaskbarIcon.IconSource can't be used instead).</summary>
    public readonly record struct RenderedIcon(BitmapImage WindowIcon, System.Drawing.Icon TrayIcon);

    /// <remarks>
    /// TaskbarIcon.IconSource looks promising for a WPF-native ImageSource, but at runtime it only
    /// converts by reading BitmapImage.UriSource - a stream- or render-based BitmapImage has a null
    /// UriSource and crashes with a NullReferenceException. TaskbarIcon.Icon (a plain
    /// System.Drawing.Icon) has no such conversion step, so that's what's used for the tray icon; the
    /// PNG bytes are reused as-is for it by wrapping them in a minimal single-image .ico container,
    /// which Icon(Stream) understands natively (no System.Drawing.Bitmap/HICON handle needed).
    /// </remarks>
    public static RenderedIcon Render(bool microphoneMuted, bool cameraBlocked, int size = 64)
    {
        var outerColor = microphoneMuted ? Colors.Red : Colors.Black;
        var innerColor = cameraBlocked ? Colors.Red : Colors.Black;
        var center = new Point(size / 2.0, size / 2.0);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            DrawRing(dc, center, outerRadius: size * 0.46, thickness: size * 0.17, outerColor);
            DrawRing(dc, center, outerRadius: size * 0.25, thickness: size * 0.12, innerColor);
        }

        var renderBitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        renderBitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
        using var pngStream = new MemoryStream();
        encoder.Save(pngStream);
        byte[] pngBytes = pngStream.ToArray();

        return new RenderedIcon(ToBitmapImage(pngBytes), ToIcon(pngBytes, size));
    }

    private static BitmapImage ToBitmapImage(byte[] pngBytes)
    {
        var stream = new MemoryStream(pngBytes);
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }

    /// <summary>Wraps a PNG in the minimal ICONDIR/ICONDIRENTRY header that makes it a valid single-image
    /// .ico stream (the PNG-payload icon format Windows has supported since Vista).</summary>
    private static System.Drawing.Icon ToIcon(byte[] pngBytes, int size)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((short)0);   // reserved
            writer.Write((short)1);   // type: icon
            writer.Write((short)1);   // image count

            writer.Write((byte)size); // width (size is kept <=255 so this never needs the "0 means 256" case)
            writer.Write((byte)size); // height
            writer.Write((byte)0);    // color count: not palette-based
            writer.Write((byte)0);    // reserved
            writer.Write((short)1);   // color planes
            writer.Write((short)32);  // bits per pixel
            writer.Write(pngBytes.Length);
            writer.Write(22);         // offset to image data: 6-byte ICONDIR + 16-byte ICONDIRENTRY

            writer.Write(pngBytes);
        }

        ms.Position = 0;
        return new System.Drawing.Icon(ms);
    }

    private static void DrawRing(DrawingContext dc, Point center, double outerRadius, double thickness, Color color)
    {
        double innerRadius = outerRadius - thickness;
        double startAngle = GapDegrees / 2.0;
        double endAngle = 360 - GapDegrees / 2.0;

        Point OnCircle(double radius, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        Point outerStart = OnCircle(outerRadius, startAngle);
        Point outerEnd = OnCircle(outerRadius, endAngle);
        Point innerEnd = OnCircle(innerRadius, endAngle);
        Point innerStart = OnCircle(innerRadius, startAngle);
        bool isLargeArc = (endAngle - startAngle) > 180;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(outerStart, isFilled: true, isClosed: true);
            ctx.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true, false);
            ctx.LineTo(innerEnd, true, false);
            ctx.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(outerStart, true, false);
        }
        geometry.Freeze();

        dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }
}
