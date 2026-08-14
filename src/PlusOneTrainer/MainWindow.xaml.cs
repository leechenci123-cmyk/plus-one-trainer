using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PlusOneTrainer.Core;
using PlusOneTrainer.Models;
using PlusOneTrainer.Services;

namespace PlusOneTrainer;

public partial class MainWindow : Window
{
    private readonly SaveVaultService _saveVault = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _toastTimer;
    private TrainerEngine? _engine;
    private HealthOverlayController? _healthOverlay;
    private FrameworkElement[] _pages = [];
    private Button[] _navButtons = [];
    private bool _uiReady;
    private bool _suppressUi;
    private bool _limboEnabled;
    private bool _nightRoofEnabled;
    private bool _moneyBackupCreated;
    private bool _closingAfterRemoteCleanup;
    private DateTime _nextAutoAttach = DateTime.MinValue;
    private HwndSource? _windowSource;
    private double _lastRequestedSpeed = 2;
    private AttachmentState _state = AttachmentState.NotRunning;
    private string _stateDetails = "";

    public MainWindow()
    {
        InitializeComponent();
        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background,
            StatusTimer_Tick, Dispatcher);
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) => { ToastText.Text = ""; _toastTimer.Stop(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowProc);
        var hwnd = _windowSource?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.RegisterHotKey(hwnd, 2, NativeMethods.ModNoRepeat, 0x73); // F4
            NativeMethods.RegisterHotKey(hwnd, 3, NativeMethods.ModNoRepeat, 0x77); // F8
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _pages = [PageHome, PageChallenge, PageModes, PageLab, PageCheats, PageVault, PageSettings];
        _navButtons = [NavHome, NavChallenge, NavModes, NavLab, NavCheats, NavVault, NavSettings];
        PopulateCoordinates(5);
        RefreshZombieList();
        _suppressUi = true;
        LanguageCombo.SelectedIndex = LocalizationService.CurrentLanguage == "en-US" ? 1 : 0;
        _suppressUi = false;
        RefreshBackups();
        UpdateSaveLocation();
        _uiReady = true;
        UpdateChallengeCapability();
        UpdateRemoteCallCapability();
        UpdateHealthBarControls();
        AttachGame(showErrors: false);
        _statusTimer.Start();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_closingAfterRemoteCleanup)
        {
            _statusTimer.Stop();
            _healthOverlay?.Dispose();
            _healthOverlay = null;
            _engine?.BeginShutdown();
            if (_engine?.Session.Calls.HasPendingOperation == true)
            {
                e.Cancel = true;
                IsEnabled = false;
                ShowToast(LocalizationService.Text("RemoteCleanupWaiting"));
                try
                {
                    while (_engine?.Session.Memory.IsAlive == true &&
                           !_engine.Session.Calls.WaitForPending(250))
                        await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    IsEnabled = true;
                    ShowException(ex);
                    return;
                }
                _closingAfterRemoteCleanup = true;
                Close();
                return;
            }
            _closingAfterRemoteCleanup = true;
        }
        _statusTimer.Stop();
        var hwnd = _windowSource?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(hwnd, 2);
            NativeMethods.UnregisterHotKey(hwnd, 3);
        }
        _windowSource?.RemoveHook(WindowProc);
        _engine?.Dispose();
        _engine = null;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotKey)
            return IntPtr.Zero;
        handled = true;
        Dispatcher.BeginInvoke(() =>
        {
            switch (wParam.ToInt32())
            {
                case 1:
                    ShowToast(LocalizationService.Text("ErrorAdvancedPauseUnavailable"));
                    break;
                case 2:
                    RunWithGame(engine =>
                    {
                        if (Math.Abs(engine.LastSpeed - 1) < 0.01)
                            engine.SetSpeed(_lastRequestedSpeed);
                        else
                            engine.ResetSpeed();
                    });
                    break;
                case 3:
                    CreateBackup("hotkey");
                    break;
            }
        });
        return IntPtr.Zero;
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (_engine is null || !_engine.Session.Memory.IsAlive)
        {
            if (_engine is not null)
            {
                _healthOverlay?.Dispose();
                _healthOverlay = null;
                _engine.Dispose();
                _engine = null;
                SetAttachmentState(AttachmentState.NotRunning, "");
            }
            if (DateTime.UtcNow >= _nextAutoAttach)
            {
                _nextAutoAttach = DateTime.UtcNow.AddSeconds(3);
                AttachGame(showErrors: false);
            }
            return;
        }

        try
        {
            _engine.ObserveGameContext();
            if (_engine.Session.IsBattle)
                PopulateCoordinates(_engine.Session.RowCount);
        }
        catch { }
    }

    private void Attach_Click(object sender, RoutedEventArgs e) => AttachGame(showErrors: true);

    private void AttachGame(bool showErrors)
    {
        if (_engine?.Session.Memory.IsAlive == true)
        {
            EnsureHealthOverlay();
            SetAttachmentState(AttachmentState.Attached, _engine.Session.ExecutablePath);
            return;
        }

        try
        {
            var result = GameSession.TryAttach();
            _stateDetails = result.Details;
            if (result.State == AttachmentState.Attached && result.Session is not null)
            {
                _healthOverlay?.Dispose();
                _healthOverlay = null;
                _engine?.Dispose();
                _engine = new TrainerEngine(result.Session, _saveVault);
                _moneyBackupCreated = false;
                EnsureHealthOverlay();
                SetAttachmentState(AttachmentState.Attached, result.Details);
                UpdateAdvancedPauseCapability();
                UpdateChallengeCapability();
                UpdateRemoteCallCapability();
                UpdateSaveLocation();
                ShowToast(result.Details);
            }
            else
            {
                SetAttachmentState(result.State, result.Details);
                if (showErrors && result.State == AttachmentState.Unsupported)
                    MessageBox.Show(LocalizationService.Text("ErrorVersionMismatch") + "\n\n" + result.Details,
                        LocalizationService.Text("Failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            SetAttachmentState(AttachmentState.Unsupported, ex.Message);
            if (showErrors)
                ShowException(ex);
        }
    }

    private void SetAttachmentState(AttachmentState state, string details)
    {
        _state = state;
        _stateDetails = details;
        StatusDetails.Text = details;
        ReadOnlyPill.Visibility = state == AttachmentState.Unsupported ? Visibility.Visible : Visibility.Collapsed;
        switch (state)
        {
            case AttachmentState.Attached:
                StatusText.Text = LocalizationService.Text("StatusAttached");
                StatusDot.Background = new SolidColorBrush(Color.FromRgb(101, 151, 72));
                break;
            case AttachmentState.Unsupported:
                StatusText.Text = LocalizationService.Text("StatusUnsupported");
                StatusDot.Background = new SolidColorBrush(Color.FromRgb(183, 71, 53));
                break;
            default:
                StatusText.Text = LocalizationService.Text("StatusNotRunning");
                StatusDot.Background = new SolidColorBrush(Color.FromRgb(197, 151, 64));
                break;
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var index = Array.IndexOf(_navButtons, sender as Button);
        if (index < 0)
            return;
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            _navButtons[i].Tag = i == index ? "Selected" : null;
        }
        if (index == 5)
        {
            RefreshBackups();
            UpdateSaveLocation();
        }
    }

    private void AdvancedPause_Click(object sender, RoutedEventArgs e)
    {
        if (_engine?.SupportsAdvancedPause != true)
        {
            MessageBox.Show(LocalizationService.Text("ErrorAdvancedPauseUnavailable"),
                LocalizationService.Text("Failed"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RunWithGame(engine => { engine.ToggleAdvancedPause(); UpdatePauseButton(); });
    }

    private void UpdatePauseButton()
    {
        AdvancedPauseButton.Content = LocalizationService.Text(_engine?.AdvancedPaused == true
            ? "AdvancedPauseOn" : "AdvancedPauseOff");
        AdvancedPauseButton.Background = _engine?.AdvancedPaused == true
            ? new SolidColorBrush(Color.FromRgb(183, 71, 53))
            : (Brush)FindResource("Leaf");
    }

    private void UpdateAdvancedPauseCapability()
    {
        var supported = _engine?.SupportsAdvancedPause == true;
        AdvancedPauseButton.IsEnabled = supported;
        FocusPauseCheck.IsEnabled = supported;
        if (!supported)
            FocusPauseCheck.IsChecked = false;
        UpdatePauseButton();
    }

    private void ApplySpeed_Click(object sender, RoutedEventArgs e)
    {
        if (SpeedCombo.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
            return;
        _lastRequestedSpeed = speed;
        RunWithGame(engine => engine.SetSpeed(speed));
    }

    private void ResetSpeed_Click(object sender, RoutedEventArgs e) => RunWithGame(engine => engine.ResetSpeed());

    private void AutoCollect_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _suppressUi)
            return;
        var desired = AutoCollectCheck.IsChecked == true;
        if (!RunWithGame(engine => engine.SetAutoCollect(desired)))
            RevertCheck(AutoCollectCheck, desired);
    }

    private void HealthBars_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _suppressUi)
            return;
        UpdateHealthBarControls();
        ApplyHealthBarPreference();
    }

    private void UpdateHealthBarControls()
    {
        var enabled = HealthBarsCheck.IsChecked == true;
        ZombieHealthBarsCheck.IsEnabled = enabled;
        PlantHealthBarsCheck.IsEnabled = enabled;
    }

    private void EnsureHealthOverlay()
    {
        if (_engine is null || !_engine.Session.Memory.IsAlive)
            return;
        _healthOverlay ??= new HealthOverlayController(_engine.Session);
        ApplyHealthBarPreference();
    }

    private void ApplyHealthBarPreference() =>
        _healthOverlay?.Configure(HealthBarsCheck.IsChecked == true,
            ZombieHealthBarsCheck.IsChecked == true, PlantHealthBarsCheck.IsChecked == true);

    private void UpdateChallengeCapability()
    {
        var supported = _engine?.SupportsChallengeRules == true;
        ChallengeSettingsCard.IsEnabled = supported;
        AdvancedChallengeCard.IsEnabled = supported;
        ChallengeApplyButton.IsEnabled = supported;
        if (!supported)
            DifficultyEnabledCheck.IsChecked = false;
    }

    private void UpdateRemoteCallCapability()
    {
        var supported = _engine?.SupportsRemoteCalls == true;
        NightRoofButton.IsEnabled = supported;
        SpawnZombieButton.IsEnabled = supported;
        PlaceLadderButton.IsEnabled = supported;
        ClearLabButton.IsEnabled = supported;
    }

    private void ApplyChallenge_Click(object sender, RoutedEventArgs e)
    {
        if (_engine?.SupportsChallengeRules != true)
        {
            MessageBox.Show(LocalizationService.Text("ErrorChallengeUnavailable"),
                LocalizationService.Text("Failed"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryNumber(SpawnCountBox.Text, out var count) || !TryNumber(SpawnSpeedBox.Text, out var speed) ||
            !TryNumber(DurabilityBox.Text, out var health) || !TryNumber(GrowthStepBox.Text, out var step) ||
            !int.TryParse(GrowthStartBox.Text, out var start) || !TryNumber(GrowthCapBox.Text, out var cap))
        {
            MessageBox.Show(LocalizationService.Text("ErrorInvalidNumericSettings"), LocalizationService.Text("Failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var unit = Enum.Parse<GrowthUnit>(((ComboBoxItem)GrowthUnitCombo.SelectedItem).Tag!.ToString()!);
        var formula = Enum.Parse<GrowthFormula>(((ComboBoxItem)GrowthFormulaCombo.SelectedItem).Tag!.ToString()!);
        var settings = new DifficultySettings(DifficultyEnabledCheck.IsChecked == true, count, speed, health,
            unit, formula, step, start, cap, ResetAtLevelCheck.IsChecked == true);
        RunWithGame(engine => engine.Difficulty.Apply(settings));
    }

    private void Limbo_Click(object sender, RoutedEventArgs e)
    {
        var desired = !_limboEnabled;
        if (desired && !RunWithGame(engine =>
            {
                if (!engine.HasCompletedFirstAdventure())
                    throw new TrainerException("ErrorAdventureRequired", "Complete Adventure once before revealing Limbo Page.");
            }))
            return;
        if (RunWithGame(engine => engine.SetLimboPage(desired)))
        {
            _limboEnabled = desired;
            LimboButton.Content = LocalizationService.Text(desired ? "LockLimbo" : "UnlockLimbo");
        }
    }

    private void NightRoof_Click(object sender, RoutedEventArgs e)
    {
        var desired = !_nightRoofEnabled;
        if (desired && !CreateBackup("night-roof"))
            return;
        if (RunWithGame(engine => engine.SetNightRoofExperiment(desired)))
        {
            _nightRoofEnabled = desired;
            ShowToast(LocalizationService.Text("Success"));
        }
    }

    private void SpawnZombie_Click(object sender, RoutedEventArgs e)
    {
        if (ZombieCombo.SelectedItem is not ZombieOption zombie ||
            ZombieRowCombo.SelectedItem is not int row || ZombieColumnCombo.SelectedItem is not int column)
            return;
        if (zombie.Id == 25)
        {
            var answer = MessageBox.Show(LocalizationService.Text("BossConfirmText"),
                LocalizationService.Text("BossConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes || !CreateBackup("before-boss"))
                return;
        }
        RunWithGame(engine => engine.SpawnZombie(row - 1, column - 1, zombie.Id));
    }

    private void PlaceLadder_Click(object sender, RoutedEventArgs e)
    {
        if (LadderRowCombo.SelectedItem is int row && LadderColumnCombo.SelectedItem is int column)
            RunWithGame(engine => engine.PlaceLadder(row - 1, column - 1));
    }

    private void ClearLab_Click(object sender, RoutedEventArgs e) => RunWithGame(engine => engine.ClearLabObjects());

    private void SetSun_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(SunBox.Text, out var sun))
            RunWithGame(engine => engine.SetSun(sun));
    }

    private void AddMoney_Click(object sender, RoutedEventArgs e)
    {
        if (!_moneyBackupCreated && !CreateBackup("before-money"))
            return;
        var balance = 0;
        if (RunWithGame(engine => balance = engine.AddMoney(1000)))
        {
            _moneyBackupCreated = true;
            ShowToast(string.Format(LocalizationService.Text("MoneyBalance"), balance));
        }
    }

    private void Cheat_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _suppressUi || sender is not CheckBox check)
            return;
        var desired = check.IsChecked == true;
        var success = RunWithGame(engine =>
        {
            if (check == SunLimitCheck) engine.SetUnlockSunLimit(desired);
            else if (check == NoCooldownCheck) engine.SetNoCooldown(desired);
            else if (check == FreePlantingCheck) engine.SetFreePlanting(desired);
            else if (check == PlantInvincibleCheck) engine.SetPlantInvincible(desired);
            else if (check == MushroomsAwakeCheck) engine.SetMushroomsAwake(desired);
        });
        if (!success)
            RevertCheck(check, desired);
    }

    private void Backup_Click(object sender, RoutedEventArgs e) => CreateBackup("manual");

    private bool CreateBackup(string reason)
    {
        try
        {
            _saveVault.CreateBackup(reason, _engine?.Session.ExecutablePath);
            RefreshBackups();
            ShowToast(LocalizationService.Text("BackupComplete"));
            return true;
        }
        catch (Exception ex)
        {
            ShowException(ex);
            return false;
        }
    }

    private void OpenVault_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_saveVault.VaultPath);
        OpenPath(_saveVault.VaultPath);
    }

    private void RefreshBackups_Click(object sender, RoutedEventArgs e) => RefreshBackups();

    private void RefreshBackups()
    {
        BackupsList.ItemsSource = _saveVault.ListBackups();
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupEntry selected)
            return;
        if (Process.GetProcessesByName("PlantsVsZombies").Length > 0)
        {
            MessageBox.Show(LocalizationService.Text("ErrorGameMustClose"), LocalizationService.Text("Failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var answer = MessageBox.Show(LocalizationService.Text("RestoreConfirmText"),
            LocalizationService.Text("RestoreConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;
        try
        {
            _saveVault.Restore(selected);
            RefreshBackups();
            ShowToast(LocalizationService.Text("RestoreComplete"));
        }
        catch (Exception ex) { ShowException(ex); }
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressUi || LanguageCombo.SelectedItem is not ComboBoxItem item)
            return;
        LocalizationService.SetLanguage(item.Tag?.ToString() ?? "zh-CN");
        RefreshZombieList();
        SetAttachmentState(_state, _stateDetails);
        UpdateAdvancedPauseCapability();
        UpdateChallengeCapability();
        UpdateRemoteCallCapability();
        UpdatePauseButton();
        LimboButton.Content = LocalizationService.Text(_limboEnabled ? "LockLimbo" : "UnlockLimbo");
        UpdateSaveLocation();
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e) => OpenPath(FindProjectRoot());

    private void OpenReferences_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(FindProjectRoot(), "docs", "REFERENCES.md");
        if (File.Exists(path)) OpenPath(path);
    }

    private void UpdateSaveLocation()
    {
        SaveLocationText.Text = _saveVault.LocateSaveDirectory(_engine?.Session.ExecutablePath)
                                ?? LocalizationService.Text("ErrorSaveNotFound");
    }

    private void PopulateCoordinates(int rows)
    {
        rows = Math.Clamp(rows, 5, 6);
        var currentRows = ZombieRowCombo.Items.Cast<int>().ToArray();
        if (currentRows.Length == rows)
            return;
        var rowItems = Enumerable.Range(1, rows).ToArray();
        var columnItems = Enumerable.Range(1, 9).ToArray();
        ZombieRowCombo.ItemsSource = rowItems;
        LadderRowCombo.ItemsSource = rowItems;
        ZombieColumnCombo.ItemsSource = columnItems;
        LadderColumnCombo.ItemsSource = columnItems;
        ZombieRowCombo.SelectedIndex = 0;
        LadderRowCombo.SelectedIndex = 0;
        ZombieColumnCombo.SelectedIndex = 8;
        LadderColumnCombo.SelectedIndex = 0;
    }

    private void RefreshZombieList()
    {
        var selected = (ZombieCombo.SelectedItem as ZombieOption)?.Id ?? 0;
        ZombieCombo.ItemsSource = null;
        ZombieCombo.ItemsSource = ZombieOption.All;
        ZombieCombo.DisplayMemberPath = nameof(ZombieOption.DisplayName);
        ZombieCombo.SelectedItem = ZombieOption.All.First(x => x.Id == selected);
    }

    private bool RunWithGame(Action<TrainerEngine> action)
    {
        if (_engine?.Session.Memory.IsAlive != true)
        {
            AttachGame(showErrors: false);
            if (_engine is null)
            {
                MessageBox.Show(LocalizationService.Text("GameNeeded"), LocalizationService.Text("Failed"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }
        try
        {
            action(_engine);
            ShowToast(LocalizationService.Text("Success"));
            return true;
        }
        catch (Exception ex)
        {
            ShowException(ex);
            return false;
        }
    }

    private void ShowException(Exception exception)
    {
        var message = exception is TrainerException trainer
            ? LocalizationService.Text(trainer.ResourceKey, trainer.Message)
            : exception.Message;
        MessageBox.Show(message, LocalizationService.Text("Failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        ShowToast(message);
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void RevertCheck(CheckBox check, bool previousDesired)
    {
        _suppressUi = true;
        check.IsChecked = !previousDesired;
        _suppressUi = false;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
