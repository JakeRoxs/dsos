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
  // List of server configurations that can be converted freely to and from json.
  [Serializable]
  public class ServerConfigList
  {
    public List<ServerConfig> Servers { get; set; } = new List<ServerConfig>();

    public string ToJson()
    {
      return JsonSerializer.Serialize(this);
    }

    public static bool FromJson(string json, out ServerConfigList config)
    {
      try
      {
        config = JsonSerializer.Deserialize<ServerConfigList>(json);
        return true;
      }
      catch (JsonException)
      {
        config = new ServerConfigList();
        return false;
      }
    }
  }

  // Server configuration, stores the needed information required to connect to a 
  // given open server. Can be freely converted to/from json.
  [Serializable]
  public class ServerConfig
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string PrivateHostname { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public bool ManualImport { get; set; }

    // These attributes are only set if retrieved from hub server.
    public string IpAddress { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public bool PasswordRequired { get; set; }
    public string ModsWhiteList { get; set; } = string.Empty;
    public string ModsBlackList { get; set; } = string.Empty;
    public string ModsRequiredList { get; set; } = string.Empty;

    public bool AllowSharding { get; set; }
    public string WebAddress { get; set; } = string.Empty;

    public bool IsShard { get; set; }
    public string GameType { get; set; } = string.Empty;

    public void CopyTransientPropsFrom(ServerConfig Source)
    {
      Name = Source.Name;
      Description = Source.Description;
      Hostname = Source.Hostname;
      PrivateHostname = Source.PrivateHostname;
      IpAddress = Source.IpAddress;
      PlayerCount = Source.PlayerCount;
      PasswordRequired = Source.PasswordRequired;
      ModsWhiteList = Source.ModsWhiteList;
      ModsBlackList = Source.ModsBlackList;
      ModsRequiredList = Source.ModsRequiredList;
      AllowSharding = Source.AllowSharding;
      WebAddress = Source.WebAddress;
      IsShard = Source.IsShard;
      GameType = Source.GameType;
    }

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
