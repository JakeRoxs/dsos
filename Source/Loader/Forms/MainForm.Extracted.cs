#nullable enable
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Loader.Services;

namespace Loader
{
  public partial class MainForm
  {
    private void SaveConfig()
    {
      configService.SaveSettings(ServerList, ExeLocationTextBox.Text, hidePasswordedBox.Checked, (int)minimumPlayersBox.Value);
    }

    private void ValidateUI()
    {
      bool LaunchEnabled = true;

      DarkSoulsLoadConfig LoadConfig;

      if (!File.Exists(ExeLocationTextBox.Text))
      {
        ExeLocationTextBox.BackColor = System.Drawing.Color.Pink;
        BuildInfoLabel.Text = ExeLocationTextBox.Text.Length > 0 ? resources.GetString("executable_does_not_exist") : "";
        BuildInfoLabel.ForeColor = System.Drawing.Color.Red;
        LaunchEnabled = false;
      }
      else if (!BuildConfig.ExeLoadConfiguration.TryGetValue(ExeUtils.GetExeSimpleHash(ExeLocationTextBox.Text), out LoadConfig))
      {
        ExeLocationTextBox.BackColor = System.Drawing.Color.Pink;
        BuildInfoLabel.Text = resources.GetString("executable_not_a_recognised_version");
        BuildInfoLabel.ForeColor = System.Drawing.Color.Red;
        LaunchEnabled = false;
      }
      else
      {
        BuildInfoLabel.Text = resources.GetString("recognised_as") + LoadConfig.VersionName;
        BuildInfoLabel.ForeColor = System.Drawing.Color.Black;

        ExeLocationTextBox.BackColor = System.Drawing.SystemColors.Control;
      }

      if (ImportedServerListView.SelectedItems.Count <= 0)
      {
        LaunchEnabled = false;
      }

      if (!SteamUtils.IsSteamRunningAndLoggedIn())
      {
        LaunchEnabled = false;
        LaunchButton.Text = resources.GetString("not_logged_into_steam");
      }
#if RELEASE
      else if (IsLaunchedProcessRunning())
      {
        LaunchEnabled = false;
        LaunchButton.Text = resources.GetString("running");
      }
#endif
      else
      {
        LaunchButton.Text = resources.GetString("launch_game");
      }

      LaunchButton.Enabled = LaunchEnabled;
      RefreshButton.Enabled = !queryService.IsQueryInProgress;
    }

    private void BuildServerList()
    {
      var filtered = serverManager.Filter(filterBox.Text, hidePasswordedBox.Checked, (int)minimumPlayersBox.Value);
      ServerListViewUpdater.Update(ImportedServerListView, filtered, OfficialServer);
    }

    private async void OnLoaded(object sender, EventArgs e)
    {
      string predictedDs3 = SteamUtils.GetGameInstallPath("DARK SOULS III") + @"\Game\DarkSoulsIII.exe";
      if (string.IsNullOrWhiteSpace(configService.Ds3ExeLocation) && File.Exists(predictedDs3))
      {
        configService.Ds3ExeLocation = predictedDs3;
        ProgramSettings.Default.ds3_exe_location = predictedDs3;
      }

      string predictedDs2 = SteamUtils.GetGameInstallPath("Dark Souls II Scholar of the First Sin") + @"\Game\DarkSoulsII.exe";
      if (string.IsNullOrWhiteSpace(configService.Ds2ExeLocation) && File.Exists(predictedDs2))
      {
        configService.Ds2ExeLocation = predictedDs2;
        ProgramSettings.Default.ds2_exe_location = predictedDs2;
      }

      ServerList = configService.LoadSettings();

      IgnoreInputChanges = true;
      gameTabControl.SelectedIndex = 1;
      ExeLocationTextBox.Text = configService.Ds3ExeLocation;
      hidePasswordedBox.Checked = configService.HidePassworded;
      minimumPlayersBox.Value = configService.MinimumPlayers;
      IgnoreInputChanges = false;

      serverManager = new ServerListManager(ServerList, CurrentGameType);

      ServerList.Servers.RemoveAll(s => !s.ManualImport);

      ApplyTabSettings();

      ValidateUI();
      BuildServerList();
      await QueryServersAsync(CancellationToken.None);

      ContinualUpdateTimer.Enabled = launcher.ShouldRunContinualUpdate();

      privateIpBox.Text = MachinePrivateIp;
      publicIpBox.Text = MachinePublicIp;
    }

    private void OnCreateNewServer(object sender, EventArgs e)
    {
      Forms.CreateServerDialog Dialog = new Forms.CreateServerDialog(ServerList.Servers, MachinePublicIp, this, CurrentGameType);
      if (Dialog.ShowDialog() != DialogResult.OK)
      {
        return;
      }
    }

    private ServerConfig? CurrentServerConfig;

    private async void OnSelectedServerChanged(object sender, EventArgs e)
    {
      CurrentServerConfig = ImportedServerListView.SelectedItems.Count > 0
          ? GetConfigFromId((ImportedServerListView.SelectedItems[0].Tag as ServerConfig)!.Id)
          : null;

      ValidateUI();

      _updateServerIpCts?.Cancel();
      _updateServerIpCts?.Dispose();
      _updateServerIpCts = new CancellationTokenSource();

      if (CurrentServerConfig != null)
      {
        await UpdateServerIpAsync(CurrentServerConfig, _updateServerIpCts.Token);
      }
    }

