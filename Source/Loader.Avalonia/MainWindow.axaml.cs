using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using Loader.Services;

namespace Loader
{
  public partial class MainWindow : Window
  {
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RekindledServer",
        "loader-avalonia-startup.log");

    private readonly ConfigService _configService = new ConfigService();
    private readonly ServerQueryService _queryService = new ServerQueryService();
    private readonly LaunchCoordinator _launchCoordinator = new LaunchCoordinator();

    private ServerConfigList _serverList = new();
    private ServerListManager? _serverManager;
    private CancellationTokenSource? _refreshCts;
    private bool _isInitialized;
    private GameType _currentGameType = GameType.DarkSouls3;
    private KeyDeviceType _lastInputDeviceType = KeyDeviceType.Keyboard;

    public ObservableCollection<ServerConfig> Servers { get; } = new();

    public MainWindow()
    {
      LogStartup("MainWindow ctor enter.");

      try
      {
        InitializeComponent();
        LogStartup("MainWindow InitializeComponent completed.");

        DataContext = this;

        Opened += OnOpened;
        Closing += (_, _) =>
        {
          LogStartup("MainWindow Closing handler invoked.");
          _queryService.Cancel();
          _queryService.Dispose();
          _refreshCts?.Cancel();
          _refreshCts?.Dispose();
        };

        LogStartup("MainWindow ctor exit.");
      }
      catch (Exception ex)
      {
        LogStartup($"MainWindow ctor exception: {ex}");
        throw;
      }
    }

    private void InitializeComponent()
    {
      LogStartup("MainWindow.InitializeComponent load start.");
      InitializeComponent(true);
      LogStartup("MainWindow.InitializeComponent load end.");
    }

    private void OnOpened(object? sender, EventArgs e)
    {
      LogStartup("MainWindow OnOpened enter.");
      LoadConfig();
      BuildServerList();
      _isInitialized = true;

      Dispatcher.UIThread.Post(() =>
      {
        UpdateLaunchButtonLabelForPlatform();
        FocusServerListAndEnsureSelection();
        UpdateSelectionUiState();
        UpdateShortcutHint(_lastInputDeviceType);
      });

      LogStartup("MainWindow OnOpened exit.");
    }

    private static void LogStartup(string message)
    {
      try
      {
        string? directory = Path.GetDirectoryName(StartupLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
          Directory.CreateDirectory(directory);
        }

        string line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
        File.AppendAllText(StartupLogPath, line);
      }
      catch
      {
        // best-effort diagnostics only
      }
    }

    private void UpdateLaunchButtonLabelForPlatform()
    {
      LaunchButton.Content = _launchCoordinator.CanLaunchOnCurrentPlatform
          ? "Launch (Baseline)"
          : "Prepare Launch";
    }

    private void LoadConfig()
    {
      _serverList = _configService.LoadSettings();
      _serverManager = new ServerListManager(_serverList, _currentGameType);

      _configService.UseSeparateSaves = ProgramSettings.Default.use_separate_saves;

      ExeLocationTextBox.Text = _currentGameType == GameType.DarkSouls3
          ? ProgramSettings.Default.ds3_exe_location
          : ProgramSettings.Default.ds2_exe_location;

      FilterBox.Text = string.Empty;
      HidePasswordedBox.IsChecked = _configService.HidePassworded;
      MinimumPlayersBox.Value = _configService.MinimumPlayers;
      UseSeparateSavesBox.IsChecked = _configService.UseSeparateSaves;

      StatusText.Text = $"Loaded {_serverList.Servers.Count} server(s).";
    }

    private void BuildServerList()
    {
      if (_serverManager == null)
      {
        return;
      }

      string? selectedServerId = GetSelectedServer()?.Id;

      string filter = FilterBox.Text ?? string.Empty;
      bool hidePassworded = HidePasswordedBox.IsChecked ?? true;
      int minimumPlayers = (int)(MinimumPlayersBox.Value ?? 0);

      List<ServerConfig> items = _serverManager.Filter(filter, hidePassworded, minimumPlayers)
          .OrderByDescending(x => x.PlayerCount)
          .ToList();

      Servers.Clear();
      foreach (ServerConfig item in items)
      {
        Servers.Add(item);
      }

      RestoreServerSelection(selectedServerId);
      UpdateSelectionUiState();
    }

