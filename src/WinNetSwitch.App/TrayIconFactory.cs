using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WinNetSwitch.App;

internal static class TrayIconFactory
{
    internal static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var backgroundBrush = new SolidBrush(Color.FromArgb(31, 102, 180));
            graphics.FillEllipse(backgroundBrush, 1, 1, 30, 30);

            using var linePen = new Pen(Color.White, 2.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawArc(linePen, 7, 7, 18, 17, 210, 120);
            graphics.DrawArc(linePen, 10, 11, 12, 11, 210, 120);
            graphics.FillEllipse(Brushes.White, 14, 21, 4, 4);

            using var switchPen = new Pen(Color.FromArgb(155, 230, 255), 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.ArrowAnchor,
            };
            graphics.DrawLine(switchPen, 6, 26, 12, 26);
            graphics.DrawLine(switchPen, 26, 6, 20, 6);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(handle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
