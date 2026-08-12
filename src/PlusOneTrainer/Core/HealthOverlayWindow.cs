using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PlusOneTrainer.Core;

internal sealed class HealthOverlayWindow : Window
{
    private readonly HealthBarSurface _surface = new();

    public HealthOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Content = _surface;
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    public void Present(NativeMethods.Rect bounds, IReadOnlyList<HealthBarSnapshot> items)
    {
        if (!IsVisible)
            Show();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopMost, bounds.Left, bounds.Top,
            bounds.Width, bounds.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _surface.SetItems(items);
    }

    public void Conceal()
    {
        if (IsVisible)
            Hide();
        _surface.SetItems([]);
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        _ = NativeMethods.SetWindowLong(handle, NativeMethods.GwlExStyle,
            style | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }

    private sealed class HealthBarSurface : FrameworkElement
    {
        private static readonly Brush Back = Freeze(new SolidColorBrush(Color.FromArgb(210, 42, 32, 25)));
        private static readonly Brush Plant = Freeze(new SolidColorBrush(Color.FromRgb(101, 176, 79)));
        private static readonly Brush Zombie = Freeze(new SolidColorBrush(Color.FromRgb(213, 76, 56)));
        private static readonly Brush Critical = Freeze(new SolidColorBrush(Color.FromRgb(240, 174, 58)));
        private static readonly Pen Border = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(225, 245, 235, 204)), 1));
        private IReadOnlyList<HealthBarSnapshot> _items = [];

        public void SetItems(IReadOnlyList<HealthBarSnapshot> items)
        {
            _items = items;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (ActualWidth <= 0 || ActualHeight <= 0)
                return;
            var scaleX = ActualWidth / 800d;
            var scaleY = ActualHeight / 600d;
            var barHeight = Math.Clamp(6 * scaleY, 4, 10);

            foreach (var item in _items.OrderBy(x => x.Kind))
            {
                var width = Math.Max(18, item.Width * scaleX);
                var x = Math.Clamp(item.X * scaleX, 2, Math.Max(2, ActualWidth - width - 2));
                var y = Math.Clamp(item.Y * scaleY, 2, Math.Max(2, ActualHeight - barHeight - 2));
                var outer = new Rect(x, y, width, barHeight);
                drawingContext.DrawRoundedRectangle(Back, Border, outer, 2, 2);

                var innerWidth = Math.Max(0, (width - 2) * item.Ratio);
                if (innerWidth <= 0)
                    continue;
                var fill = item.Ratio <= 0.25 ? Critical : item.Kind == HealthBarKind.Zombie ? Zombie : Plant;
                drawingContext.DrawRoundedRectangle(fill, null,
                    new Rect(x + 1, y + 1, innerWidth, Math.Max(1, barHeight - 2)), 1.5, 1.5);
            }
        }

        private static T Freeze<T>(T value) where T : Freezable
        {
            value.Freeze();
            return value;
        }
    }
}
