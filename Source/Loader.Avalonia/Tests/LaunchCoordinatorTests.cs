using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Loader.Tests
{
  [TestClass]
  public class LaunchCoordinatorTests
  {
    private const string WindowsGameDirectory = @"C:\Games";
    private const string DarkSouls3ExePath = WindowsGameDirectory + @"\DarkSoulsIII.exe";
    private const string DarkSouls2ExePath = WindowsGameDirectory + @"\DarkSoulsII.exe";
    private const string WindowsSteamAppIdPath = WindowsGameDirectory + @"\steam_appid.txt";
    private const string SteamAppIdFileName = "steam_appid.txt";
    private const string LinuxProtonInjectorScriptPath = "/home/user/proton-injector/scripts/inject.sh";
    private const string LinuxInjectorDllPath = "/opt/loader/Injector.dll";
    private const string LinuxExePath = "/home/user/.local/share/Steam/steamapps/common/DarkSoulsIII/DarkSoulsIII.exe";

    [TestMethod]
    public void TryPrepareLaunch_ReturnsFalse_WhenExecutablePathMissing()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      LaunchCoordinator coordinator = new LaunchCoordinator(platform);

      bool prepared = coordinator.TryPrepareLaunch(CreateServer(), string.Empty, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(prepared);
      Assert.AreEqual("Select a game executable first.", message);
    }

    [TestMethod]
    public void TryPrepareLaunch_ReturnsFalse_WhenExecutableNotFound()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      LaunchCoordinator coordinator = new LaunchCoordinator(platform);

      string exePath = DarkSouls3ExePath;
      bool prepared = coordinator.TryPrepareLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(prepared);
      Assert.AreEqual("Selected executable does not exist.", message);
    }

    [TestMethod]
    public void TryPrepareLaunch_ReturnsFalse_WhenExecutableDoesNotMatchSelectedGame()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      LaunchCoordinator coordinator = new LaunchCoordinator(platform);

      string exePath = DarkSouls2ExePath;
      platform.ExistingFiles.Add(exePath);

      bool prepared = coordinator.TryPrepareLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(prepared);
      Assert.AreEqual("Selected executable does not match the selected game (DarkSoulsIII.exe).", message);
    }

    [TestMethod]
    public void TryPrepareLaunch_ReturnsFalse_WhenLoadConfigurationMissing()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      LaunchCoordinator coordinator = new LaunchCoordinator(platform);

      string exePath = DarkSouls3ExePath;
      platform.ExistingFiles.Add(exePath);
      platform.HasLoadConfiguration = false;

      bool prepared = coordinator.TryPrepareLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(prepared);
      Assert.AreEqual("Could not determine game executable version support.", message);
    }

    [TestMethod]
    public void TryPrepareLaunch_UsesLoopbackWhenServerResolvesToLocalMachine()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      ServerConfig server = CreateServer();

      string exePath = DarkSouls3ExePath;
      platform.ExistingFiles.Add(exePath);

      bool prepared = coordinator.TryPrepareLaunch(server, exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsTrue(prepared);
      StringAssert.Contains(message, "Server: Test Server");
      StringAssert.Contains(message, "Resolved Host: 127.0.0.1:4242");
      StringAssert.Contains(message, "Version: Dark Souls III - Test Build");
      StringAssert.Contains(message, $"Steam AppId File: {WindowsSteamAppIdPath}");
      StringAssert.Contains(message, "Use Injector: True");
      StringAssert.Contains(message, "Use Separate Saves: True");
    }

    [TestMethod]
    public void TryPrepareLaunch_UsesPrivateHostnameWhenPublicMatchesButPrivateNetworkDoesNot()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      ServerConfig server = CreateServer();

      string exePath = DarkSouls3ExePath;
      platform.ExistingFiles.Add(exePath);
      platform.ResolvedHostIps[server.PrivateHostname] = "10.1.2.3";

      bool prepared = coordinator.TryPrepareLaunch(server, exePath, GameType.DarkSouls3, false, out string message);

      Assert.IsTrue(prepared);
      StringAssert.Contains(message, $"Resolved Host: {server.PrivateHostname}:{server.Port}");
      StringAssert.Contains(message, "Use Separate Saves: False");
    }

    [TestMethod]
    public void TryExecuteLaunch_ReturnsFalse_WhenPlatformCannotAutoLaunch()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      platform.IsLinux = false;
      platform.IsWindows = true;
      platform.CanLaunchOnCurrentPlatform = false;

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      string exePath = DarkSouls3ExePath;
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(launched);
      StringAssert.Contains(message, "Automatic process launch is currently implemented only on Windows and Linux in this iteration.");
      StringAssert.Contains(message, "Generated launch preparation details:");
      StringAssert.Contains(message, "Server: Test Server");
    }

    [TestMethod]
    public void TryExecuteLaunch_ReturnsFalse_WhenSteamAppIdWriteFails()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      platform.WriteAllTextException = new IOException("disk full");

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      string exePath = DarkSouls3ExePath;
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(launched);
      StringAssert.StartsWith(message, $"Failed to write {SteamAppIdFileName}:");
      StringAssert.Contains(message, "disk full");
    }

    [TestMethod]
    public void TryExecuteLaunch_ReturnsFalse_WhenProcessStartReturnsNull()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      platform.StartedProcessId = null;

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      string exePath = DarkSouls3ExePath;
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(launched);
      Assert.AreEqual("Failed to start game process: Process.Start returned null.", message);
      Assert.AreEqual(WindowsSteamAppIdPath, platform.LastWritePath);
      Assert.AreEqual("374320", platform.LastWriteContents);
    }

    [TestMethod]
    public void TryExecuteLaunch_ReturnsTrue_WhenProcessStarts()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      platform.StartedProcessId = 4242;

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      ServerConfig server = CreateServer();
      string exePath = DarkSouls3ExePath;
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(server, exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsTrue(launched);
      Assert.AreEqual(exePath, platform.LastStartFileName);
      Assert.AreEqual(WindowsGameDirectory, platform.LastStartWorkingDirectory);
      Assert.AreEqual(WindowsSteamAppIdPath, platform.LastWritePath);
      Assert.AreEqual("374320", platform.LastWriteContents);
      Assert.AreEqual(1, platform.GetExeSimpleHashCallCount, "Launch execution should reuse a single prepared hash lookup.");
      Assert.AreEqual(1, platform.TryGetLoadConfigurationCallCount, "Launch execution should reuse a single prepared load-config lookup.");
      StringAssert.Contains(message, "Launch started.");
      StringAssert.Contains(message, "Process Id: 4242");
      StringAssert.Contains(message, "Resolved Host: 127.0.0.1:4242");
      StringAssert.Contains(message, "Note: Injector/memory patch handoff is not wired in this Avalonia slice yet.");
    }

    [TestMethod]
    public void TryExecuteLinuxLaunch_FindsProtonInjectorScriptAndInjectorDll()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      platform.IsLinux = true;
      platform.IsWindows = false;
      platform.CanLaunchOnCurrentPlatform = true;
      platform.StartedProcessId = 7777;
      platform.ExistingFiles.Add(LinuxProtonInjectorScriptPath);
      platform.ExistingFiles.Add(LinuxInjectorDllPath);
      platform.CurrentProcessDirectory = "/opt/loader";
      platform.EnvironmentVariables["REKINDLED_PROTON_INJECTOR_SCRIPT"] = LinuxProtonInjectorScriptPath;

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      ServerConfig server = CreateServer();
      string exePath = LinuxExePath;
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(server, exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsTrue(launched, message);
      Assert.AreEqual("/bin/bash", platform.LastStartFileName);
      Assert.AreEqual("/home/user/proton-injector/scripts", platform.LastStartWorkingDirectory);
      Assert.AreEqual(3, platform.LastStartArguments.Count);
      Assert.AreEqual(LinuxProtonInjectorScriptPath, platform.LastStartArguments[0]);
      StringAssert.Contains(message, "Proton injector script: /home/user/proton-injector/scripts/inject.sh");
      StringAssert.Contains(message, "Injector DLL: /opt/loader/Injector.dll");
      StringAssert.Contains(message, "Injector config: /opt/loader/Injector.config");
      Assert.AreEqual("/opt/loader/Injector.config", platform.LastWritePath);
    }

    private static FakeLaunchPlatformServices CreateValidPlatformServices()
    {
      FakeLaunchPlatformServices platform = new FakeLaunchPlatformServices
      {
        CanLaunchOnCurrentPlatform = true,
        IsWindows = true,
        IsLinux = false,
        ExpectedSimpleHash = "hash-ds3",
        MachinePrivateIp = "192.168.1.10",
        MachinePublicIp = "203.0.113.10",
        HasLoadConfiguration = true,
        LoadConfiguration = new DarkSoulsLoadConfig
        {
          VersionName = "Dark Souls III - Test Build",
          ServerInfoAddress = 0,
          UsesASLR = true,
          UseInjector = true,
          Key = Array.Empty<uint>(),
          SteamAppId = 374320
        },
        StartedProcessId = 7777,
        CurrentProcessDirectory = @"C:\Games"
      };

      platform.ResolvedHostIps["public.example.com"] = "203.0.113.10";
      platform.ResolvedHostIps["private.example.com"] = "192.168.1.10";

      return platform;
    }

    private static ServerConfig CreateServer()
    {
      return new ServerConfig
      {
        Id = "server-1",
        Name = "Test Server",
        Hostname = "public.example.com",
        PrivateHostname = "private.example.com",
        Port = 4242,
        GameType = "DarkSouls3"
      };
    }

    private sealed class FakeLaunchPlatformServices : LaunchCoordinator.ILaunchPlatformServices
    {
      public bool CanLaunchOnCurrentPlatform { get; set; }
      public bool IsWindows { get; set; }
      public bool IsLinux { get; set; }
      public string ExpectedSimpleHash { get; set; } = "hash";
      public bool HasLoadConfiguration { get; set; }
      public DarkSoulsLoadConfig LoadConfiguration { get; set; }
      public string MachinePrivateIp { get; set; } = string.Empty;
      public string MachinePublicIp { get; set; } = string.Empty;
      public int? StartedProcessId { get; set; }
      public Exception? WriteAllTextException { get; set; }
      public string CurrentProcessDirectory { get; set; } = string.Empty;

      public string? LastWritePath { get; private set; }
      public string? LastWriteContents { get; private set; }
      public string? LastStartFileName { get; private set; }
      public string? LastStartWorkingDirectory { get; private set; }
      public List<string> LastStartArguments { get; private set; } = new();
      public int GetExeSimpleHashCallCount { get; private set; }
      public int TryGetLoadConfigurationCallCount { get; private set; }

      public HashSet<string> ExistingFiles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      public Dictionary<string, string> ResolvedHostIps { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      public Dictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      public bool FileExists(string path)
      {
        return ExistingFiles.Contains(path);
      }

      public string GetFileName(string path)
      {
        return GetWindowsFileName(path);
      }

      public string? GetDirectoryName(string path)
      {
        return GetWindowsDirectoryName(path);
      }

      private static string GetWindowsFileName(string path)
      {
        if (string.IsNullOrEmpty(path))
        {
          return string.Empty;
        }

        int lastSlash = path.LastIndexOfAny(new[] { '\\', '/' });
        return lastSlash < 0 ? path : path.Substring(lastSlash + 1);
      }

      private static string? GetWindowsDirectoryName(string path)
      {
        if (string.IsNullOrEmpty(path))
        {
          return null;
        }

        int lastSlash = path.LastIndexOfAny(new[] { '\\', '/' });
        return lastSlash < 0 ? null : path.Substring(0, lastSlash);
      }

      public string GetExeSimpleHash(string exePath)
      {
        GetExeSimpleHashCallCount++;
        return ExpectedSimpleHash;
      }

      public bool TryGetLoadConfiguration(string simpleHash, out DarkSoulsLoadConfig loadConfig)
      {
        TryGetLoadConfigurationCallCount++;

        if (HasLoadConfiguration && string.Equals(simpleHash, ExpectedSimpleHash, StringComparison.Ordinal))
        {
          loadConfig = LoadConfiguration;
          return true;
        }

        loadConfig = default;
        return false;
      }

      public string GetMachineIPv4(bool getPublicAddress)
      {
        return getPublicAddress ? MachinePublicIp : MachinePrivateIp;
      }

      public string HostnameToIPv4(string hostname)
      {
        return ResolvedHostIps.TryGetValue(hostname, out string? value) ? value : string.Empty;
      }

      public string? GetEnvironmentVariable(string name)
      {
        return EnvironmentVariables.TryGetValue(name, out string? value) ? value : null;
      }

      public string GetCurrentProcessDirectory()
      {
        return CurrentProcessDirectory;
      }

      public void WriteAllText(string path, string contents)
      {
        if (WriteAllTextException != null)
        {
          throw WriteAllTextException;
        }

        LastWritePath = path;
        LastWriteContents = contents;
      }

      public int? StartProcess(string fileName, string workingDirectory, IReadOnlyList<string>? arguments = null, IDictionary<string, string>? environmentVariables = null)
      {
        LastStartFileName = fileName;
        LastStartWorkingDirectory = workingDirectory;
        LastStartArguments = arguments != null ? new List<string>(arguments) : new List<string>();
        return StartedProcessId;
      }
    }
  }
}
