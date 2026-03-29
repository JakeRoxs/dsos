using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Loader.Tests
{
    [TestClass]
    public class ServerListViewUpdaterTests
    {
        private ServerConfig MakeConfig(string id, string name = "", string desc = "", string game = "DarkSouls3",
            bool manual = false, bool password = false, int players = 0)
        {
            return new ServerConfig
            {
                Id = id,
                Name = name,
                Description = desc,
                GameType = game,
                ManualImport = manual,
                PasswordRequired = password,
                PlayerCount = players
            };
        }

        [TestMethod]
        public void Update_RemovesStaleItemsAndAddsNewOnes()
        {
            var existingConfig = MakeConfig("1", name: "existing", players: 1);
            var listView = new ListView();
            var existingItem = new ListViewItem(new string[3], -1);
            existingItem.Tag = existingConfig;
            existingItem.SubItems[0].Text = existingConfig.Name;
            existingItem.SubItems[1].Text = existingConfig.PlayerCount.ToString();
            existingItem.SubItems[2].Text = existingConfig.Description;
            listView.Items.Add(existingItem);

            var newConfig = MakeConfig("2", name: "new", players: 2);
            ServerListViewUpdater.Update(listView, new List<ServerConfig> { newConfig }, MainForm.OfficialServer);

            Assert.AreEqual(1, listView.Items.Count);
            Assert.AreEqual("new", listView.Items[0].Text);
            Assert.AreEqual("2", listView.Items[0].SubItems[1].Text);
        }

        [TestMethod]
        public void Update_UpdatesExistingItemWithLatestValues()
        {
            var configV1 = MakeConfig("1", name: "A", players: 1);
            var listView = new ListView();
            var item = new ListViewItem(new string[3], -1);
            item.Tag = configV1;
            listView.Items.Add(item);

            var configV2 = MakeConfig("1", name: "B", players: 5);
            ServerListViewUpdater.Update(listView, new List<ServerConfig> { configV2 }, MainForm.OfficialServer);

            Assert.AreEqual(1, listView.Items.Count);
            Assert.AreEqual("B", listView.Items[0].Text);
            Assert.AreEqual("5", listView.Items[0].SubItems[1].Text);
        }
    }
}
