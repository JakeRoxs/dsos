/*
 * Dark Souls 3 - Open Server
 * Copyright (C) 2021 Tim Leonard
 *
 * This program is free software; licensed under the MIT license. 
 * You should have received a copy of the license along with this program. 
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Loader.Services;

namespace Loader
{
  public enum GameType
  {
    DarkSouls3,
    DarkSouls2
  }

  public partial class MainForm : Form
  {
    private ServerConfigList ServerList = new ServerConfigList();
    private ServerListManager serverManager;
    private readonly ConfigService configService = new ConfigService();
    private readonly Launcher launcher = new Launcher();
    private readonly ServerQueryService queryService = new ServerQueryService();

    private CancellationTokenSource? _updateServerIpCts;

    private GameType CurrentGameType = GameType.DarkSouls3;

    private bool IgnoreInputChanges = false;

    private string MachinePrivateIp = "";
    private string MachinePublicIp = "";

    private string[] ColumnNames = { "Server Name", "Player Count", "Description" };

    public static string OfficialServer = NetUtils.HostnameToIPv4("dsos.jakesws.xyz");

    public MainForm()
    {
      InitializeComponent();

      // panel1 contains banner and link; set up image and sizing
      panel1.Dock = DockStyle.Top;
      panel1.BackgroundImage = Properties.Resources.banner;
      // stretch horizontally but keep vertical aspect by using Zoom instead of Stretch
      panel1.BackgroundImageLayout = ImageLayout.Zoom;
      panel1.Height = Properties.Resources.banner.Height;
      GithubLink.Location = new System.Drawing.Point(5, 5);

      ImportedServerListView.Items.Clear();
      ImportedServerListView.ListViewItemSorter = new ServerListSorter();

      MachinePrivateIp = NetUtils.GetMachineIPv4(false);
      MachinePublicIp = NetUtils.GetMachineIPv4(true);

      // manager will be initialized once config is loaded
      serverManager = new ServerListManager(ServerList, CurrentGameType);

      _updateServerIpCts = new CancellationTokenSource();
      this.FormClosing += MainForm_FormClosing;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
      queryService.Cancel();
      _updateServerIpCts?.Cancel();
    }



    private void OnBrowseForExecutable(object sender, EventArgs e)
    {
      using (OpenFileDialog Dialog = new OpenFileDialog())
      {
        switch (CurrentGameType)
        {
          case GameType.DarkSouls3:
            {
              Dialog.Filter = "Dark Souls III|DarkSoulsIII.exe|All Files|*.*";
              Dialog.Title = resources.GetString("select_ds3_exe");
              break;
            }
          case GameType.DarkSouls2:
            {
              Dialog.Filter = "Dark Souls II|DarkSoulsII.exe|All Files|*.*";
              Dialog.Title = resources.GetString("select_ds2_exe");
              break;
            }
        }

        if (Dialog.ShowDialog() == DialogResult.OK)
        {
          ExeLocationTextBox.Text = Dialog.FileName;

          SaveConfig();
          ValidateUI();
        }
      }
    }

    // UI behavior methods are now implemented in MainForm.Extracted.cs partial class.







    // UI behavior methods are now implemented in MainForm.Extracted.cs partial class.

    private void SettingsButton_Click(object sender, EventArgs e)
    {
      SettingsForm dialog = new SettingsForm();
      dialog.ExeLocation = ExeLocationTextBox.Text;
      dialog.ShowDialog();
    }

    private void GameTabControl_SelectedIndexChanged(object sender, EventArgs e)
    {
      ApplyTabSettings();
      serverManager.CurrentGameType = CurrentGameType;
      ValidateUI();
      BuildServerList();
    }

    private void ApplyTabSettings()
    {
      CurrentGameType = GameType.DarkSouls2;
      if (gameTabControl.SelectedIndex == 1)
      {
        CurrentGameType = GameType.DarkSouls3;
      }

      switch (CurrentGameType)
      {
        case GameType.DarkSouls3:
          {
            ExeLocationTextBox!.Text = ProgramSettings.Default.ds3_exe_location;
            ExePathLabel!.Text = resources.GetString("ds3_location");
            break;
          }
        case GameType.DarkSouls2:
          {
            ExeLocationTextBox!.Text = ProgramSettings.Default.ds2_exe_location;
            ExePathLabel!.Text = resources.GetString("ds2_location");
            break;
          }
      }
    }
  }

  class ServerListSorter : System.Collections.IComparer
  {
    public int SortColumn = 1;
    public int SortOrder = 0; // 0="Smart" Order, 1=Ascending, 2=Descending

    public int Compare(object? x, object? y)
    {
      ServerConfig? a = (x as ListViewItem)?.Tag as ServerConfig;
      ServerConfig? b = (y as ListViewItem)?.Tag as ServerConfig;

      if (a == null || b == null)
      {
        return 0;
      }

      if (SortOrder == 0)
      {
        // Official server is always first.
        if (a.Hostname == MainForm.OfficialServer)
        {
          return -1;
        }
        if (b.Hostname == MainForm.OfficialServer)
        {
          return 1;
        }

        // Imported comes before public which comes before private.
        if (a.ManualImport != b.ManualImport)
        {
          return (b.ManualImport ? 1 : 0) - (a.ManualImport ? 1 : 0);
        }
        if (a.PasswordRequired != b.PasswordRequired)
        {
          return (b.ManualImport ? 1 : 0) - (a.ManualImport ? 1 : 0);
        }

        // Sort in each group by player count.
        return b.PlayerCount - a.PlayerCount;
      }
      else
      {
        int Result = 0;
        if (SortColumn == 0)
        {
          Result = a.Name.CompareTo(b.Name);
        }
        else if (SortColumn == 1)
        {
          Result = b.PlayerCount - a.PlayerCount;
        }
        else
        {
          Result = a.Description.CompareTo(b.Description);
        }

        if (SortOrder == 2)
        {
          Result = -Result;
        }

        return Result;
      }
    }
  }
}
