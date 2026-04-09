/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license. 
 * You should have received a copy of the license along with this program. 
 * If not, see <https://opensource.org/licenses/MIT>.
 */

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Loader
{
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct STARTUPINFO
  {
    public uint cb;
    public string lpReserved;
    public string lpDesktop;
    public string lpTitle;
    public uint dwX;
    public uint dwY;
    public uint dwXSize;
    public uint dwYSize;
    public uint dwXCountChars;
    public uint dwYCountChars;
    public uint dwFillAttribute;
    public uint dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct PROCESS_INFORMATION
  {
    public IntPtr hProcess;
    public IntPtr hThread;
    public uint dwProcessId;
    public uint dwThreadId;
  }

  [Flags]
  public enum ProcessCreationFlags : uint
  {
    ZERO_FLAG = 0x00000000,
    CREATE_BREAKAWAY_FROM_JOB = 0x01000000,
    CREATE_DEFAULT_ERROR_MODE = 0x04000000,
    CREATE_NEW_CONSOLE = 0x00000010,
    CREATE_NEW_PROCESS_GROUP = 0x00000200,
    CREATE_NO_WINDOW = 0x08000000,
    CREATE_PROTECTED_PROCESS = 0x00040000,
    CREATE_PRESERVE_CODE_AUTHZ_LEVEL = 0x02000000,
    CREATE_SEPARATE_WOW_VDM = 0x00001000,
    CREATE_SHARED_WOW_VDM = 0x00001000,
    CREATE_SUSPENDED = 0x00000004,
    CREATE_UNICODE_ENVIRONMENT = 0x00000400,
    DEBUG_ONLY_THIS_PROCESS = 0x00000002,
    DEBUG_PROCESS = 0x00000001,
    DETACHED_PROCESS = 0x00000008,
    EXTENDED_STARTUPINFO_PRESENT = 0x00080000,
    INHERIT_PARENT_AFFINITY = 0x00010000
  }

  [Flags]
  public enum AllocationType
  {
    Commit = 0x1000,
    Reserve = 0x2000,
    Decommit = 0x4000,
    Release = 0x8000,
    Reset = 0x80000,
    Physical = 0x400000,
    TopDown = 0x100000,
    WriteWatch = 0x200000,
    LargePages = 0x20000000
  }

  [Flags]
  public enum MemoryProtection
  {
    Execute = 0x10,
    ExecuteRead = 0x20,
    ExecuteReadWrite = 0x40,
    ExecuteWriteCopy = 0x80,
    NoAccess = 0x01,
    ReadOnly = 0x02,
    ReadWrite = 0x04,
    WriteCopy = 0x08,
    GuardModifierflag = 0x100,
    NoCacheModifierflag = 0x200,
    WriteCombineModifierflag = 0x400
  }
  public static class WinAPI
  {
    [DllImport("kernel32.dll")]
    public static extern bool CreateProcess(string lpApplicationName,
            string lpCommandLine, IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles, ProcessCreationFlags dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleA", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll")]
    public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

    public static IntPtr GetProcessModuleBaseAddress(int processId)
    {
      try
      {
        using var process = Process.GetProcessById(processId);
        if (process.MainModule != null)
        {
          return process.MainModule.BaseAddress;
        }

        if (process.Modules.Count > 0)
        {
          return process.Modules[0].BaseAddress;
        }

        Debug.WriteLine($"GetProcessModuleBaseAddress: Process {processId} returned null MainModule and no managed modules.");
      }
      catch (ArgumentException ex)
      {
        Debug.WriteLine($"GetProcessModuleBaseAddress: Invalid process id {processId}. Exception: {ex}");
      }
      catch (InvalidOperationException ex)
      {
        Debug.WriteLine($"GetProcessModuleBaseAddress: Process exited before module information could be read. Exception: {ex}");
      }
      catch (System.ComponentModel.Win32Exception ex)
      {
        Debug.WriteLine($"GetProcessModuleBaseAddress: Win32Exception when reading MainModule for process id {processId}. Exception: {ex}");
      }

      // If the managed API cannot resolve the main module, give up here.
      // This avoids additional unmanaged module enumeration when the process
      // cannot be reliably inspected from this runtime.
      return IntPtr.Zero;
    }
  }
}


