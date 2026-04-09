#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Loader.Services
{
  public class Launcher
  {
    public IntPtr RunningProcessHandle { get; private set; } = IntPtr.Zero;
    public uint RunningProcessId { get; private set; } = 0;

    public bool ShouldRunContinualUpdate()
    {
      if (IsProcessRunning())
      {
        return true;
      }

      if (!SteamUtils.IsSteamRunningAndLoggedIn())
      {
        return true;
      }

      return false;
    }

    public string ResolveConnectIp(ServerConfig config, string machinePublicIp, string machinePrivateIp)
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

    public bool PerformLaunch(ServerConfig config, string exeLocation, string machinePublicIp, string machinePrivateIp, bool useSeparateSaveFiles, out string? errorMessage)
    {
      if (string.IsNullOrEmpty(config.PublicKey))
      {
        errorMessage = "no_public_key_available";
        return false;
      }

      string connectionHostname = ResolveConnectIp(config, machinePublicIp, machinePrivateIp);

      if (!BuildConfig.ExeLoadConfiguration.TryGetValue(ExeUtils.GetExeSimpleHash(exeLocation), out var loadConfig))
      {
        errorMessage = "failed_to_determine_exe_version";
        return false;
      }

      string exeDirectory = Path.GetDirectoryName(exeLocation) ?? string.Empty;
      string appIdFile = Path.Join(exeDirectory, "steam_appid.txt");
      File.WriteAllText(appIdFile, loadConfig.SteamAppId.ToString());

      STARTUPINFO startupInfo = new STARTUPINFO();
      PROCESS_INFORMATION processInfo = new PROCESS_INFORMATION();

      bool result = WinAPI.CreateProcess(
          null,
          "\"" + exeLocation + "\"",
          IntPtr.Zero,
          IntPtr.Zero,
          false,
          ProcessCreationFlags.ZERO_FLAG,
          IntPtr.Zero,
          exeDirectory,
          ref startupInfo,
          out processInfo
      );

      if (!result)
      {
        errorMessage = "failed_to_run_ds3_exe";
        return false;
      }

      if (loadConfig.UseInjector)
      {
        if (!TryRunInjector(config, connectionHostname, useSeparateSaveFiles, processInfo, out errorMessage))
        {
          return false;
        }
      }
      else
      {
        if (!TryPatchServerInfo(config, connectionHostname, processInfo, loadConfig, out errorMessage))
        {
          return false;
        }
      }

      RunningProcessHandle = processInfo.hProcess;
      RunningProcessId = processInfo.dwProcessId;
      errorMessage = null;
      return true;
    }

    public void ClearProcess()
    {
      RunningProcessHandle = IntPtr.Zero;
      RunningProcessId = 0;
    }

    public bool IsProcessRunning()
    {
      if (RunningProcessId == 0)
      {
        return false;
      }

      try
      {
        using var process = Process.GetProcessById((int)RunningProcessId);
        return !process.HasExited;
      }
      catch (ArgumentException)
      {
        RunningProcessHandle = IntPtr.Zero;
        RunningProcessId = 0;
        return false;
      }
    }

    private bool TryRunInjector(ServerConfig config, string connectionHostname, bool useSeparateSaveFiles, PROCESS_INFORMATION processInfo, out string? errorMessage)
    {
      string? directoryPath = Path.GetDirectoryName(Application.ExecutablePath);
      string injectorPath = Path.Join(directoryPath ?? string.Empty, "Injector.dll");
      string injectorConfigPath = Path.Join(directoryPath ?? string.Empty, "Injector.config");

      while (!File.Exists(injectorPath))
      {
        directoryPath = Path.GetDirectoryName(directoryPath ?? string.Empty);
        if (string.IsNullOrEmpty(directoryPath))
        {
          errorMessage = "error_injector";
          return false;
        }

        injectorPath = Path.Join(directoryPath, "Injector.dll");
        injectorConfigPath = Path.Join(directoryPath, "Injector.config");
      }

      var injectConfig = new InjectionConfig
      {
        ServerName = config.Name,
        ServerPublicKey = config.PublicKey,
        ServerHostname = connectionHostname,
        ServerPort = config.Port,
        ServerGameType = config.GameType,
        EnableSeparateSaveFiles = useSeparateSaveFiles
      };

      File.WriteAllText(injectorConfigPath, injectConfig.ToJson());

      IntPtr modulePtr = WinAPI.GetModuleHandle("kernel32.dll");
      if (modulePtr == IntPtr.Zero)
      {
        errorMessage = "kernel32_missing";
        return false;
      }

      IntPtr loadLibraryPtr = WinAPI.GetProcAddress(modulePtr, "LoadLibraryW");
      if (loadLibraryPtr == IntPtr.Zero)
      {
        errorMessage = "loadlibrary_missing";
        return false;
      }

      IntPtr pathAddress = IntPtr.Zero;
      byte[] injectorPathBuffer = System.Text.Encoding.Unicode.GetBytes(injectorPath + "\0");
      for (int i = 0; i < 32 && pathAddress == IntPtr.Zero; i++)
      {
        pathAddress = WinAPI.VirtualAllocEx(processInfo.hProcess, IntPtr.Zero, (uint)injectorPathBuffer.Length, (uint)(AllocationType.Reserve | AllocationType.Commit), (uint)MemoryProtection.ReadWrite);
        if (pathAddress == IntPtr.Zero)
        {
          Thread.Sleep(500);
        }
      }

      if (pathAddress == IntPtr.Zero)
      {
        errorMessage = "allocation_failed";
        return false;
      }

      if (!WinAPI.WriteProcessMemory(processInfo.hProcess, pathAddress, injectorPathBuffer, (uint)injectorPathBuffer.Length, out _))
      {
        errorMessage = "write_memory_failed";
        return false;
      }

      IntPtr threadHandle = WinAPI.CreateRemoteThread(processInfo.hProcess, IntPtr.Zero, 0, loadLibraryPtr, pathAddress, 0, IntPtr.Zero);
      if (threadHandle == IntPtr.Zero)
      {
        errorMessage = "remote_thread_failed";
        return false;
      }

      errorMessage = null;
      return true;
    }

    private bool TryPatchServerInfo(ServerConfig config, string connectionHostname, PROCESS_INFORMATION processInfo, DarkSoulsLoadConfig loadConfig, out string? errorMessage)
    {
      byte[] dataBlock = PatchingUtils.MakeEncryptedServerInfo(connectionHostname, config.PublicKey, loadConfig.Key);
      if (dataBlock == null)
      {
        errorMessage = "Failed to encode server info patch. Potentially server information is too long to fit into the space available.";
        return false;
      }

      for (int i = 0; i < 32; i++)
      {
        IntPtr baseAddress = WinAPI.GetProcessModuleBaseAddress((int)processInfo.dwProcessId);
        IntPtr patchAddress = (IntPtr)loadConfig.ServerInfoAddress;
        if (loadConfig.UsesASLR)
        {
          patchAddress = (IntPtr)((ulong)baseAddress + (ulong)patchAddress);
        }

        if (WinAPI.WriteProcessMemory(processInfo.hProcess, patchAddress, dataBlock, (uint)dataBlock.Length, out int bytesWritten) && bytesWritten == dataBlock.Length)
        {
          errorMessage = null;
          return true;
        }

        if (i == 31)
        {
          errorMessage = "Failed to write full patch to memory. Game may or may not work.";
          return false;
        }

        Thread.Sleep(500);
      }

      errorMessage = "unknown_patch_failure";
      return false;
    }

  }
}
