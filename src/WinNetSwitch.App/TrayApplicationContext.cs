using System.Diagnostics;
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
    private readonly System.Windows.Forms.Timer _deferredMenuRebuildTimer = new()
    {
        Interval = 1,
    };
    private readonly Icon _trayIcon;
    private readonly NotifyIcon _notifyIcon;
    private IReadOnlyList<PhysicalNetworkAdapter> _adapters = [];
    private bool _mutationInProgress;
    private bool _refreshInProgress;
    private bool _menuOpen;
    private bool _menuRebuildPending;
    private string? _pendingMenuStatus;
    private bool _pendingMenuStatusIsError;
    private int _menuRevision;
    private long _stateVersion;
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
        _menu.Closed += MenuOnClosed;
        _deferredMenuRebuildTimer.Tick += DeferredMenuRebuildTimerOnTick;
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
        AppLogger.Info("Tray context created.");
    }

    internal Task InitialRefreshCompleted => _initialRefreshCompleted.Task;

    internal bool IsTrayIconVisible => _notifyIcon.Visible;

    internal bool IsRefreshInProgress => _refreshInProgress;

    internal int MenuRevision => _menuRevision;

    internal IReadOnlyList<TrayMenuItemSnapshot> GetMenuSnapshot() =>
        _menu.Items
            .OfType<ToolStripMenuItem>()
            .Select(CreateSnapshot)
            .ToArray();

    internal void BeginRefreshForSmoke() => _ = RefreshAdaptersAsync(showErrors: true);

    internal void BeginMenuSessionForSmoke() => _menuOpen = true;

    internal void EndMenuSessionForSmoke() => CompleteMenuSession();

    protected override void ExitThreadCore()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        AppLogger.Info("Tray exit requested.");
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
            _deferredMenuRebuildTimer.Dispose();
            _menu.Dispose();
            _trayIcon.Dispose();
            (_adapterService as IDisposable)?.Dispose();
            _lifetimeSource.Dispose();
        }

        base.Dispose(disposing);
    }

    private void MenuOnOpening(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        _menuOpen = true;
        if (!_mutationInProgress)
        {
            _ = RefreshAdaptersAsync(showErrors: true);
        }
    }

    private void MenuOnClosed(object? sender, ToolStripDropDownClosedEventArgs eventArgs) =>
        CompleteMenuSession();

    private void CompleteMenuSession()
    {
        _menuOpen = false;
        if (_menuRebuildPending && !_exiting)
        {
            _deferredMenuRebuildTimer.Start();
        }
    }

    private void DeferredMenuRebuildTimerOnTick(object? sender, EventArgs eventArgs)
    {
        _deferredMenuRebuildTimer.Stop();
        if (_menuOpen || !_menuRebuildPending || _exiting)
        {
            return;
        }

        var status = _pendingMenuStatus;
        var isError = _pendingMenuStatusIsError;
        _menuRebuildPending = false;
        _pendingMenuStatus = null;
        _pendingMenuStatusIsError = false;
        RebuildMenu(status, isError);
    }

    private void NotifyIconOnDoubleClick(object? sender, EventArgs eventArgs)
    {
        if (!_mutationInProgress)
        {
            _ = RefreshAdaptersAsync(showErrors: true);
        }
    }

    private async Task RefreshAdaptersAsync(bool showErrors)
    {
        if (_refreshInProgress || _exiting)
        {
            return;
        }

        _refreshInProgress = true;
        var versionAtStart = _stateVersion;
        AppLogger.Info("Refreshing physical adapter list.");
        RequestMenuRebuild("Обновление списка…");
        string? statusAfterRefresh = null;
        try
        {
            var refreshedAdapters = await _adapterService.GetPhysicalAdaptersAsync(_lifetimeSource.Token);
            if (!_mutationInProgress && versionAtStart == _stateVersion)
            {
                _adapters = refreshedAdapters;
            }
            AppLogger.Info(
                $"Adapter refresh completed. Count: {refreshedAdapters.Count}. " +
                string.Join(
                    "; ",
                    refreshedAdapters.Select(adapter =>
                        $"{adapter.Name} ({adapter.Id:D}): {adapter.Status}, " +
                        $"adapter enabled={adapter.IsEnabled}, radio on={adapter.WirelessRadio?.IsOn}, " +
                        $"active={adapter.IsActive}")));
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
            _refreshInProgress = false;
            if (!_exiting && !_mutationInProgress)
            {
                RequestMenuRebuild(statusAfterRefresh, isError: statusAfterRefresh is not null);
            }

            _initialRefreshCompleted.TrySetResult();
        }
    }

    private async Task ToggleAdapterAsync(PhysicalNetworkAdapter adapter)
    {
        if (_mutationInProgress || _exiting)
        {
            return;
        }

        _mutationInProgress = true;
        _stateVersion++;
        var requestedState = !adapter.IsActive;
        AppLogger.Info(
            $"Adapter toggle requested: {adapter.Name} ({adapter.Id:D}), " +
            $"requested enabled={requestedState}, adapter enabled={adapter.IsEnabled}, " +
            $"radio on={adapter.WirelessRadio?.IsOn}.");
        RequestMenuRebuild($"{(requestedState ? "Включение" : "Отключение")} «{adapter.Name}»…");
        string? statusAfterToggle = null;
        try
        {
            _adapters = await _adapterService.SetAdapterEnabledAsync(
                adapter.Id,
                requestedState,
                _lifetimeSource.Token);
            _stateVersion++;
            AppLogger.Info(
                $"Adapter toggle completed: {adapter.Name} ({adapter.Id:D}), " +
                $"enabled={requestedState}.");
            ShowBalloon(
                ToolTipIcon.Info,
                "Состояние сети изменено",
                $"Адаптер «{adapter.Name}» {StateWord(requestedState)}. Остальные адаптеры не изменялись.");
        }
        catch (OperationCanceledException) when (_exiting)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Не удалось изменить «{adapter.Name}»", exception);
            statusAfterToggle = await RefreshAfterFailureAsync();
        }
        finally
        {
            _mutationInProgress = false;
            if (!_exiting)
            {
                RequestMenuRebuild(statusAfterToggle, isError: statusAfterToggle is not null);
            }
        }
    }

    private async Task EnableOnlyAsync(PhysicalNetworkAdapter adapter)
    {
        if (_mutationInProgress || _exiting)
        {
            return;
        }

        _mutationInProgress = true;
        _stateVersion++;
        AppLogger.Info(
            $"Exclusive adapter enable requested: {adapter.Name} ({adapter.Id:D}).");
        RequestMenuRebuild($"Включение только «{adapter.Name}»…");
        string? statusAfterChange = null;
        try
        {
            _adapters = await _adapterService.EnableOnlyAsync(
                adapter.Id,
                _lifetimeSource.Token);
            _stateVersion++;
            AppLogger.Info(
                $"Exclusive adapter enable completed: {adapter.Name} ({adapter.Id:D}).");
            ShowBalloon(
                ToolTipIcon.Info,
                "Состояние сети изменено",
                $"Адаптер «{adapter.Name}» включён, остальные адаптеры отключены.");
        }
        catch (OperationCanceledException) when (_exiting)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Не удалось включить только «{adapter.Name}»", exception);
            statusAfterChange = await RefreshAfterFailureAsync();
        }
        finally
        {
            _mutationInProgress = false;
            if (!_exiting)
            {
                RequestMenuRebuild(statusAfterChange, isError: statusAfterChange is not null);
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
            AppLogger.Error("Failed to refresh adapter state after a switch error.", exception);
            return Shorten($"Состояние неизвестно: {exception.Message}", 100);
        }
    }

    private void RebuildMenu(string? status = null, bool isError = false)
    {
        _menuRevision++;
        var previousItems = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var previousItem in previousItems)
        {
            previousItem.Dispose();
        }

        var heading = new ToolStripMenuItem("Сетевые адаптеры")
        {
            Enabled = false,
            Font = new Font(_menu.Font, FontStyle.Bold),
        };
        _menu.Items.Add(heading);

        if (_adapters.Count == 0 && status is null)
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
                var item = new ToolStripMenuItem(FormatAdapter(adapter))
                {
                    Checked = adapter.IsActive,
                    CheckOnClick = false,
                    ToolTipText = string.IsNullOrWhiteSpace(adapter.Description)
                        ? adapter.Name
                        : adapter.Description,
                };

                var capturedAdapter = adapter;
                var toggleItem = new ToolStripMenuItem(adapter.IsActive ? "Выключить" : "Включить")
                {
                    Enabled = !_mutationInProgress,
                };
                toggleItem.Click += async (_, _) => await ToggleAdapterAsync(capturedAdapter);
                item.DropDownItems.Add(toggleItem);

                var enableOnlyItem = new ToolStripMenuItem("Включить только этот адаптер")
                {
                    Enabled = !_mutationInProgress,
                };
                enableOnlyItem.Click += async (_, _) => await EnableOnlyAsync(capturedAdapter);
                item.DropDownItems.Add(enableOnlyItem);
                _menu.Items.Add(item);
            }
        }

        if (status is not null)
        {
            _menu.Items.Add(new ToolStripMenuItem(status)
            {
                Enabled = false,
                ForeColor = isError ? Color.Firebrick : SystemColors.GrayText,
            });
        }

        _menu.Items.Add(new ToolStripSeparator());
        var refreshItem = new ToolStripMenuItem("Обновить")
        {
            Enabled = !_refreshInProgress && !_mutationInProgress,
            ShortcutKeyDisplayString = "двойной щелчок по значку",
        };
        refreshItem.Click += async (_, _) => await RefreshAdaptersAsync(showErrors: true);
        _menu.Items.Add(refreshItem);

        var openLogItem = new ToolStripMenuItem("Открыть лог ошибок")
        {
            Enabled = true,
        };
        openLogItem.Click += (_, _) => OpenLog();
        _menu.Items.Add(openLogItem);

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Enabled = true;
        exitItem.Click += (_, _) => ExitThread();
        _menu.Items.Add(exitItem);
    }

    private void RequestMenuRebuild(string? status = null, bool isError = false)
    {
        if (_menuOpen || _deferredMenuRebuildTimer.Enabled)
        {
            _menuRebuildPending = true;
            _pendingMenuStatus = status;
            _pendingMenuStatusIsError = isError;
            return;
        }

        RebuildMenu(status, isError);
    }

    private static string FormatAdapter(PhysicalNetworkAdapter adapter)
    {
        var state = adapter switch
        {
            { IsEnabled: false } => "отключён",
            { WirelessRadio.IsOn: false } => "адаптер включён, Wi-Fi выключен",
            { Status: "Up" } => "включён, подключён",
            { Status: "Disconnected" } => "включён, нет подключения",
            _ => $"включён, {adapter.Status}",
        };
        var speed = string.IsNullOrWhiteSpace(adapter.LinkSpeed) ? string.Empty : $" · {adapter.LinkSpeed}";
        return Shorten($"{adapter.Name} — {state}{speed}", 100);
    }

    private static string StateWord(bool enabled) => enabled ? "включён" : "отключён";

    private static TrayMenuItemSnapshot CreateSnapshot(ToolStripMenuItem item) =>
        new(
            item.Text ?? string.Empty,
            item.Checked,
            item.Enabled,
            item.DropDownItems
                .OfType<ToolStripMenuItem>()
                .Select(CreateSnapshot)
                .ToArray());

    private void ShowError(string title, Exception exception)
    {
        AppLogger.Error(title, exception);
        ShowBalloon(
            ToolTipIcon.Error,
            title,
            $"{exception.Message}\nПодробности: {AppLogger.LogPath}");
    }

    private void OpenLog()
    {
        try
        {
            AppLogger.EnsureCreated();
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLogger.LogPath,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            ShowError("Не удалось открыть лог", exception);
        }
    }

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

internal sealed record TrayMenuItemSnapshot(
    string Text,
    bool Checked,
    bool Enabled,
    IReadOnlyList<TrayMenuItemSnapshot> Children);
