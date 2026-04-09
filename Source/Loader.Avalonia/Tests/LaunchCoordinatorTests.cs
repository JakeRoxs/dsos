using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Loader.Tests
{
  [TestClass]
  public class LaunchCoordinatorTests
  {
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

      string exePath = @"C:\Games\DarkSoulsIII.exe";
      bool prepared = coordinator.TryPrepareLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(prepared);
      Assert.AreEqual("Selected executable does not exist.", message);
    }

    [TestMethod]
    public void TryPrepareLaunch_ReturnsFalse_WhenExecutableDoesNotMatchSelectedGame()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      LaunchCoordinator coordinator = new LaunchCoordinator(platform);

      string exePath = @"C:\Games\DarkSoulsII.exe";
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

      string exePath = @"C:\Games\DarkSoulsIII.exe";
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

      string exePath = @"C:\Games\DarkSoulsIII.exe";
      platform.ExistingFiles.Add(exePath);

      bool prepared = coordinator.TryPrepareLaunch(server, exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsTrue(prepared);
      StringAssert.Contains(message, "Server: Test Server");
      StringAssert.Contains(message, "Resolved Host: 127.0.0.1:4242");
      StringAssert.Contains(message, "Version: Dark Souls III - Test Build");
      StringAssert.Contains(message, @"Steam AppId File: C:\Games\steam_appid.txt");
      StringAssert.Contains(message, "Use Injector: True");
      StringAssert.Contains(message, "Use Separate Saves: True");
    }

    [TestMethod]
    public void TryPrepareLaunch_UsesPrivateHostnameWhenPublicMatchesButPrivateNetworkDoesNot()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      ServerConfig server = CreateServer();

      string exePath = @"C:\Games\DarkSoulsIII.exe";
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
      platform.CanLaunchOnCurrentPlatform = false;

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      string exePath = @"C:\Games\DarkSoulsIII.exe";
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(launched);
      StringAssert.Contains(message, "Automatic process launch is currently implemented only on Windows in this iteration.");
      StringAssert.Contains(message, "Generated launch preparation details:");
      StringAssert.Contains(message, "Server: Test Server");
    }

    [TestMethod]
    public void TryExecuteLaunch_ReturnsFalse_WhenSteamAppIdWriteFails()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      platform.WriteAllTextException = new IOException("disk full");

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      string exePath = @"C:\Games\DarkSoulsIII.exe";
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(launched);
      StringAssert.StartsWith(message, "Failed to write steam_appid.txt:");
      StringAssert.Contains(message, "disk full");
    }

    [TestMethod]
    public void TryExecuteLaunch_ReturnsFalse_WhenProcessStartReturnsNull()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      platform.StartedProcessId = null;

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      string exePath = @"C:\Games\DarkSoulsIII.exe";
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(CreateServer(), exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsFalse(launched);
      Assert.AreEqual("Failed to start game process: Process.Start returned null.", message);
      Assert.AreEqual(@"C:\Games\steam_appid.txt", platform.LastWritePath);
      Assert.AreEqual("374320", platform.LastWriteContents);
    }

    [TestMethod]
    public void TryExecuteLaunch_ReturnsTrue_WhenProcessStarts()
    {
      FakeLaunchPlatformServices platform = CreateValidPlatformServices();
      platform.StartedProcessId = 4242;

      LaunchCoordinator coordinator = new LaunchCoordinator(platform);
      ServerConfig server = CreateServer();
      string exePath = @"C:\Games\DarkSoulsIII.exe";
      platform.ExistingFiles.Add(exePath);

      bool launched = coordinator.TryExecuteLaunch(server, exePath, GameType.DarkSouls3, true, out string message);

      Assert.IsTrue(launched);
      Assert.AreEqual(exePath, platform.LastStartFileName);
      Assert.AreEqual(@"C:\Games", platform.LastStartWorkingDirectory);
      Assert.AreEqual(@"C:\Games\steam_appid.txt", platform.LastWritePath);
      Assert.AreEqual("374320", platform.LastWriteContents);
      Assert.AreEqual(1, platform.GetExeSimpleHashCallCount, "Launch execution should reuse a single prepared hash lookup.");
      Assert.AreEqual(1, platform.TryGetLoadConfigurationCallCount, "Launch execution should reuse a single prepared load-config lookup.");
      StringAssert.Contains(message, "Launch started.");
      StringAssert.Contains(message, "Process Id: 4242");
      StringAssert.Contains(message, "Resolved Host: 127.0.0.1:4242");
      StringAssert.Contains(message, "Note: Injector/memory patch handoff is not wired in this Avalonia slice yet.");
    }

    private static FakeLaunchPlatformServices CreateValidPlatformServices()
    {
      FakeLaunchPlatformServices platform = new FakeLaunchPlatformServices
      {
        CanLaunchOnCurrentPlatform = true,
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
        StartedProcessId = 7777
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
      public string ExpectedSimpleHash { get; set; } = "hash";
      public bool HasLoadConfiguration { get; set; }
      public DarkSoulsLoadConfig LoadConfiguration { get; set; }
      public string MachinePrivateIp { get; set; } = string.Empty;
      public string MachinePublicIp { get; set; } = string.Empty;
      public int? StartedProcessId { get; set; }
      public Exception? WriteAllTextException { get; set; }

      public string? LastWritePath { get; private set; }
      public string? LastWriteContents { get; private set; }
      public string? LastStartFileName { get; private set; }
      public string? LastStartWorkingDirectory { get; private set; }
      public int GetExeSimpleHashCallCount { get; private set; }
      public int TryGetLoadConfigurationCallCount { get; private set; }

      public HashSet<string> ExistingFiles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      public Dictionary<string, string> ResolvedHostIps { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      public bool FileExists(string path)
      {
        return ExistingFiles.Contains(path);
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

      public void WriteAllText(string path, string contents)
      {
        if (WriteAllTextException != null)
        {
          throw WriteAllTextException;
        }

        LastWritePath = path;
        LastWriteContents = contents;
      }

      public int? StartProcess(string fileName, string workingDirectory)
      {
        LastStartFileName = fileName;
        LastStartWorkingDirectory = workingDirectory;
        return StartedProcessId;
      }
    }
  }
}