    private void RestoreServerSelection(string? selectedServerId)
    {
      if (Servers.Count == 0)
      {
        ServerListBox.SelectedIndex = -1;
        return;
      }

      if (!string.IsNullOrWhiteSpace(selectedServerId))
      {
        int selectedIndex = Servers
            .Select((server, index) => new { server, index })
            .Where(x => string.Equals(x.server.Id, selectedServerId, StringComparison.Ordinal))
            .Select(x => x.index)
            .DefaultIfEmpty(-1)
            .First();

        if (selectedIndex >= 0)
        {
          ServerListBox.SelectedIndex = selectedIndex;
          return;
        }
      }

      ServerListBox.SelectedIndex = 0;
    }

    private void UpdateSelectionUiState()
    {
      ServerConfig? selected = GetSelectedServer();
      bool hasSelection = selected != null;
      bool canOpenWeb = hasSelection && !string.IsNullOrWhiteSpace(selected?.WebAddress);

      CopyHostButton.IsEnabled = hasSelection;
      OpenWebButton.IsEnabled = canOpenWeb;
      LaunchButton.IsEnabled = hasSelection;

      if (!hasSelection)
      {
        OpenWebButton.Content = "Open Web";
        ToolTip.SetTip(OpenWebButton, "Select a server to open its web dashboard.");
      }
      else if (canOpenWeb)
      {
        OpenWebButton.Content = "Open Web";
        ToolTip.SetTip(OpenWebButton, "Open the selected server web dashboard.");
      }
      else
      {
        OpenWebButton.Content = "Open Web (N/A)";
        ToolTip.SetTip(OpenWebButton, "The selected server does not provide a web dashboard URL.");
      }

      if (selected != null)
      {
        StatusText.Text = BuildServerDisplay(selected);
      }
      else
      {
        StatusText.Text = Servers.Count == 0
            ? $"No servers available for {_currentGameType}. Try Refresh."
            : $"Showing {Servers.Count} server(s) for {_currentGameType}. Select a server to continue.";
      }

      EnsureFocusTargetsRemainActionable();
    }

    private void EnsureFocusTargetsRemainActionable()
    {
      if (!CopyHostButton.IsEnabled && !OpenWebButton.IsEnabled && !LaunchButton.IsEnabled)
      {
        if (IsActionButtonFocusWithin())
        {
          FocusServerListAndEnsureSelection();
        }

        return;
      }

      if (LaunchButton.IsKeyboardFocusWithin && !LaunchButton.IsEnabled)
      {
        FocusPrimaryActionButton();
        return;
      }

      if (OpenWebButton.IsKeyboardFocusWithin && !OpenWebButton.IsEnabled)
      {
        FocusPrimaryActionButton();
      }
    }

    private void UpdateShortcutHint(KeyDeviceType keyDeviceType)
    {
      _lastInputDeviceType = keyDeviceType;

      string hint = keyDeviceType switch
      {
        KeyDeviceType.Gamepad => "Input: Controller • A = Action • B = Back • D-pad = Navigate • L1/R1 = Game Tabs",
        KeyDeviceType.Remote => "Input: Remote • OK = Action • Back = Return • D-pad = Navigate • Channel +/- = Game Tabs",
        _ => "Input: Keyboard/Mouse • Enter/Space = Action • Esc = Back • Arrows = Navigate • PageUp/PageDown = Game Tabs"
      };

      if (!_launchCoordinator.CanLaunchOnCurrentPlatform)
      {
        hint += " • Launch Button = Prepare Launch Plan";
      }

      ShortcutHintText.Text = hint;
    }

    private ServerConfig? GetSelectedServer()
    {
      return ServerListBox.SelectedItem as ServerConfig;
    }

    private static string BuildServerDisplay(ServerConfig config)
    {
      StringBuilder builder = new StringBuilder();
      builder.AppendLine(config.Name);

      if (!string.IsNullOrWhiteSpace(config.Description))
      {
        builder.AppendLine(config.Description);
      }

      builder.AppendLine($"Host: {config.Hostname}:{config.Port}");
      if (string.IsNullOrWhiteSpace(config.WebAddress))
      {
        builder.AppendLine("Web: Not available");
      }
      else
      {
        builder.AppendLine($"Web: {config.WebAddress}");
      }

      builder.AppendLine($"Game: {config.GameType}");
      builder.AppendLine($"Players: {config.PlayerCount}");
      return builder.ToString();
    }

