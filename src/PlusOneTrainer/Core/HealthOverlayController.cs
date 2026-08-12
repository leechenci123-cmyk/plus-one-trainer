using System.Windows.Threading;

namespace PlusOneTrainer.Core;

public sealed class HealthOverlayController : IDisposable
{
    private readonly GameSession _session;
    private readonly HealthBarSnapshotReader _reader;
    private readonly DispatcherTimer _timer;
    private HealthOverlayWindow? _window;
    private bool _enabled;
    private bool _showZombies = true;
    private bool _showPlants = true;
    private bool _disposed;

    public HealthOverlayController(GameSession session)
    {
        _session = session;
        _reader = new HealthBarSnapshotReader(session);
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    public void Configure(bool enabled, bool showZombies, bool showPlants)
    {
        _enabled = enabled;
        _showZombies = showZombies;
        _showPlants = showPlants;
        if (!enabled || (!showZombies && !showPlants))
            _window?.Conceal();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_disposed || !_enabled || (!_showZombies && !_showPlants) || !_session.Memory.IsAlive)
        {
            _window?.Conceal();
            return;
        }

        try
        {
            var gameWindow = _session.GameWindow;
            if (gameWindow == IntPtr.Zero || NativeMethods.GetForegroundWindow() != gameWindow ||
                !NativeMethods.IsWindowVisible(gameWindow) || NativeMethods.IsIconic(gameWindow) ||
                !_session.IsBattle || !TryGetClientBounds(gameWindow, out var bounds))
            {
                _window?.Conceal();
                return;
            }

            var items = _reader.Read(_showZombies, _showPlants);
            _window ??= new HealthOverlayWindow();
            _window.Present(bounds, items);
        }
        catch
        {
            // Read-only polling is best effort. A scene transition hides the overlay
            // and the next valid snapshot will make it visible again.
            _window?.Conceal();
        }
    }

    private static bool TryGetClientBounds(IntPtr gameWindow, out NativeMethods.Rect bounds)
    {
        bounds = default;
        if (!NativeMethods.GetClientRect(gameWindow, out var client) || client.Width <= 0 || client.Height <= 0)
            return false;
        var origin = new NativeMethods.Point(client.Left, client.Top);
        if (!NativeMethods.ClientToScreen(gameWindow, ref origin))
            return false;
        bounds = new NativeMethods.Rect(origin.X, origin.Y, origin.X + client.Width, origin.Y + client.Height);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        if (_window is not null)
        {
            _window.Conceal();
            _window.Close();
            _window = null;
        }
    }
}
