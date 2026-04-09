using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Loader
{
  internal sealed class LaunchCoordinator
  {
    internal interface ILaunchPlatformServices
    {
      bool CanLaunchOnCurrentPlatform { get; }
      bool FileExists(string path);
      string GetFileName(string path);
      string? GetDirectoryName(string path);
      string GetExeSimpleHash(string exePath);
      bool TryGetLoadConfiguration(string simpleHash, out DarkSoulsLoadConfig loadConfig);
      string GetMachineIPv4(bool getPublicAddress);
      string HostnameToIPv4(string hostname);
      void WriteAllText(string path, string contents);
      int? StartProcess(string fileName, string workingDirectory);
    }

    private sealed class DefaultLaunchPlatformServices : ILaunchPlatformServices
    {
      public bool CanLaunchOnCurrentPlatform => OperatingSystem.IsWindows();

      public bool FileExists(string path)
      {
        return File.Exists(path);
      }

      public string GetFileName(string path)
      {
        return Path.GetFileName(path);
      }

      public string? GetDirectoryName(string path)
      {
        return Path.GetDirectoryName(path);
      }

      public string GetExeSimpleHash(string exePath)
      {
        return ExeUtils.GetExeSimpleHash(exePath);
      }

      public bool TryGetLoadConfiguration(string simpleHash, out DarkSoulsLoadConfig loadConfig)
      {
        return BuildConfig.ExeLoadConfiguration.TryGetValue(simpleHash, out loadConfig);
      }

      public string GetMachineIPv4(bool getPublicAddress)
      {
        return NetUtils.GetMachineIPv4(getPublicAddress);
      }

      public string HostnameToIPv4(string hostname)
      {
        return NetUtils.HostnameToIPv4(hostname);
      }

      public void WriteAllText(string path, string contents)
      {
        File.WriteAllText(path, contents);
      }

      public int? StartProcess(string fileName, string workingDirectory)
      {
        Process? process = Process.Start(new ProcessStartInfo
        {
          FileName = fileName,
          WorkingDirectory = workingDirectory,
          UseShellExecute = false
        });

        return process?.Id;
      }
    }

    private readonly ILaunchPlatformServices _platformServices;

    public LaunchCoordinator()
        : this(new DefaultLaunchPlatformServices())
    {
    }

    internal LaunchCoordinator(ILaunchPlatformServices platformServices)
    {
      _platformServices = platformServices ?? throw new ArgumentNullException(nameof(platformServices));
    }

    public bool CanLaunchOnCurrentPlatform => _platformServices.CanLaunchOnCurrentPlatform;

    public bool TryExecuteLaunch(
        ServerConfig server,
        string exePath,
        GameType gameType,
        bool useSeparateSaves,
        out string message)
    {
      if (!TryBuildLaunchPlan(server, exePath, gameType, useSeparateSaves, out DarkSoulsLoadConfig loadConfig, out string launchPlan))
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

      string exeDirectory = _platformServices.GetDirectoryName(exePath) ?? string.Empty;
      string appIdPath = CombinePathPreservingSeparator(exeDirectory, "steam_appid.txt");

      try
      {
        _platformServices.WriteAllText(appIdPath, loadConfig.SteamAppId.ToString());
      }
      catch (Exception ex)
      {
        message = $"Failed to write steam_appid.txt: {ex.Message}";
        return false;
      }

      int? processId;
      try
      {
        processId = _platformServices.StartProcess(exePath, exeDirectory);
      }
      catch (Exception ex)
      {
        message = $"Failed to start game process: {ex.Message}";
        return false;
      }

      if (!processId.HasValue)
      {
        message = "Failed to start game process: Process.Start returned null.";
        return false;
      }

      StringBuilder summary = new StringBuilder();
      summary.AppendLine("Launch started.");
      summary.AppendLine($"Process Id: {processId.Value}");
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
      if (!TryBuildLaunchPlan(server, exePath, gameType, useSeparateSaves, out _, out string launchPlan))
      {
        message = launchPlan;
        return false;
      }

      message = launchPlan;
      return true;
    }

    private bool TryBuildLaunchPlan(
        ServerConfig server,
        string exePath,
        GameType gameType,
        bool useSeparateSaves,
        out DarkSoulsLoadConfig loadConfig,
        out string message)
    {
      loadConfig = default;

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

      string simpleHash = _platformServices.GetExeSimpleHash(exePath);
      if (!_platformServices.TryGetLoadConfiguration(simpleHash, out loadConfig))
      {
        message = "Could not determine game executable version support.";
        return false;
      }

      string machinePrivateIp = _platformServices.GetMachineIPv4(false);
      string machinePublicIp = _platformServices.GetMachineIPv4(true);

      string resolvedHost = ResolveConnectIp(server, machinePublicIp, machinePrivateIp);
      string appIdPath = CombinePathPreservingSeparator(_platformServices.GetDirectoryName(exePath) ?? string.Empty, "steam_appid.txt");

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

    private string? ValidateExecutable(string exePath, GameType gameType)
    {
      if (string.IsNullOrWhiteSpace(exePath))
      {
        return "Select a game executable first.";
      }

      if (!_platformServices.FileExists(exePath))
      {
        return "Selected executable does not exist.";
      }

      string expectedFile = gameType == GameType.DarkSouls3 ? "DarkSoulsIII.exe" : "DarkSoulsII.exe";
      if (!string.Equals(_platformServices.GetFileName(exePath), expectedFile, StringComparison.OrdinalIgnoreCase))
      {
        return $"Selected executable does not match the selected game ({expectedFile}).";
      }

      return null;
    }

    private string ResolveConnectIp(ServerConfig config, string machinePublicIp, string machinePrivateIp)
    {
      string connectionHostname = config.Hostname;
      string hostnameIp = _platformServices.HostnameToIPv4(config.Hostname);
      string privateHostnameIp = _platformServices.HostnameToIPv4(config.PrivateHostname);

      if (hostnameIp == machinePublicIp)
      {
        connectionHostname = privateHostnameIp == machinePrivateIp ? "127.0.0.1" : config.PrivateHostname;
      }

      return connectionHostname;
    }

    private static string CombinePathPreservingSeparator(string directory, string fileName)
    {
      if (string.IsNullOrEmpty(directory))
      {
        return fileName;
      }

      if (directory.Contains('\\'))
      {
        return directory.EndsWith("\\") ? directory + fileName : directory + "\\" + fileName;
      }

      return Path.Combine(directory, fileName);
    }
  }
}
