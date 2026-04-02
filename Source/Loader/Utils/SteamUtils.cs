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
    public static string GetGameInstallPath(string FolderName)
    {
      object? rawPath = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", "");
      string SteamPath = rawPath as string ?? string.Empty;
      if (string.IsNullOrEmpty(SteamPath))
      {
        return "";
      }

      /*
      string PotentialPath = SteamPath + @"\steamapps\common\" + FolderName;
      if (Directory.Exists(PotentialPath))
      {
          return PotentialPath;
      }
      */

      string ConfigVdfPath = SteamPath + @"\steamapps\LibraryFolders.vdf";
      if (!File.Exists(ConfigVdfPath))
      {
        return "";
      }

      // Turbo-shit parsing. Lets just pretend you didn't see any of this ...
      string[] Lines = File.ReadAllLines(ConfigVdfPath);
      foreach (string Line in Lines)
      {
        string Trimmed = Line.Trim();
        if (!Trimmed.StartsWith("\""))
        {
          continue;
        }

        int IndexKeyStart = 0;
        int IndexKeyEnd = Trimmed.IndexOf("\"", IndexKeyStart + 1);
        if (IndexKeyEnd == -1)
        {
          continue;
        }

        string Key = Trimmed.Substring(IndexKeyStart + 1, IndexKeyEnd - IndexKeyStart - 1);
        if (Key != "path")
        {
          continue;
        }

        int IndexValueStart = Trimmed.IndexOf("\"", IndexKeyEnd + 1);
        if (IndexValueStart == -1)
        {
          continue;
        }

        int IndexValueEnd = Trimmed.IndexOf("\"", IndexValueStart + 1);
        if (IndexValueEnd == -1)
        {
          continue;
        }

        string Value = Trimmed.Substring(IndexValueStart + 1, IndexValueEnd - IndexValueStart - 1);
        Value = Value.Replace("\\\\", "\\");

        string PotentialPath = Value + @"\steamapps\common\" + FolderName;
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
      if (ActiveUserValue == null || ActiveUserValue is not int)
      {
        return false;
      }
      if (ActivePidValue == null || ActivePidValue is not int)
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
        if (proc == null || proc.HasExited)
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
