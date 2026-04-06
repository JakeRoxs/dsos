using System;
using System.IO;

namespace Loader.Services
{
  public class ConfigService
  {
    public string Ds3ExeLocation { get; set; } = string.Empty;
    public string Ds2ExeLocation { get; set; } = string.Empty;
    public bool HidePassworded { get; set; }
    public int MinimumPlayers { get; set; }
    public string ServerConfigJson { get; set; } = string.Empty;
    public bool UseSeparateSaves { get; set; }

    public ServerConfigList LoadSettings()
    {
      Ds3ExeLocation = ProgramSettings.Default.ds3_exe_location;
      Ds2ExeLocation = ProgramSettings.Default.ds2_exe_location;
      HidePassworded = ProgramSettings.Default.hide_passworded;
      MinimumPlayers = ProgramSettings.Default.minimum_players;
      ServerConfigJson = ProgramSettings.Default.server_config_json;
      UseSeparateSaves = ProgramSettings.Default.use_separate_saves;

      if (ServerConfigList.FromJson(ServerConfigJson, out var serverList))
      {
        return serverList;
      }

      return new ServerConfigList();
    }

    public void SaveSettings(ServerConfigList serverList, string exeLocation, bool hidePassworded, int minimumPlayers)
    {
      if (serverList == null)
        throw new ArgumentNullException(nameof(serverList));

      if (string.IsNullOrWhiteSpace(exeLocation))
        throw new ArgumentException("Exe location must be provided.", nameof(exeLocation));

      if (!File.Exists(exeLocation))
        throw new FileNotFoundException("Executable not found", exeLocation);

      if (exeLocation.EndsWith("DarkSoulsIII.exe", StringComparison.OrdinalIgnoreCase))
      {
        ProgramSettings.Default.ds3_exe_location = exeLocation;
        Ds3ExeLocation = exeLocation;
      }
      else if (exeLocation.EndsWith("DarkSoulsII.exe", StringComparison.OrdinalIgnoreCase))
      {
        ProgramSettings.Default.ds2_exe_location = exeLocation;
        Ds2ExeLocation = exeLocation;
      }

      var json = serverList.ToJson();
      ProgramSettings.Default.server_config_json = json;
      ServerConfigJson = json;
      ProgramSettings.Default.hide_passworded = hidePassworded;
      HidePassworded = hidePassworded;
      ProgramSettings.Default.minimum_players = minimumPlayers;
      MinimumPlayers = minimumPlayers;
      ProgramSettings.Default.use_separate_saves = UseSeparateSaves;
      ProgramSettings.Default.Save();
    }
  }
}
