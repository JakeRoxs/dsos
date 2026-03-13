using Microsoft.VisualStudio.TestTools.UnitTesting;
using Loader.Services;
using System.Collections.Generic;

namespace Loader.Tests
{
    [TestClass]
    public class ServerListManagerTests
    {
        private ServerConfigList MakeList(params ServerConfig[] servers)
        {
            var list = new ServerConfigList();
            foreach (var s in servers)
                list.Servers.Add(s);
            return list;
        }

        private ServerConfig MakeConfig(string id, string name = "", string desc = "", string game="DarkSouls3",
            bool manual=false, bool password=false, int players=0)
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
        public void Filter_HidesByGameType()
        {
            var cfgA = MakeConfig("1", game: "DarkSouls2");
            var cfgB = MakeConfig("2", game: "DarkSouls3");
            var mgr = new ServerListManager(MakeList(cfgA, cfgB), GameType.DarkSouls3);

            var result = mgr.Filter("", false, 0);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("2", result[0].Id);
        }

        [TestMethod]
        public void Filter_SearchTextMatchesNameOrDescription()
        {
            var cfg = MakeConfig("1", name: "hello world", desc: "other");
            var mgr = new ServerListManager(MakeList(cfg), GameType.DarkSouls3);

            var res1 = mgr.Filter("hello", false, 0);
            Assert.AreEqual(1, res1.Count);
            var res2 = mgr.Filter("xyz", false, 0);
            Assert.AreEqual(0, res2.Count);
        }

        [TestMethod]
        public void Filter_HidePasswordedAndMinPlayers()
        {
            var cfg = MakeConfig("1", players: 1, password: true);
            var mgr = new ServerListManager(MakeList(cfg), GameType.DarkSouls3);

            var r1 = mgr.Filter("", true, 0);
            Assert.AreEqual(0, r1.Count);
            var r2 = mgr.Filter("", false, 2);
            Assert.AreEqual(0, r2.Count);
            var r3 = mgr.Filter("", false, 0);
            Assert.AreEqual(1, r3.Count);
        }

        [TestMethod]
        public void Filter_ManualImportAlwaysShown()
        {
            var cfg = MakeConfig("1", manual: true, players: 0, password: true, game: "DarkSouls2");
            var mgr = new ServerListManager(MakeList(cfg), GameType.DarkSouls3);
            var r = mgr.Filter("", true, 100);
            Assert.AreEqual(1, r.Count);
        }

        [TestMethod]
        public void AddOrUpdate_AddsAndUpdatesAndRemovesDuplicates()
        {
            var baseCfg = MakeConfig("1", players: 1);
            var mgr = new ServerListManager(MakeList(baseCfg), GameType.DarkSouls3);
            var newCfg = MakeConfig("1", players: 5);
            var other = MakeConfig("2");
            mgr.AddOrUpdate(new List<ServerConfig> { newCfg, other, other });
            var all = mgr.GetAll();
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(5, mgr.GetById("1").PlayerCount);
        }
    }
}
