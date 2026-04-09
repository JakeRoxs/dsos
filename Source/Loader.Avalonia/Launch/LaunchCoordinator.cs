using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Loader
{
  internal sealed class LaunchCoordinator
  {
    public bool CanLaunchOnCurrentPlatform => OperatingSystem.IsWindows();

    public bool TryExecuteLaunch(
        ServerConfig server,
        string exePath,
        GameType gameType,
        bool useSeparateSaves,
        out string message)
    {
      if (!TryPrepareLaunch(server, exePath, gameType, useSeparateSaves, out string launchPlan))
      {
        message = launchPlan;
        return false;
      }

      if (!CanLaunchOnCurrentPlatform)
      {
        StringBuilder unsupportedSummary = new StringBuilder();
        unsupportedSummary.AppendLine("Automatic process launch is currently implemented only on Windows in this iteration.");
        unsupportedSummary.AppendLine("Generated launch preparation details:");
        unsupportedSummary.AppendLine();
        unsupportedSummary.AppendLine(launchPlan);

        message = unsupportedSummary.ToString().TrimEnd();
        return false;
      }

      if (!BuildConfig.ExeLoadConfiguration.TryGetValue(ExeUtils.GetExeSimpleHash(exePath), out var loadConfig))
      {
        message = "Could not determine game executable version support.";
        return false;
      }

      string exeDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
      string appIdPath = Path.Combine(exeDirectory, "steam_appid.txt");

      try
      {
        File.WriteAllText(appIdPath, loadConfig.SteamAppId.ToString());
      }
      catch (Exception ex)
      {
        message = $"Failed to write steam_appid.txt: {ex.Message}";
        return false;
      }

      Process? process;
      try
      {
        process = Process.Start(new ProcessStartInfo
        {
          FileName = exePath,
          WorkingDirectory = exeDirectory,
          UseShellExecute = false
        });
      }
      catch (Exception ex)
      {
        message = $"Failed to start game process: {ex.Message}";
        return false;
      }

      if (process == null)
      {
        message = "Failed to start game process: Process.Start returned null.";
        return false;
      }

      StringBuilder summary = new StringBuilder();
      summary.AppendLine("Launch started.");
      summary.AppendLine($"Process Id: {process.Id}");
      summary.AppendLine();
      summary.AppendLine(launchPlan);
      summary.AppendLine();
      summary.AppendLine("Note: Injector/memory patch handoff is not wired in this Avalonia slice yet.");

      message = summary.ToString().TrimEnd();
      return true;
    }

    public bool TryPrepareLaunch(
        ServerConfig server,
        string exePath,
        GameType gameType,
        bool useSeparateSaves,
        out string message)
    {
      if (server == null)
      {
        message = "No server selected.";
        return false;
      }

      string? validationError = ValidateExecutable(exePath, gameType);
      if (validationError != null)
      {
        message = validationError;
        return false;
      }

      string simpleHash = ExeUtils.GetExeSimpleHash(exePath);
      if (!BuildConfig.ExeLoadConfiguration.TryGetValue(simpleHash, out var loadConfig))
      {
        message = "Could not determine game executable version support.";
        return false;
      }

      string machinePrivateIp = NetUtils.GetMachineIPv4(false);
      string machinePublicIp = NetUtils.GetMachineIPv4(true);

      string resolvedHost = ResolveConnectIp(server, machinePublicIp, machinePrivateIp);
      string appIdPath = Path.Combine(Path.GetDirectoryName(exePath) ?? string.Empty, "steam_appid.txt");

      StringBuilder summary = new StringBuilder();
      summary.AppendLine($"Server: {server.Name}");
      summary.AppendLine($"Game: {gameType}");
      summary.AppendLine($"Executable: {exePath}");
      summary.AppendLine($"Resolved Host: {resolvedHost}:{server.Port}");
      summary.AppendLine($"Version: {loadConfig.VersionName}");
      summary.AppendLine($"Steam AppId File: {appIdPath}");
      summary.AppendLine($"Use Injector: {loadConfig.UseInjector}");
      summary.AppendLine($"Use Separate Saves: {useSeparateSaves}");

      message = summary.ToString().TrimEnd();
      return true;
    }

    private static string? ValidateExecutable(string exePath, GameType gameType)
    {
      if (string.IsNullOrWhiteSpace(exePath))
      {
        return "Select a game executable first.";
      }

      if (!File.Exists(exePath))
      {
        return "Selected executable does not exist.";
      }

      string expectedFile = gameType == GameType.DarkSouls3 ? "DarkSoulsIII.exe" : "DarkSoulsII.exe";
      if (!string.Equals(Path.GetFileName(exePath), expectedFile, StringComparison.OrdinalIgnoreCase))
      {
        return $"Selected executable does not match the selected game ({expectedFile}).";
      }

      return null;
    }

    private static string ResolveConnectIp(ServerConfig config, string machinePublicIp, string machinePrivateIp)
    {
      string connectionHostname = config.Hostname;
      string hostnameIp = NetUtils.HostnameToIPv4(config.Hostname);
      string privateHostnameIp = NetUtils.HostnameToIPv4(config.PrivateHostname);

      if (hostnameIp == machinePublicIp)
      {
        connectionHostname = privateHostnameIp == machinePrivateIp ? "127.0.0.1" : config.PrivateHostname;
      }

      return connectionHostname;
    }
  }
}
