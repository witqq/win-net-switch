using WinNetSwitch.Core;
using WinNetSwitch.Core.PowerShell;

namespace WinNetSwitch.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly INetworkAdapterService _adapterService;
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly TaskCompletionSource _initialRefreshCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Icon _trayIcon;
    private readonly NotifyIcon _notifyIcon;
    private IReadOnlyList<PhysicalNetworkAdapter> _adapters = [];
    private bool _busy;
    private bool _disposed;
    private bool _exiting;

    public TrayApplicationContext()
        : this(new PhysicalNetworkAdapterService(new PowerShellRunner()))
    {
    }

    internal TrayApplicationContext(INetworkAdapterService adapterService)
    {
        _adapterService = adapterService ?? throw new ArgumentNullException(nameof(adapterService));
        _trayIcon = TrayIconFactory.Create();
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _trayIcon,
            Text = "WinNetSwitch — переключение сети",
            Visible = true,
        };

        _menu.Opening += MenuOnOpening;
        _notifyIcon.DoubleClick += NotifyIconOnDoubleClick;
        RebuildMenu("Загрузка физических адаптеров…");

        var startupTimer = new System.Windows.Forms.Timer { Interval = 100 };
        startupTimer.Tick += (_, _) =>
        {
            startupTimer.Stop();
            startupTimer.Dispose();
            _ = RefreshAdaptersAsync(showErrors: true);
        };
        startupTimer.Start();
    }

    internal Task InitialRefreshCompleted => _initialRefreshCompleted.Task;

    internal bool IsTrayIconVisible => _notifyIcon.Visible;

    internal IReadOnlyList<TrayMenuItemSnapshot> GetMenuSnapshot() =>
        _menu.Items
            .OfType<ToolStripMenuItem>()
            .Select(item => new TrayMenuItemSnapshot(item.Text ?? string.Empty, item.Checked, item.Enabled))
            .ToArray();

    protected override void ExitThreadCore()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _lifetimeSource.Cancel();
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _lifetimeSource.Cancel();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _trayIcon.Dispose();
            (_adapterService as IDisposable)?.Dispose();
            _lifetimeSource.Dispose();
        }

        base.Dispose(disposing);
    }

    private void MenuOnOpening(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        if (!_busy)
        {
            _ = RefreshAdaptersAsync(showErrors: true);
        }
    }

    private void NotifyIconOnDoubleClick(object? sender, EventArgs eventArgs)
    {
        if (!_busy)
        {
            _ = RefreshAdaptersAsync(showErrors: true);
        }
    }

    private async Task RefreshAdaptersAsync(bool showErrors)
    {
        if (_busy || _exiting)
        {
            return;
        }

        _busy = true;
        RebuildMenu("Обновление списка…");
        string? statusAfterRefresh = null;
        try
        {
            _adapters = await _adapterService.GetPhysicalAdaptersAsync(_lifetimeSource.Token);
        }
        catch (OperationCanceledException) when (_exiting)
        {
        }
        catch (Exception exception)
        {
            statusAfterRefresh = Shorten($"Ошибка: {exception.Message}", 100);
            if (showErrors)
            {
                ShowError("Не удалось обновить список адаптеров", exception);
            }
        }
        finally
        {
            _busy = false;
            if (!_exiting)
            {
                RebuildMenu(statusAfterRefresh, isError: statusAfterRefresh is not null);
            }

            _initialRefreshCompleted.TrySetResult();
        }
    }

    private async Task SwitchAdapterAsync(PhysicalNetworkAdapter adapter)
    {
        if (_busy || _exiting)
        {
            return;
        }

        _busy = true;
        RebuildMenu($"Переключение на «{adapter.Name}»…");
        string? statusAfterSwitch = null;
        try
        {
            _adapters = await _adapterService.SwitchExclusivelyAsync(
                adapter.Id,
                _lifetimeSource.Token);
            ShowBalloon(
                ToolTipIcon.Info,
                "Сеть переключена",
                $"Включён только физический адаптер «{adapter.Name}».");
        }
        catch (OperationCanceledException) when (_exiting)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Не удалось переключиться на «{adapter.Name}»", exception);
            statusAfterSwitch = await RefreshAfterFailureAsync();
        }
        finally
        {
            _busy = false;
            if (!_exiting)
            {
                RebuildMenu(statusAfterSwitch, isError: statusAfterSwitch is not null);
            }
        }
    }

    private async Task<string?> RefreshAfterFailureAsync()
    {
        try
        {
            _adapters = await _adapterService.GetPhysicalAdaptersAsync(_lifetimeSource.Token);
            return null;
        }
        catch (OperationCanceledException) when (_exiting)
        {
            return null;
        }
        catch (Exception exception)
        {
            return Shorten($"Состояние неизвестно: {exception.Message}", 100);
        }
    }

    private void RebuildMenu(string? status = null, bool isError = false)
    {
        var previousItems = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var previousItem in previousItems)
        {
            previousItem.Dispose();
        }

        var heading = new ToolStripMenuItem("Выберите физический адаптер")
        {
            Enabled = false,
            Font = new Font(_menu.Font, FontStyle.Bold),
        };
        _menu.Items.Add(heading);

        if (status is not null)
        {
            _menu.Items.Add(new ToolStripMenuItem(status)
            {
                Enabled = false,
                ForeColor = isError ? Color.Firebrick : SystemColors.GrayText,
            });
        }
        else if (_adapters.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem("Физические адаптеры не найдены")
            {
                Enabled = false,
            });
        }
        else
        {
            foreach (var adapter in _adapters)
            {
                var capturedAdapter = adapter;
                var item = new ToolStripMenuItem(FormatAdapter(adapter))
                {
                    Checked = adapter.IsEnabled,
                    CheckOnClick = false,
                    Enabled = !_busy,
                    ToolTipText = string.IsNullOrWhiteSpace(adapter.Description)
                        ? adapter.Name
                        : adapter.Description,
                };
                item.Click += async (_, _) => await SwitchAdapterAsync(capturedAdapter);
                _menu.Items.Add(item);
            }
        }

        _menu.Items.Add(new ToolStripSeparator());
        var refreshItem = new ToolStripMenuItem("Обновить")
        {
            Enabled = !_busy,
            ShortcutKeyDisplayString = "двойной щелчок по значку",
        };
        refreshItem.Click += async (_, _) => await RefreshAdaptersAsync(showErrors: true);
        _menu.Items.Add(refreshItem);

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Enabled = !_busy;
        exitItem.Click += (_, _) => ExitThread();
        _menu.Items.Add(exitItem);
    }

    private static string FormatAdapter(PhysicalNetworkAdapter adapter)
    {
        var state = adapter switch
        {
            { IsEnabled: false } => "отключён",
            { Status: "Up" } => "включён, подключён",
            { Status: "Disconnected" } => "включён, нет подключения",
            _ => $"включён, {adapter.Status}",
        };
        var speed = string.IsNullOrWhiteSpace(adapter.LinkSpeed) ? string.Empty : $" · {adapter.LinkSpeed}";
        return Shorten($"{adapter.Name} — {state}{speed}", 100);
    }

    private void ShowError(string title, Exception exception) =>
        ShowBalloon(ToolTipIcon.Error, title, exception.Message);

    private void ShowBalloon(ToolTipIcon icon, string title, string message)
    {
        if (_exiting)
        {
            return;
        }

        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.BalloonTipTitle = Shorten(title, 63);
        _notifyIcon.BalloonTipText = Shorten(message, 255);
        _notifyIcon.ShowBalloonTip(5000);
    }

    private static string Shorten(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 1), "…");
}

internal sealed record TrayMenuItemSnapshot(string Text, bool Checked, bool Enabled);
