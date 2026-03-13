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
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
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

        private IntPtr RunningProcessHandle = IntPtr.Zero;
        private uint RunningProcessId = 0;
        private Task? QueryServerTask = null;
        private CancellationTokenSource? _queryServerCts;
        private CancellationTokenSource? _updateServerIpCts;

        private GameType CurrentGameType = GameType.DarkSouls3;

        private bool IgnoreInputChanges = false;

        private string MachinePrivateIp = "";
        private string MachinePublicIp = "";

        private string[] ColumnNames = { "Server Name", "Player Count", "Description" };
        
        public static string OfficialServer = NetUtils.HostnameToIPv4("ds3os-server.timleonard.uk");

        public MainForm()
        {
            InitializeComponent();

            ImportedServerListView.Items.Clear();
            ImportedServerListView.ListViewItemSorter = new ServerListSorter();

            MachinePrivateIp = NetUtils.GetMachineIPv4(false);
            MachinePublicIp = NetUtils.GetMachineIPv4(true);

            // manager will be initialized once config is loaded
            serverManager = new ServerListManager(ServerList, CurrentGameType);

            _queryServerCts = new CancellationTokenSource();
            _updateServerIpCts = new CancellationTokenSource();
            this.FormClosing += MainForm_FormClosing;
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _queryServerCts?.Cancel();
            _updateServerIpCts?.Cancel();
        }

        private void SaveConfig()
        {        
            switch (CurrentGameType)
            {
                case GameType.DarkSouls3:
                {
                    ProgramSettings.Default.ds3_exe_location = ExeLocationTextBox.Text;
                    break;
                }
                case GameType.DarkSouls2:
                {
                    ProgramSettings.Default.ds2_exe_location = ExeLocationTextBox.Text;
                    break;
                }
            }

            ProgramSettings.Default.server_config_json = ServerList.ToJson();
            ProgramSettings.Default.hide_passworded = hidePasswordedBox.Checked;
            ProgramSettings.Default.minimum_players = (int)minimumPlayersBox.Value;

            ProgramSettings.Default.Save();
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

            bool HasSelectedManualServer = false;
            if (ImportedServerListView.SelectedIndices.Count > 0)
            {
                HasSelectedManualServer = GetConfigFromId((ImportedServerListView.SelectedItems[0].Tag as ServerConfig)!.Id)!.ManualImport;
            }
            //RemoveButton.Enabled = HasSelectedManualServer;

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
            else if (RunningProcessHandle != IntPtr.Zero)
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

            RefreshButton.Enabled = (QueryServerTask != null);
        }


        private void BuildServerList()
        {
            var filtered = serverManager.Filter(filterBox.Text, hidePasswordedBox.Checked, (int)minimumPlayersBox.Value);
            ServerListViewUpdater.Update(ImportedServerListView, filtered, OfficialServer);
        }

        private async void OnLoaded(object sender, EventArgs e)
        {
            string PredictedInstallPath = SteamUtils.GetGameInstallPath("DARK SOULS III") + @"\Game\DarkSoulsIII.exe";
            if (!File.Exists(ProgramSettings.Default.ds3_exe_location) && File.Exists(PredictedInstallPath))
            {
                ProgramSettings.Default.ds3_exe_location = PredictedInstallPath;
            }
            
            PredictedInstallPath = SteamUtils.GetGameInstallPath("Dark Souls II Scholar of the First Sin") + @"\Game\DarkSoulsII.exe";
            if (!File.Exists(ProgramSettings.Default.ds2_exe_location) && File.Exists(PredictedInstallPath))
            {
                ProgramSettings.Default.ds2_exe_location = PredictedInstallPath;
            }

            IgnoreInputChanges = true;
            gameTabControl.SelectedIndex = 1;
            ExeLocationTextBox.Text = ProgramSettings.Default.ds3_exe_location;
            hidePasswordedBox.Checked = ProgramSettings.Default.hide_passworded;
            minimumPlayersBox.Value = ProgramSettings.Default.minimum_players;
            ServerConfigList.FromJson(ProgramSettings.Default.server_config_json, out ServerList);
            IgnoreInputChanges = false;

            // create manager now that we have loaded the list
            serverManager = new ServerListManager(ServerList, CurrentGameType);

#if false//DEBUG
            ProgramSettings.Default.Reset();
            ProgramSettings.Default.master_server_url = "http://127.0.0.1:50020";
#endif
            
            // Strip out any old config files downloaded from the server, we will be querying them
            // shortly anyway.
            ServerList.Servers.RemoveAll(s => !s.ManualImport);
            
            ApplyTabSettings();

            ValidateUI();
            BuildServerList();
            await QueryServersAsync(_queryServerCts?.Token ?? CancellationToken.None);

            ContinualUpdateTimer.Enabled = ShouldRunContinualUpdate();

            privateIpBox.Text = MachinePrivateIp;
            publicIpBox.Text = MachinePublicIp;
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
            if (ImportedServerListView.SelectedItems.Count > 0)
            {
                CurrentServerConfig = GetConfigFromId((ImportedServerListView.SelectedItems[0].Tag as ServerConfig)!.Id);
            }

            ValidateUI();

            _updateServerIpCts?.Cancel();
            _updateServerIpCts = new CancellationTokenSource();
            await UpdateServerIpAsync(_updateServerIpCts.Token);
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
            return Task.Run(() => ResolveConnectIp(config), cancellationToken);
        }

        protected virtual Task<string> GetPublicKeyAsync(string id, string password, CancellationToken cancellationToken)
        {
            return Task.Run(() => MasterServerApi.GetPublicKey(id, password), cancellationToken);
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

        protected virtual Task<List<ServerConfig>?> QueryServersFromMasterAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => MasterServerApi.ListServers(), cancellationToken);
        }

        internal async Task QueryServersAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("Querying master server ...");

            if (QueryServerTask != null && !QueryServerTask.IsCompleted)
            {
                return;
            }

            RefreshButton.Enabled = false;
            _queryServerCts?.Cancel();
            _queryServerCts = new CancellationTokenSource();
            cancellationToken = _queryServerCts.Token;

            QueryServerTask = QueryServersFromMasterAsync(cancellationToken);

            List<ServerConfig>? servers = null;
            try
            {
                servers = await (Task<List<ServerConfig>?>)QueryServerTask;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            if (servers != null)
            {
                ProcessServerQueryResponse(servers);
            }

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
                        Config.PublicKey = await GetPublicKeyAsync(Config.Id, "", _queryServerCts?.Token ?? CancellationToken.None);
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

        string ResolveConnectIp(ServerConfig Config)
        {
            string ConnectionHostname = Config.Hostname;
            string HostnameIp = NetUtils.HostnameToIPv4(Config.Hostname);
            string PrivateHostnameIp = NetUtils.HostnameToIPv4(Config.PrivateHostname);

            // If the servers public ip is the same as the machines public ip, then we are behind
            // the same nat and should use the private hostname instead. 
            //
            // Note: This potentially breaks down with carrier grade NAT.
            // ... We're sort of ignoring that right now as this helps the majority of users.
            // Those behind CGN can manually set the ip's on the servers config to get around this.
            if (HostnameIp == MachinePublicIp)
            {
                // If ip of private hostname and private ip of machine are the same
                // then server is running on same host so just use loopback address.
                if (PrivateHostnameIp == MachinePrivateIp)
                {
                    ConnectionHostname = "127.0.0.1";
                }
                // Otherwise just use the private address.
                else
                {
                    ConnectionHostname = Config.PrivateHostname;
                }
            }

            return ConnectionHostname;
        }

        void PerformLaunch(ServerConfig Config)
        {
            if (Config.PublicKey == null || Config.PublicKey.Length == 0)
            {
                MessageBox.Show(resources.GetString("no_public_key_available"), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Try and kill existing process mutex if we can.
            if (RunningProcessHandle != IntPtr.Zero)
            {
#if DEBUG
                KillNamedMutexIfExists();
#endif
            }

            string ConnectionHostname = ResolveConnectIp(Config);
            
            DarkSoulsLoadConfig LoadConfig;
            if (!BuildConfig.ExeLoadConfiguration.TryGetValue(ExeUtils.GetExeSimpleHash(ExeLocationTextBox.Text), out LoadConfig))
            {
                MessageBox.Show(resources.GetString("failed_to_determine_exe_version"), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string ExeLocation = ExeLocationTextBox.Text;
            string? ExeDirectory = Path.GetDirectoryName(ExeLocation);
 
            string AppIdFile = Path.Combine(ExeDirectory!, "steam_appid.txt");
            File.WriteAllText(AppIdFile, LoadConfig.SteamAppId.ToString());

            STARTUPINFO StartupInfo = new STARTUPINFO();
            PROCESS_INFORMATION ProcessInfo = new PROCESS_INFORMATION();

            bool Result = WinAPI.CreateProcess(
                null,
                "\"" + ExeLocation + "\"",
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                ProcessCreationFlags.ZERO_FLAG, 
                IntPtr.Zero,
                ExeDirectory,
                ref StartupInfo,
                out ProcessInfo
            );

            if (!Result)
            {
                MessageBox.Show(resources.GetString("failed_to_run_ds3_exe"), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Inject our hook DLL.
            if (LoadConfig.UseInjector)
            {
                // Find injector DLL.
                string? DirectoryPath = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
                string InjectorPath = System.IO.Path.Combine(DirectoryPath!, "Injector.dll");
                string InjectorConfigPath = System.IO.Path.Combine(DirectoryPath!, "Injector.config");
                while (!File.Exists(InjectorPath))
                {
                    DirectoryPath = System.IO.Path.GetDirectoryName(DirectoryPath);
                    if (DirectoryPath == null)
                    {
                        MessageBox.Show(resources.GetString("error_injector") + Marshal.GetLastWin32Error(), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    InjectorPath = System.IO.Path.Combine(DirectoryPath!, "Injector.dll");
                    InjectorConfigPath = System.IO.Path.Combine(DirectoryPath!, "Injector.config");
                }

                byte[] InjectorPathBuffer = System.Text.Encoding.Unicode.GetBytes(InjectorPath + "\0");

                // Write the config file which the injector will read everything from.
                InjectionConfig injectConfig = new InjectionConfig();
                injectConfig.ServerName = Config.Name;
                injectConfig.ServerPublicKey = Config.PublicKey;
                injectConfig.ServerHostname = ConnectionHostname;
                injectConfig.ServerPort = Config.Port;
                injectConfig.ServerGameType = Config.GameType;
                injectConfig.EnableSeperateSaveFiles = ProgramSettings.Default.use_seperate_saves;

                string json = injectConfig.ToJson();
                File.WriteAllText(InjectorConfigPath, json);

                // Inject the DLL into the process.
                IntPtr ModulePtr = WinAPI.GetModuleHandle("kernel32.dll");
                if (ModulePtr == IntPtr.Zero)
                {
                    MessageBox.Show("Failed to get kernel32.dll module handle: GetLastError=" + Marshal.GetLastWin32Error(), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                IntPtr LoadLibraryPtr = WinAPI.GetProcAddress(ModulePtr, "LoadLibraryW");
                if (LoadLibraryPtr == IntPtr.Zero)
                {
                    MessageBox.Show("Failed to get LoadLibraryA procedure address: GetLastError=" + Marshal.GetLastWin32Error(), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                IntPtr PathAddress = IntPtr.Zero;
                for (int i = 0; i < 32 && PathAddress == IntPtr.Zero; i++)
                {
                    PathAddress = WinAPI.VirtualAllocEx(ProcessInfo.hProcess, IntPtr.Zero, (uint)InjectorPathBuffer.Length, (uint)(AllocationType.Reserve | AllocationType.Commit), (uint)MemoryProtection.ReadWrite);
                    if (PathAddress == IntPtr.Zero)
                    {
                        Thread.Sleep(500);
                    }
                }
                if (PathAddress == IntPtr.Zero)
                {
                    MessageBox.Show("Failed to allocation memory in process: GetLastError=" + Marshal.GetLastWin32Error(), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);                    
                    return;
                }
                
                int BytesWritten;
                bool WriteSuccessful = WinAPI.WriteProcessMemory(ProcessInfo.hProcess, PathAddress, InjectorPathBuffer, (uint)InjectorPathBuffer.Length, out BytesWritten);
                if (!WriteSuccessful || BytesWritten != InjectorPathBuffer.Length)
                {
                    MessageBox.Show("Failed to write full patch to memory: GetLastError=" + Marshal.GetLastWin32Error(), resources.GetString("Warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                IntPtr ThreadHandle = WinAPI.CreateRemoteThread(ProcessInfo.hProcess, IntPtr.Zero, 0, LoadLibraryPtr, PathAddress, 0, IntPtr.Zero);
                if (ThreadHandle == IntPtr.Zero)
                {
                    MessageBox.Show("Failed to spawn remote thread: GetLastError=" + Marshal.GetLastWin32Error(), resources.GetString("Warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // Otherwise patch the server key into the process memory.
            else
            {            
                byte[] DataBlock = PatchingUtils.MakeEncryptedServerInfo(ConnectionHostname, Config.PublicKey, LoadConfig.Key);
                if (DataBlock == null)
                {
                    MessageBox.Show(resources.GetString("Failed to encode server info patch. Potentially server information is too long to fit into the space available."), resources.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Retry a few times until steamstub has unpacked everything.
                // Ugly as fuck, but simplest way to handle this.
                for (int i = 0; i < 32; i++)
                {
                    IntPtr BaseAddress = WinAPI.GetProcessModuleBaseAddress(ProcessInfo.hProcess);
                    IntPtr PatchAddress = (IntPtr)LoadConfig.ServerInfoAddress;
                    if (LoadConfig.UsesASLR)
                    {
                        PatchAddress = (IntPtr)((ulong)BaseAddress + (ulong)PatchAddress);
                    }

                    int BytesWritten;
                    bool WriteSuccessful = WinAPI.WriteProcessMemory(ProcessInfo.hProcess, PatchAddress, DataBlock, (uint)DataBlock.Length, out BytesWritten);
                    if (!WriteSuccessful || BytesWritten != DataBlock.Length)
                    {
                        if (i == 31)
                        {
                            MessageBox.Show(resources.GetString("Failed to write full patch to memory. Game may or may not work."), resources.GetString("Warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        else
                        {
                            Thread.Sleep(500);
                        }
                    }            
                    else
                    {
                        break;
                    }
                }
            }

            RunningProcessHandle = ProcessInfo.hProcess;
            RunningProcessId = ProcessInfo.dwProcessId;
            ContinualUpdateTimer.Enabled = ShouldRunContinualUpdate();

            ValidateUI();
        }

        private bool ShouldRunContinualUpdate()
        {
            /*
            if (RunningProcessHandle != IntPtr.Zero)
            {
                return true;
            }

            if (!SteamUtils.IsSteamRunningAndLoggedIn())
            {
                return true;
            }

            return false;
            */
            return true;
        }

        // Kills the named mutex that dark souls 3 uses to only open a single instance.
        private void KillNamedMutexIfExists()
        {
            Process ExistingProcess = Process.GetProcessById((int)RunningProcessId);
            if (ExistingProcess != null)
            {
                WinAPIProcesses.KillMutex(ExistingProcess, "\\BaseNamedObjects\\DarkSoulsIIIMutex");
                WinAPIProcesses.KillMutex(ExistingProcess, "\\BaseNamedObjects\\DarkSoulsIIMutex");
            }
        }

        private void OnContinualUpdateTimer(object sender, EventArgs e)
        {
            uint ExitCode = 0;
            if (RunningProcessHandle != IntPtr.Zero)
            {
                // Check if process has finished.
                if (!WinAPI.GetExitCodeProcess(RunningProcessHandle, out ExitCode) || ExitCode != (uint)ProcessExitCodes.STILL_ACTIVE)
                {
                    RunningProcessHandle = IntPtr.Zero;
                }
            }

            ValidateUI();

            //ContinualUpdateTimer.Enabled = ShouldRunContinualUpdate();
        }

        private async void OnServerRefreshTimer(object sender, EventArgs e)
        {
            await QueryServersAsync(_queryServerCts?.Token ?? CancellationToken.None);
        }

        private async void OnRefreshClicked(object sender, EventArgs e)
        {
            await QueryServersAsync(_queryServerCts?.Token ?? CancellationToken.None);
        }

        private void OnClickGithubLink(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/jakeroxs/ds3os",
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

        private void OnClickDiscordLink(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://discord.gg/pBmquc9Jkj",
                UseShellExecute = true
            });
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
            
            Sorter.SortOrder = (Sorter.SortOrder + 1) % 3;

            if (Sorter.SortOrder == 0)
            {
                ImportedServerListView.Columns[Sorter.SortColumn].Text = ColumnNames[Sorter.SortColumn];
            }
            else if (Sorter.SortOrder == 1)
            {
                ImportedServerListView.Columns[Sorter.SortColumn].Text = "↑ " + ColumnNames[Sorter.SortColumn];
            }
            else
            {
                ImportedServerListView.Columns[Sorter.SortColumn].Text = "↓ " + ColumnNames[Sorter.SortColumn];
            }
            ImportedServerListView.Sort();
        }

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