    private async Task ShowInfoAsync(string title, string message)
    {
      Window dialog = new Window
      {
        Width = 560,
        MinWidth = 420,
        Height = 280,
        Title = title,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new Grid
        {
          RowDefinitions = new RowDefinitions("*,Auto"),
          Margin = new Thickness(16),
          Children =
          {
            new TextBlock
            {
              Text = message,
              TextWrapping = TextWrapping.Wrap
            },
            new Button
            {
              Content = "OK",
              HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
              MinWidth = 110,
              MinHeight = 42,
              [Grid.RowProperty] = 1
            }
          }
        }
      };

      if (dialog.Content is Grid grid && grid.Children[1] is Button okButton)
      {
        okButton.Click += (_, _) => dialog.Close();
      }

      await dialog.ShowDialog(this);
    }

    private async Task RefreshAsync()
    {
      if (_serverManager == null)
      {
        return;
      }

      RefreshButton.IsEnabled = false;
      RefreshProgress.IsVisible = true;
      StatusText.Text = "Refreshing servers from hub...";

      _refreshCts?.Cancel();
      _refreshCts?.Dispose();
      _refreshCts = new CancellationTokenSource();

      try
      {
        List<ServerConfig>? servers = await _queryService.QueryServersAsync(_refreshCts.Token);
        if (servers is { Count: > 0 })
        {
          _serverManager.AddOrUpdate(servers);
          BuildServerList();
          StatusText.Text = $"Refresh complete. Retrieved {servers.Count} server(s).";
        }
        else
        {
          StatusText.Text = "Refresh complete. No server changes returned.";
        }
      }
      catch (Exception ex)
      {
        StatusText.Text = $"Refresh failed: {ex.Message}";
      }
      finally
      {
        RefreshProgress.IsVisible = false;
        RefreshButton.IsEnabled = true;
      }
    }

    private void SaveConfigIfPossible()
    {
      if (_serverManager == null)
      {
        return;
      }

      _configService.UseSeparateSaves = UseSeparateSavesBox.IsChecked ?? true;

      string exePath = ExeLocationTextBox.Text ?? string.Empty;
      if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
      {
        return;
      }

      try
      {
        ProgramSettings.Default.use_separate_saves = _configService.UseSeparateSaves;
        ProgramSettings.Default.Save();

        _configService.SaveSettings(
            _serverList,
            exePath,
            HidePasswordedBox.IsChecked ?? true,
            (int)(MinimumPlayersBox.Value ?? 0));
      }
      catch
      {
        // Non-fatal for shell preview.
      }
    }

    private void UpdateGameTypeFromTabSelection()
    {
      _currentGameType = GameTabControl.SelectedIndex == 0 ? GameType.DarkSouls2SotFS : GameType.DarkSouls3;

      if (_serverManager != null)
      {
        _serverManager.CurrentGameType = _currentGameType;
      }

      ExeLocationTextBox.Text = _currentGameType == GameType.DarkSouls3
          ? ProgramSettings.Default.ds3_exe_location
          : ProgramSettings.Default.ds2_exe_location;

      BuildServerList();
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
      await RefreshAsync();
    }

    private async void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
      ServerConfig? selected = GetSelectedServer();
      if (selected == null)
      {
        StatusText.Text = "Select a server before launching.";
        return;
      }

      string exePath = ExeLocationTextBox.Text ?? string.Empty;

      if (!_launchCoordinator.CanLaunchOnCurrentPlatform)
      {
        bool prepared = _launchCoordinator.TryPrepareLaunch(
            selected,
            exePath,
            _currentGameType,
            _configService.UseSeparateSaves,
            out string preparationMessage);

        if (!prepared)
        {
          StatusText.Text = preparationMessage;
          await ShowInfoAsync("Launch Blocked", preparationMessage);
          return;
        }

        SaveConfigIfPossible();
        StatusText.Text = "Prepared launch plan for Linux/Steam Deck. Auto launch integration is pending.";
        await ShowInfoAsync(
            "Launch Plan (Linux/Steam Deck Preview)",
            preparationMessage + Environment.NewLine + Environment.NewLine +
            "Automatic process start + injection on Linux/Steam Deck will be completed in the proton-injector integration slice.");
        return;
      }

