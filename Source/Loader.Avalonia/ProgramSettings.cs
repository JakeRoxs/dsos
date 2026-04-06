using System.Text.Json;

namespace Loader
{
  internal sealed class ProgramSettings
  {
    private sealed class SettingsDto
    {
      public string server_config_json { get; set; } = string.Empty;
      public string hub_server_url { get; set; } = "https://rekindled.jakesws.xyz";
      public bool hide_passworded { get; set; } = true;
      public int minimum_players { get; set; } = 1;
      public bool use_separate_saves { get; set; } = true;
      public string ds2_exe_location { get; set; } = string.Empty;
      public string ds3_exe_location { get; set; } = string.Empty;
    }

    public static ProgramSettings Default { get; } = new();

    public string server_config_json { get; set; } = string.Empty;
    public string hub_server_url { get; set; } = "https://rekindled.jakesws.xyz";
    public bool hide_passworded { get; set; } = true;
    public int minimum_players { get; set; } = 1;
    public bool use_separate_saves { get; set; } = true;
    public string ds2_exe_location { get; set; } = string.Empty;
    public string ds3_exe_location { get; set; } = string.Empty;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RekindledServer",
        "loader-avalonia-settings.json");

    private ProgramSettings()
    {
      LoadFromDisk();
    }

    public void Save()
    {
      string? directory = Path.GetDirectoryName(SettingsPath);
      if (!string.IsNullOrWhiteSpace(directory))
      {
        Directory.CreateDirectory(directory);
      }

      SettingsDto dto = new SettingsDto
      {
        server_config_json = server_config_json,
        hub_server_url = hub_server_url,
        hide_passworded = hide_passworded,
        minimum_players = minimum_players,
        use_separate_saves = use_separate_saves,
        ds2_exe_location = ds2_exe_location,
        ds3_exe_location = ds3_exe_location
      };

      File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LoadFromDisk()
    {
      if (!File.Exists(SettingsPath))
      {
        return;
      }

      try
      {
        SettingsDto? loaded = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(SettingsPath));
        if (loaded == null)
        {
          return;
        }

        server_config_json = loaded.server_config_json;
        hub_server_url = string.IsNullOrWhiteSpace(loaded.hub_server_url) ? hub_server_url : loaded.hub_server_url;
        hide_passworded = loaded.hide_passworded;
        minimum_players = loaded.minimum_players;
        use_separate_saves = loaded.use_separate_saves;
        ds2_exe_location = loaded.ds2_exe_location;
        ds3_exe_location = loaded.ds3_exe_location;
      }
      catch
      {
        // Keep defaults if settings are unreadable.
      }
    }
  }
}
