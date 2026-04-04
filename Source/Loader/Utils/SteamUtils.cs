/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license. 
 * You should have received a copy of the license along with this program. 
 * If not, see <https://opensource.org/licenses/MIT>.
 */
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Win32;

namespace Loader
{
  [SupportedOSPlatform("windows")]
  public static class SteamUtils
  {
    private static bool TryGetLibraryPathFromVdfLine(string line, out string libraryPath)
    {
      libraryPath = string.Empty;

      string trimmed = line.Trim();
      if (trimmed.Length == 0 || trimmed[0] != '"')
      {
        return false;
      }

      int indexKeyStart = 0;
      int indexKeyEnd = trimmed.IndexOf('"', indexKeyStart + 1);
      if (indexKeyEnd == -1)
      {
        return false;
      }

      string key = trimmed.Substring(indexKeyStart + 1, indexKeyEnd - indexKeyStart - 1);
      if (key != "path")
      {
        return false;
      }

      int indexValueStart = trimmed.IndexOf('"', indexKeyEnd + 1);
      if (indexValueStart == -1)
      {
        return false;
      }

      int indexValueEnd = trimmed.IndexOf('"', indexValueStart + 1);
      if (indexValueEnd == -1)
      {
        return false;
      }

      libraryPath = trimmed.Substring(indexValueStart + 1, indexValueEnd - indexValueStart - 1).Replace("\\\\", "\\");
      return true;
    }

    public static string GetGameInstallPath(string FolderName)
    {
      object? rawPath = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", "");
      string SteamPath = rawPath as string ?? string.Empty;
      if (string.IsNullOrEmpty(SteamPath))
      {
        return "";
      }

      string ConfigVdfPath = SteamPath + @"\steamapps\LibraryFolders.vdf";
      if (!File.Exists(ConfigVdfPath))
      {
        return "";
      }

      // Turbo-shit parsing. Lets just pretend you didn't see any of this ...
      string[] Lines = File.ReadAllLines(ConfigVdfPath);
      foreach (string line in Lines)
      {
        if (!TryGetLibraryPathFromVdfLine(line, out string libraryPath))
        {
          continue;
        }

        string PotentialPath = libraryPath + @"\steamapps\common\" + FolderName;
        if (Directory.Exists(PotentialPath))
        {
          return PotentialPath;
        }
      }

      return "";
    }

    public static bool IsSteamRunningAndLoggedIn()
    {
      if (Environment.GetEnvironmentVariable("YES_STEAM_IS_RUNNING") == "1")
      {
        return true;
      }
      object? ActiveUserValue = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam\ActiveProcess", "ActiveUser", 0);
      object? ActivePidValue = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam\ActiveProcess", "pid", 0);
      if (ActiveUserValue is not int)
      {
        return false;
      }
      if (ActivePidValue is not int)
      {
        return false;
      }

      if (((int)ActiveUserValue) == 0)
      {
        return false;
      }

      int Pid = (int)ActivePidValue;
      if (Pid == 0)
      {
        return false;
      }

      try
      {
        Process proc = Process.GetProcessById(Pid);
        if (proc.HasExited)
        {
          return false;
        }
      }
      catch (InvalidOperationException)
      {
        return false;
      }
      catch (ArgumentException)
      {
        return false;
      }

      return true;
    }
  }
}