      bool launched = _launchCoordinator.TryExecuteLaunch(
          selected,
          exePath,
          _currentGameType,
          _configService.UseSeparateSaves,
          out string launchResultMessage);

      if (!launched)
      {
        StatusText.Text = launchResultMessage;
        await ShowInfoAsync("Launch Blocked", launchResultMessage);
        return;
      }

      SaveConfigIfPossible();
      await ShowInfoAsync("Launch Started", launchResultMessage);
      StatusText.Text = $"Launch started for '{selected.Name}'.";
    }

    private async void BrowseButton_OnClick(object? sender, RoutedEventArgs e)
    {
      TopLevel? topLevel = TopLevel.GetTopLevel(this);
      if (topLevel?.StorageProvider == null)
      {
        StatusText.Text = "Storage provider unavailable for file browsing.";
        return;
      }

      FilePickerOpenOptions options = new FilePickerOpenOptions
      {
        AllowMultiple = false,
        Title = _currentGameType == GameType.DarkSouls3 ? "Select Dark Souls III executable" : "Select Dark Souls II executable",
        FileTypeFilter = new List<FilePickerFileType>
        {
          new FilePickerFileType("Executable")
          {
            Patterns = new[]
            {
              "*.exe"
            }
          }
        }
      };

      IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
      IStorageFile? first = files.FirstOrDefault();
      if (first is null)
      {
        return;
      }

      string? path = first.TryGetLocalPath();
      if (string.IsNullOrWhiteSpace(path))
      {
        StatusText.Text = "Selected file is not accessible via a local filesystem path.";
        return;
      }

      ExeLocationTextBox.Text = path;
      if (_currentGameType == GameType.DarkSouls3)
      {
        ProgramSettings.Default.ds3_exe_location = path;
      }
      else
      {
        ProgramSettings.Default.ds2_exe_location = path;
      }

      ProgramSettings.Default.Save();
      SaveConfigIfPossible();
      StatusText.Text = "Executable path updated.";
    }

    private void FilterBox_OnTextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
      BuildServerList();
    }

    private void MinimumPlayersBox_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
      BuildServerList();
    }

    private void HidePasswordedBox_OnClick(object? sender, RoutedEventArgs e)
    {
      BuildServerList();
    }

    private void UseSeparateSavesBox_OnClick(object? sender, RoutedEventArgs e)
    {
      _configService.UseSeparateSaves = UseSeparateSavesBox.IsChecked ?? true;
      ProgramSettings.Default.use_separate_saves = _configService.UseSeparateSaves;
      ProgramSettings.Default.Save();

      StatusText.Text = _configService.UseSeparateSaves
          ? "Separate save files enabled."
          : "Separate save files disabled.";
    }

    private void GameTabControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
      if (!_isInitialized)
      {
        return;
      }

      UpdateGameTypeFromTabSelection();
    }

    private async void CopyHostButton_OnClick(object? sender, RoutedEventArgs e)
    {
      ServerConfig? selected = GetSelectedServer();
      if (selected == null)
      {
        StatusText.Text = "Select a server first.";
        return;
      }

      if (TopLevel.GetTopLevel(this)?.Clipboard == null)
      {
        StatusText.Text = "Clipboard unavailable.";
        return;
      }

      await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync($"{selected.Hostname}:{selected.Port}");
      StatusText.Text = $"Copied host for '{selected.Name}'.";
    }

    private async void OpenWebButton_OnClick(object? sender, RoutedEventArgs e)
    {
      ServerConfig? selected = GetSelectedServer();
      if (selected == null)
      {
        StatusText.Text = "Select a server first.";
        return;
      }

      if (string.IsNullOrWhiteSpace(selected.WebAddress))
      {
        await ShowInfoAsync("No Web Address", "The selected server does not provide a web dashboard URL.");
        return;
      }

      try
      {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
          FileName = selected.WebAddress,
          UseShellExecute = true
        });
        StatusText.Text = $"Opened web dashboard for '{selected.Name}'.";
      }
      catch (Exception ex)
      {
        StatusText.Text = $"Failed to open browser: {ex.Message}";
      }
    }

    private void ServerListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
      UpdateSelectionUiState();
    }

    private void FocusServerListAndEnsureSelection()
    {
      if (Servers.Count > 0 && ServerListBox.SelectedIndex < 0)
      {
        ServerListBox.SelectedIndex = 0;
      }

      ServerListBox.Focus();
    }

    private bool IsActionButtonFocusWithin()
    {
      return CopyHostButton.IsKeyboardFocusWithin
          || OpenWebButton.IsKeyboardFocusWithin
          || LaunchButton.IsKeyboardFocusWithin;
    }

    private void MoveActionButtonFocus(int delta)
    {
      Button[] buttons =
      {
        CopyHostButton,
        OpenWebButton,
        LaunchButton
      };

      if (!buttons.Any(button => button.IsEnabled))
      {
        return;
      }

      int current = Array.FindIndex(buttons, button => button.IsKeyboardFocusWithin || button.IsFocused);
      if (current < 0)
      {
        IEnumerable<Button> ordered = delta >= 0 ? buttons : buttons.Reverse();
        Button? entry = ordered.FirstOrDefault(button => button.IsEnabled);
        entry?.Focus();
        return;
      }

      for (int offset = 1; offset <= buttons.Length; offset++)
      {
        int target = (current + (delta * offset) + buttons.Length) % buttons.Length;
        if (buttons[target].IsEnabled)
        {
          buttons[target].Focus();
          return;
        }
      }
    }

    private bool FocusPrimaryActionButton()
    {
      Button? target = LaunchButton.IsEnabled
          ? LaunchButton
          : OpenWebButton.IsEnabled
              ? OpenWebButton
              : CopyHostButton.IsEnabled
                  ? CopyHostButton
                  : null;

      if (target == null)
      {
        return false;
      }

      target.Focus();
      return true;
    }

    private void SelectRelativeServer(int delta)
    {
      if (Servers.Count == 0)
      {
        return;
      }

      int current = ServerListBox.SelectedIndex;
      if (current < 0)
      {
        ServerListBox.SelectedIndex = delta >= 0 ? 0 : Servers.Count - 1;
        return;
      }

      ServerListBox.SelectedIndex = (current + delta + Servers.Count) % Servers.Count;
    }

    private void CyclePrimaryFocus(bool forward)
    {
      Control[] focusTargets =
      {
        GameTabControl,
        ExeLocationTextBox,
        BrowseButton,
        FilterBox,
        MinimumPlayersBox,
        HidePasswordedBox,
        UseSeparateSavesBox,
        RefreshButton,
        ServerListBox,
        CopyHostButton,
        OpenWebButton,
        LaunchButton
      };

      int current = Array.FindIndex(focusTargets, control => control.IsKeyboardFocusWithin || control.IsFocused);
      int next = current;
      for (int i = 0; i < focusTargets.Length; i++)
      {
        next = next < 0
            ? (forward ? 0 : focusTargets.Length - 1)
            : (next + (forward ? 1 : -1) + focusTargets.Length) % focusTargets.Length;

        if (focusTargets[next].IsEnabled)
        {
          break;
        }
      }

      if (!focusTargets[next].IsEnabled)
      {
        return;
      }

      if (ReferenceEquals(focusTargets[next], ServerListBox))
      {
        FocusServerListAndEnsureSelection();
      }
      else
      {
        focusTargets[next].Focus();
      }
    }

    private void ShiftGameTab(int delta)
    {
      int tabCount = GameTabControl.ItemCount;
      if (tabCount <= 0)
      {
        return;
      }

      int currentIndex = GameTabControl.SelectedIndex < 0 ? 0 : GameTabControl.SelectedIndex;
      int index = (currentIndex + delta + tabCount) % tabCount;
      if (index != currentIndex)
      {
        GameTabControl.SelectedIndex = index;
      }
    }

    private void TriggerPrimaryActionForCurrentFocus()
    {
      if (BrowseButton.IsKeyboardFocusWithin && BrowseButton.IsEnabled)
      {
        BrowseButton_OnClick(this, new RoutedEventArgs());
        return;
      }

      if (RefreshButton.IsKeyboardFocusWithin && RefreshButton.IsEnabled)
      {
        _ = RefreshAsync();
        return;
      }

      if (OpenWebButton.IsKeyboardFocusWithin)
      {
        if (!OpenWebButton.IsEnabled)
        {
          StatusText.Text = "Selected server does not provide a web dashboard URL.";
          return;
        }

        OpenWebButton_OnClick(this, new RoutedEventArgs());
        return;
      }

      if (CopyHostButton.IsKeyboardFocusWithin && CopyHostButton.IsEnabled)
      {
        CopyHostButton_OnClick(this, new RoutedEventArgs());
        return;
      }

      if (LaunchButton.IsKeyboardFocusWithin)
      {
        if (!LaunchButton.IsEnabled)
        {
          StatusText.Text = "Select a server before launching.";
          return;
        }

        LaunchButton_OnClick(this, new RoutedEventArgs());
        return;
      }

      if (!LaunchButton.IsEnabled)
      {
        StatusText.Text = "Select a server before launching.";
        return;
      }

      LaunchButton_OnClick(this, new RoutedEventArgs());
    }

    private void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
      UpdateShortcutHint(e.KeyDeviceType);

      if (e.Source is TextBox || e.Source is NumericUpDown)
      {
        return;
      }

      if (e.Source is CheckBox)
      {
        if (e.Key == Key.Left)
        {
          CyclePrimaryFocus(false);
          e.Handled = true;
          return;
        }

        if (e.Key == Key.Right)
        {
          CyclePrimaryFocus(true);
          e.Handled = true;
          return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
          return;
        }
      }

      if (e.Key == Key.F5)
      {
        _ = RefreshAsync();
        e.Handled = true;
        return;
      }

      if (e.Key == Key.Enter)
      {
        TriggerPrimaryActionForCurrentFocus();
        e.Handled = true;
        return;
      }

      if (e.Key == Key.Space)
      {
        TriggerPrimaryActionForCurrentFocus();
        e.Handled = true;
        return;
      }

      if (e.Key == Key.F6)
      {
        RefreshButton_OnClick(this, new RoutedEventArgs());
        e.Handled = true;
        return;
      }

      if (e.Key == Key.PageUp)
      {
        ShiftGameTab(-1);
        e.Handled = true;
        return;
      }

      if (e.Key == Key.PageDown)
      {
        ShiftGameTab(1);
        e.Handled = true;
        return;
      }

      if (e.Key == Key.Escape)
      {
        FocusServerListAndEnsureSelection();
        e.Handled = true;
        return;
      }

      if (e.Key == Key.Tab)
      {
        bool forward = (e.KeyModifiers & KeyModifiers.Shift) == 0;
        CyclePrimaryFocus(forward);
        e.Handled = true;
        return;
      }

      if (e.Key == Key.Up)
      {
        if (!ServerListBox.IsKeyboardFocusWithin)
        {
          FocusServerListAndEnsureSelection();
        }
        else
        {
          SelectRelativeServer(-1);
        }

        e.Handled = true;
        return;
      }

      if (e.Key == Key.Down)
      {
        if (!ServerListBox.IsKeyboardFocusWithin)
        {
          FocusServerListAndEnsureSelection();
        }
        else
        {
          SelectRelativeServer(1);
        }

        e.Handled = true;
        return;
      }

      if (e.Key == Key.Left)
      {
        if (IsActionButtonFocusWithin())
        {
          MoveActionButtonFocus(-1);
        }
        else if (ServerListBox.IsKeyboardFocusWithin)
        {
          CyclePrimaryFocus(false);
        }
        else if (GameTabControl.IsKeyboardFocusWithin)
        {
          ShiftGameTab(-1);
        }
        else
        {
          CyclePrimaryFocus(false);
        }

        e.Handled = true;
        return;
      }

      if (e.Key == Key.Right)
      {
        if (IsActionButtonFocusWithin())
        {
          MoveActionButtonFocus(1);
        }
        else if (GameTabControl.IsKeyboardFocusWithin)
        {
          ShiftGameTab(1);
        }
        else if (ServerListBox.IsKeyboardFocusWithin)
        {
          if (!FocusPrimaryActionButton())
          {
            CyclePrimaryFocus(true);
          }
        }
        else
        {
          CyclePrimaryFocus(true);
        }

        e.Handled = true;
      }
    }
  }
}
