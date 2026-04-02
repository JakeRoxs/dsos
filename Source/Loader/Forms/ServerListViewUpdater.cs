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
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Loader
{
  /// <summary>
  /// Helper that updates a <see cref="ListView"/> to match a filtered server list.
  /// Keeps existing items when possible, updates values, and removes stale entries.
  /// </summary>
  public static class ServerListViewUpdater
  {
    public static void Update(ListView listView, IReadOnlyList<ServerConfig> filtered, string officialServerHostname)
    {
      // Add or update items for the current filtered list.
      foreach (var config in filtered)
      {
        var item = listView.Items.Cast<ListViewItem>().FirstOrDefault(i => (i.Tag as ServerConfig)?.Id == config.Id);
        if (item == null)
        {
          item = new ListViewItem(new string[3], -1);
          listView.Items.Add(item);
        }

        bool isOfficial = (config.Hostname == officialServerHostname && !config.IsShard);
        item.Text = config.Name;
        item.Tag = config;
        item.SubItems[0].Text = config.Name;
        item.SubItems[1].Text = config.ManualImport ? "Not Available For Manual Import" : config.PlayerCount.ToString();
        item.SubItems[2].Text = config.Description;
        item.BackColor = (isOfficial ? Color.PaleGoldenrod : Color.Transparent);

        if (isOfficial)
          item.ImageIndex = 10;
        else if (config.PasswordRequired)
          item.ImageIndex = 0;
        else if (config.ManualImport)
          item.ImageIndex = 7;
        else
          item.ImageIndex = 8;
      }

      // Remove stale items that are no longer in the filtered list.
      var filteredIds = new HashSet<string>(filtered.Select(x => x.Id));
      var staleItems = listView.Items.Cast<ListViewItem>()
          .Where(item => (item.Tag as ServerConfig) is not ServerConfig cfg || !filteredIds.Contains(cfg.Id))
          .ToList();

      foreach (var item in staleItems)
        listView.Items.Remove(item);

      listView.Sort();
    }
  }
}
