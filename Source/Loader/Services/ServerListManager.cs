#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Loader.Services
{
  /// <summary>
  /// Manages the in-memory server list and provides filtering logic that was previously
  /// embedded in MainForm. This class has no WinForms dependencies and can be unit
  /// tested.
  /// </summary>
  public class ServerListManager
  {
    private readonly ServerConfigList _serverList;
    private GameType _currentGameType;

    public ServerListManager(ServerConfigList initialList, GameType initialGame)
    {
      _serverList = initialList ?? throw new ArgumentNullException(nameof(initialList));
      _currentGameType = initialGame;
    }

    public GameType CurrentGameType
    {
      get => _currentGameType;
      set => _currentGameType = value;
    }

    /// <summary>
    /// Returns a copy of the underlying list. Caller may mutate without affecting the manager.
    /// </summary>
    public List<ServerConfig> GetAll() => new List<ServerConfig>(_serverList.Servers);

    public ServerConfig? GetById(string id)
    {
      if (id == null)
        return null;
      return _serverList.Servers.FirstOrDefault(s => s.Id == id);
    }

    public void AddOrUpdate(IEnumerable<ServerConfig> servers)
    {
      if (servers == null)
        return;

      // ensure any existing entries are updated and new ones added
      foreach (var server in servers)
      {
        var existing = GetById(server.Id);
        if (existing != null)
        {
          existing.CopyTransientPropsFrom(server);
        }
        else
        {
          _serverList.Servers.Add(server);
        }
      }

      // remove duplicates and stale entries
      Cleanup();
    }

    private void Cleanup()
    {
      // Remove duplicate IDs while preserving the first occurrence.
      // Using List<T>.RemoveAll avoids manual index management.
      var seen = new HashSet<string>();
      _serverList.Servers.RemoveAll(s =>
      {
        if (seen.Contains(s.Id))
          return true;
        seen.Add(s.Id);
        return false;
      });
    }

    /// <summary>
    /// Apply filtering rules (game type, search text, password hide, minimum players).
    /// Returns a new list containing only the servers that should be shown.
    /// </summary>
    public List<ServerConfig> Filter(string searchFilter, bool hidePassworded, int minimumPlayers)
    {
      searchFilter = searchFilter?.Trim().ToLower() ?? string.Empty;
      bool hasFilterText = !string.IsNullOrWhiteSpace(searchFilter);

      return _serverList.Servers.Where(cfg => ShouldShowServer(cfg, searchFilter, hasFilterText, hidePassworded, minimumPlayers)).ToList();
    }

    private bool ShouldShowServer(ServerConfig config, string filter, bool hasFilterText, bool hidePassworded, int minimumPlayers)
    {
      if (config.ManualImport)
        return true;

      if (config.GameType != _currentGameType.ToString())
        return false;

      if (hasFilterText)
      {
        if (!config.Name.ToLower().Contains(filter) && !config.Description.ToLower().Contains(filter))
          return false;
      }
      else
      {
        if (config.PasswordRequired && hidePassworded)
          return false;
        if (config.PlayerCount < minimumPlayers)
          return false;
      }

      return true;
    }
  }
}