    private void OnRemoveClicked(object sender, EventArgs e)
    {
      if (ImportedServerListView.SelectedItems.Count > 0)
      {
        ServerConfig? Config = GetConfigFromId((ImportedServerListView.SelectedItems[0].Tag as ServerConfig)!.Id);
        if (Config == null) return;

        ServerList.Servers.RemoveAll(s => s.Hostname == Config.Hostname);

        BuildServerList();
        SaveConfig();
        ValidateUI();
      }
    }

    protected virtual Task<string> ResolveConnectIpAsync(ServerConfig config, CancellationToken cancellationToken)
    {
      return Task.Run(() => launcher.ResolveConnectIp(config, MachinePublicIp, MachinePrivateIp), cancellationToken);
    }

    protected virtual Task<string> GetPublicKeyAsync(string id, string password, CancellationToken cancellationToken)
    {
      return Task.Run(() => HubApi.GetPublicKey(id, password), cancellationToken);
    }

    internal async Task UpdateServerIpAsync(ServerConfig updateConfig, CancellationToken cancellationToken)
    {
      if (updateConfig == null)
        return;

      string ip;
      try
      {
        ip = await ResolveConnectIpAsync(updateConfig, cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return;
      }

      if (cancellationToken.IsCancellationRequested)
        return;

      if (CurrentServerConfig != null && CurrentServerConfig.Hostname == updateConfig.Hostname)
      {
        serverIpBox.Text = ip;
      }
    }

    protected virtual Task<List<ServerConfig>> QueryServersFromHubAsync(CancellationToken cancellationToken)
    {
      // HubApi.ListServers now returns an empty list on failure instead of null.
      return Task.Run(() => HubApi.ListServers(), cancellationToken);
    }

    internal async Task QueryServersAsync(CancellationToken cancellationToken)
    {
      var servers = await queryService.QueryServersAsync(cancellationToken);

      if (servers == null)
      {
        RefreshButton.Enabled = true;
        return;
      }

      ProcessServerQueryResponse(servers);
      RefreshButton.Enabled = true;
    }

    private void ProcessServerQueryResponse(List<ServerConfig> Servers)
    {
      serverManager.AddOrUpdate(Servers);
      BuildServerList();
    }

    private ServerConfig? GetConfigFromId(string Id)
    {
      return serverManager.GetById(Id);
    }

    private async void OnLaunch(object sender, EventArgs e)
    {
      ServerConfig? Config = GetConfigFromId((ImportedServerListView.SelectedItems[0].Tag as ServerConfig)!.Id);
      if (Config == null)
      {
        return;
      }

      if (string.IsNullOrEmpty(Config.PublicKey))
      {
        if (Config.PasswordRequired)
        {
          Forms.PasswordDialog Dialog = new Forms.PasswordDialog(Config);
          if (Dialog.ShowDialog() != DialogResult.OK || string.IsNullOrEmpty(Config.PublicKey))
          {
            return;
          }
        }
        else
        {
          try
          {
            Config.PublicKey = await GetPublicKeyAsync(Config.Id, "", CancellationToken.None);
          }
          catch (OperationCanceledException)
          {
            return;
          }

          if (string.IsNullOrEmpty(Config.PublicKey))
          {
            MessageBox.Show(resources.GetString("failed_to_retrieve_keys"), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
          }
        }
      }

      PerformLaunch(Config);
    }

    void PerformLaunch(ServerConfig Config)
    {
      if (!launcher.PerformLaunch(Config, ExeLocationTextBox.Text, MachinePublicIp, MachinePrivateIp, ProgramSettings.Default.use_separate_saves, out var errorKey))
      {
        if (!string.IsNullOrEmpty(errorKey))
        {
          var text = resources.GetString(errorKey);
          if (string.IsNullOrEmpty(text))
          {
            text = errorKey;
          }

          MessageBox.Show(text, resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return;
      }

      ContinualUpdateTimer.Enabled = launcher.ShouldRunContinualUpdate();
      ValidateUI();
    }

    private bool IsLaunchedProcessRunning()
    {
      return launcher.IsProcessRunning();
    }

    private void OnContinualUpdateTimer(object sender, EventArgs e)
    {
      if (!IsLaunchedProcessRunning())
      {
        launcher.ClearProcess();
      }

      ValidateUI();
      ContinualUpdateTimer.Enabled = launcher.ShouldRunContinualUpdate();
    }

    private async void OnServerRefreshTimer(object sender, EventArgs e)
    {
      await QueryServersAsync(CancellationToken.None);
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
      await QueryServersAsync(CancellationToken.None);
    }

    private void OnClickGithubLink(object sender, LinkLabelLinkClickedEventArgs e)
    {
      Process.Start(new ProcessStartInfo
      {
        FileName = "https://github.com/jakeroxs/rekindled-server",
        UseShellExecute = true
      });
    }

    private void OnFilterPropertyChanged(object sender, EventArgs e)
    {
      if (IgnoreInputChanges)
      {
        return;
      }

      SaveConfig();
      BuildServerList();
    }

    private void OnColumnClicked(object sender, ColumnClickEventArgs e)
    {
      ServerListSorter? Sorter = ImportedServerListView.ListViewItemSorter as ServerListSorter;
      if (Sorter == null)
      {
        return;
      }
      if (Sorter.SortColumn != e.Column)
      {
        if (Sorter.SortColumn != -1)
        {
          ImportedServerListView.Columns[Sorter.SortColumn].Text = ColumnNames[Sorter.SortColumn];
        }
        Sorter.SortColumn = e.Column;
      }
      ImportedServerListView.Sort();
      BuildServerList();
    }
  }
}
