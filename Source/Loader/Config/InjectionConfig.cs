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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Loader
{
  // Server configuration, stores the needed information required to connect to a 
  // given open server. Can be freely converted to/from json.
  [Serializable]
  public class InjectionConfig
  {
    public string ServerName { get; set; } = string.Empty;
    public string ServerHostname { get; set; } = string.Empty;
    public string ServerPublicKey { get; set; } = string.Empty;
    public string ServerGameType { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public bool EnableSeparateSaveFiles { get; set; }

    public string ToJson()
    {
      return JsonSerializer.Serialize(this);
    }

    public static bool FromJson(string json, out ServerConfig config)
    {
      try
      {
        config = JsonSerializer.Deserialize<ServerConfig>(json) ?? new ServerConfig();
        config.ManualImport = true;
        return true;
      }
      catch (JsonException)
      {
        config = new ServerConfig();
        return false;
      }
    }
  }
}
