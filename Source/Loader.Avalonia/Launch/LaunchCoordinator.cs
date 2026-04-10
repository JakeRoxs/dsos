using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Loader
{
  internal sealed class LaunchCoordinator
  {
    internal interface ILaunchPlatformServices
    {
      bool CanLaunchOnCurrentPlatform { get; }
      bool IsWindows { get; }
      bool IsLinux { get; }
      bool FileExists(string path);
      string GetFileName(string path);
      string? GetDirectoryName(string path);
      string GetExeSimpleHash(string exePath);
      bool TryGetLoadConfiguration(string simpleHash, out DarkSoulsLoadConfig loadConfig);
      string GetMachineIPv4(bool getPublicAddress);
      string HostnameToIPv4(string hostname);
      string? GetEnvironmentVariable(string name);
      string GetCurrentProcessDirectory();
      void WriteAllText(string path, string contents);
      int? StartProcess(string fileName, string workingDirectory, IReadOnlyList<string>? arguments = null, IDictionary<string, string>? environmentVariables = null);
    }

    private sealed class DefaultLaunchPlatformServices : ILaunchPlatformServices
    {
      public bool CanLaunchOnCurrentPlatform => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
      public bool IsWindows => OperatingSystem.IsWindows();
      public bool IsLinux => OperatingSystem.IsLinux();

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

      public string? GetEnvironmentVariable(string name)
      {
        return Environment.GetEnvironmentVariable(name);
      }

      public string GetCurrentProcessDirectory()
      {
        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      }

      public void WriteAllText(string path, string contents)
      {
        File.WriteAllText(path, contents);
      }

      public int? StartProcess(string fileName, string workingDirectory, IReadOnlyList<string>? arguments = null, IDictionary<string, string>? environmentVariables = null)
      {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
          FileName = fileName,
          WorkingDirectory = workingDirectory,
          UseShellExecute = false
        };

        if (arguments != null)
        {
          foreach (string argument in arguments)
          {
            startInfo.ArgumentList.Add(argument);
          }
        }

        if (environmentVariables != null)
        {
          foreach (KeyValuePair<string, string> pair in environmentVariables)
          {
            startInfo.Environment[pair.Key] = pair.Value;
          }
        }

        Process? process = Process.Start(startInfo);
        return process?.Id;
      }
    }

    private readonly ILaunchPlatformServices _platformServices;
    private static readonly char[] PathTrimChars = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
    private const string SteamAppIdFileName = "steam_appid.txt";
    private const string ProtonInjectorScriptsDirectory = "scripts";
    private const string ProtonInjectorScriptFileName = "inject.sh";
    private const string InjectorDllFileName = "Injector.dll";
    private const string InjectorConfigFileName = "Injector.config";

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

      if (!_platformServices.CanLaunchOnCurrentPlatform)
      {
        message = BuildUnsupportedLaunchMessage(launchPlan);
        return false;
      }

      if (_platformServices.IsWindows)
      {
        return TryExecuteWindowsLaunch(exePath, loadConfig, launchPlan, out message);
      }

      if (_platformServices.IsLinux)
      {
        return TryExecuteLinuxLaunch(server, exePath, useSeparateSaves, loadConfig, launchPlan, out message);
      }

      message = BuildUnsupportedLaunchMessage(launchPlan);
      return false;
    }

    private static string BuildUnsupportedLaunchMessage(string launchPlan)
    {
      StringBuilder unsupportedSummary = new StringBuilder();
      unsupportedSummary.AppendLine("Automatic process launch is currently implemented only on Windows and Linux in this iteration.");
      unsupportedSummary.AppendLine("Generated launch preparation details:");
      unsupportedSummary.AppendLine();
      unsupportedSummary.AppendLine(launchPlan);

      return unsupportedSummary.ToString().TrimEnd();
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

    private bool TryExecuteWindowsLaunch(
        string exePath,
        DarkSoulsLoadConfig loadConfig,
        string launchPlan,
        out string message)
    {
      string exeDirectory = _platformServices.GetDirectoryName(exePath) ?? string.Empty;
      string appIdPath = CombinePathPreservingSeparator(exeDirectory, SteamAppIdFileName);

      try
      {
        _platformServices.WriteAllText(appIdPath, loadConfig.SteamAppId.ToString());
      }
      catch (Exception ex)
      {
        message = $"Failed to write {SteamAppIdFileName}: {ex.Message}";
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

    private bool TryExecuteLinuxLaunch(
        ServerConfig server,
        string exePath,
        bool useSeparateSaves,
        DarkSoulsLoadConfig loadConfig,
        string launchPlan,
        out string message)
    {
      if (!loadConfig.UseInjector)
      {
        message = "Linux auto-launch currently supports proton-injector based executables only.";
        return false;
      }

      string exeDirectory = _platformServices.GetDirectoryName(exePath) ?? string.Empty;
      string appIdPath = CombinePathPreservingSeparator(exeDirectory, SteamAppIdFileName);

      try
      {
        _platformServices.WriteAllText(appIdPath, loadConfig.SteamAppId.ToString());
      }
      catch (Exception ex)
      {
        message = $"Failed to write {SteamAppIdFileName}: {ex.Message}";
        return false;
      }

      if (!TryLocateInjectorDll(out string injectorDllPath, out string injectorConfigPath, out string injectorError))
      {
        message = injectorError;
        return false;
      }

      var injectConfig = new InjectionConfig
      {
        ServerName = server.Name,
        ServerPublicKey = server.PublicKey,
        ServerHostname = ResolveConnectIp(server, _platformServices.GetMachineIPv4(true), _platformServices.GetMachineIPv4(false)),
        ServerPort = server.Port,
        ServerGameType = server.GameType,
        EnableSeparateSaveFiles = useSeparateSaves
      };

      try
      {
        _platformServices.WriteAllText(injectorConfigPath, injectConfig.ToJson());
      }
      catch (Exception ex)
      {
        message = $"Failed to write Injector.config: {ex.Message}";
        return false;
      }

      if (!TryResolveProtonInjectorScript(out string scriptPath, out string scriptError))
      {
        message = scriptError;
        return false;
      }

      string scriptDirectory = _platformServices.GetDirectoryName(scriptPath) ?? string.Empty;
      IReadOnlyList<string> arguments = new[] { scriptPath, exePath, injectorDllPath };
      Dictionary<string, string> environmentVariables = new Dictionary<string, string>
      {
        ["APPID"] = loadConfig.SteamAppId.ToString()
      };

      int? processId;
      try
      {
        processId = _platformServices.StartProcess("/bin/bash", scriptDirectory, arguments, environmentVariables);
      }
      catch (Exception ex)
      {
        message = $"Failed to start proton-injector helper: {ex.Message}";
        return false;
      }

      if (!processId.HasValue)
      {
        message = "Failed to start proton-injector helper: Process.Start returned null.";
        return false;
      }

      StringBuilder summary = new StringBuilder();
      summary.AppendLine("Launch started.");
      summary.AppendLine($"Process Id: {processId.Value}");
      summary.AppendLine();
      summary.AppendLine(launchPlan);
      summary.AppendLine($"Proton injector script: {scriptPath}");
      summary.AppendLine($"Injector DLL: {injectorDllPath}");
      summary.AppendLine($"Injector config: {injectorConfigPath}");
      summary.AppendLine();
      summary.AppendLine("Note: The external proton-injector helper is responsible for launching the game and injecting Injector.dll into the Proton process.");

      message = summary.ToString().TrimEnd();
      return true;
    }

    private bool TryResolveProtonInjectorScript(out string scriptPath, out string message)
    {
      string? explicitScript = _platformServices.GetEnvironmentVariable("REKINDLED_PROTON_INJECTOR_SCRIPT");
      if (!string.IsNullOrWhiteSpace(explicitScript))
      {
        if (_platformServices.FileExists(explicitScript))
        {
          scriptPath = explicitScript;
          message = string.Empty;
          return true;
        }

        scriptPath = string.Empty;
        message = $"Configured proton injector script was not found: {explicitScript}";
        return false;
      }

      string? injectorRoot = _platformServices.GetEnvironmentVariable("REKINDLED_PROTON_INJECTOR_ROOT");
      if (!string.IsNullOrWhiteSpace(injectorRoot))
      {
        string candidate = EnsurePlatformFullPath(CombinePlatformPath(injectorRoot, ProtonInjectorScriptsDirectory, ProtonInjectorScriptFileName));
        if (_platformServices.FileExists(candidate))
        {
          scriptPath = candidate;
          message = string.Empty;
          return true;
        }

        scriptPath = string.Empty;
        message = $"proton-injector root was configured but {ProtonInjectorScriptFileName} was not found at: {candidate}";
        return false;
      }

      string currentDir = _platformServices.GetCurrentProcessDirectory();
      string[] candidates = new[]
      {
        EnsurePlatformFullPath(CombinePlatformPath(currentDir, "proton-injector", ProtonInjectorScriptsDirectory, ProtonInjectorScriptFileName)),
        EnsurePlatformFullPath(CombinePlatformPath(currentDir, "..", "proton-injector", ProtonInjectorScriptsDirectory, ProtonInjectorScriptFileName)),
        EnsurePlatformFullPath(CombinePlatformPath(currentDir, "..", "tools", "proton-injector", ProtonInjectorScriptsDirectory, ProtonInjectorScriptFileName))
      };

      string? matchingCandidate = candidates.FirstOrDefault(_platformServices.FileExists);
      if (matchingCandidate != null)
      {
        scriptPath = matchingCandidate;
        message = string.Empty;
        return true;
      }

      scriptPath = string.Empty;
      message = "Could not locate proton-injector inject.sh script. Set REKINDLED_PROTON_INJECTOR_SCRIPT or REKINDLED_PROTON_INJECTOR_ROOT, or place proton-injector/scripts/inject.sh next to the loader.";
      return false;
    }

    private bool TryLocateInjectorDll(out string injectorDllPath, out string injectorConfigPath, out string message)
    {
      string? explicitDll = _platformServices.GetEnvironmentVariable("REKINDLED_INJECTOR_DLL_PATH");
      if (!string.IsNullOrWhiteSpace(explicitDll) && _platformServices.FileExists(explicitDll))
      {
        injectorDllPath = explicitDll;
        injectorConfigPath = CombinePathPreservingSeparator(_platformServices.GetDirectoryName(explicitDll) ?? string.Empty, InjectorConfigFileName);
        message = string.Empty;
        return true;
      }

      string currentDir = _platformServices.GetCurrentProcessDirectory();
      string? searchDir = currentDir;
      while (!string.IsNullOrEmpty(searchDir))
      {
        string candidate = EnsurePlatformFullPath(CombinePlatformPath(searchDir, InjectorDllFileName));
        if (_platformServices.FileExists(candidate))
        {
          injectorDllPath = candidate;
          injectorConfigPath = CombinePathPreservingSeparator(searchDir, InjectorConfigFileName);
          message = string.Empty;
          return true;
        }

        string? parentDir = _platformServices.GetDirectoryName(searchDir);
        if (string.IsNullOrEmpty(parentDir) || parentDir == searchDir)
        {
          break;
        }

        searchDir = parentDir;
      }

      injectorDllPath = string.Empty;
      injectorConfigPath = string.Empty;
      message = "Could not locate Injector.dll. Ensure the injector package is installed alongside the loader or set REKINDLED_INJECTOR_DLL_PATH.";
      return false;
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
      string appIdPath = CombinePathPreservingSeparator(_platformServices.GetDirectoryName(exePath) ?? string.Empty, SteamAppIdFileName);

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

    private static string CombinePlatformPath(string basePath, params string[] segments)
    {
      string[] cleanedSegments = segments.Select(segment => segment.Trim(PathTrimChars)).ToArray();
      if (string.IsNullOrEmpty(basePath))
      {
        return Path.Combine(cleanedSegments);
      }

      bool preserveUnixStyle = basePath.Contains('/');
      string trimmedBasePath = basePath.TrimEnd(PathTrimChars);
      string combined = Path.Combine(new[] { trimmedBasePath }.Concat(cleanedSegments).ToArray());
      return preserveUnixStyle ? combined.Replace('\\', '/') : combined;
    }

    private static string EnsurePlatformFullPath(string path)
    {
      if (path.Length > 0 && (path[0] == '/' || path[0] == '\\'))
      {
        return path.Replace('\\', '/');
      }

      return Path.GetFullPath(path);
    }

    private static string CombinePathPreservingSeparator(string directory, string fileName)
    {
      if (string.IsNullOrEmpty(directory))
      {
        return fileName;
      }

      if (directory.Contains('/') || (directory.Length > 0 && directory[0] == '/'))
      {
        return directory.EndsWith('/') ? directory + fileName : directory.TrimEnd(PathTrimChars) + '/' + fileName;
      }

      if (directory.Contains('\\'))
      {
        return directory.EndsWith('\\') ? directory + fileName : directory.TrimEnd(PathTrimChars) + '\\' + fileName;
      }

      return Path.Combine(directory, fileName);
    }
  }
}
