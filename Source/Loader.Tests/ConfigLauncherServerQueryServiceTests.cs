using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Loader.Tests
{
    [TestClass]
    public class ConfigLauncherServerQueryServiceTests
    {
        [TestMethod]
        public void ConfigService_SaveAndLoadSettings_WorksForExistingExePath()
        {
            // Arrange
            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempPath);
            string exeFile = Path.Combine(tempPath, "DarkSoulsIII.exe");
            File.WriteAllText(exeFile, "stub");

            var configService = new Loader.Services.ConfigService();
            var serverList = new ServerConfigList();
            var config = new ServerConfig { Name = "Test", Hostname = "127.0.0.1", ManualImport = true, GameType = "DarkSouls3" };
            serverList.Servers.Add(config);

            try
            {
                // Act
                configService.SaveSettings(serverList, exeFile, true, 2);
                var loadedList = configService.LoadSettings();

                // Assert
                Assert.AreEqual(configService.Ds3ExeLocation, exeFile);
                Assert.IsTrue(configService.HidePassworded);
                Assert.AreEqual(2, configService.MinimumPlayers);
                Assert.AreEqual(1, loadedList.Servers.Count);
                Assert.AreEqual("Test", loadedList.Servers[0].Name);
            }
            finally
            {
                File.Delete(exeFile);
                Directory.Delete(tempPath);
            }
        }

        [TestMethod]
        public void Launcher_ShouldRunContinualUpdate_TrueWhenSteamNotRunningOrNoProcess()
        {
            var launcher = new Loader.Services.Launcher();

            // Without process handle and Steam inactive, should be true.
            bool result = launcher.ShouldRunContinualUpdate();
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Launcher_PerformLaunch_FailsWithoutPublicKey()
        {
            var launcher = new Loader.Services.Launcher();
            var cfg = new ServerConfig { PublicKey = string.Empty, Hostname = "127.0.0.1", PrivateHostname = "127.0.0.1", Port = 1234, GameType = "DarkSouls3", Name = "Test" };

            bool result = launcher.PerformLaunch(cfg, "C:\\noexe\\DarkSoulsIII.exe", "1.2.3.4", "192.168.1.5", false, out string? errorKey);

            Assert.IsFalse(result);
            Assert.AreEqual("no_public_key_available", errorKey);
        }

        [TestMethod]
        public async Task ServerQueryService_QueryServersAsync_RespectsCancellation()
        {
            var service = new TestableServerQueryService();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await service.QueryServersAsync(cts.Token);
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ServerQueryService_QueryServersAsync_ReturnsValues()
        {
            var service = new TestableServerQueryService();

            var result = await service.QueryServersAsync(CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result!.Count);
            Assert.AreEqual("demo", result[0].Name);
        }

        private class TestableServerQueryService : Loader.Services.ServerQueryService
        {
            protected override Task<List<ServerConfig>?> QueryServersFromHubAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<List<ServerConfig>?>(new List<ServerConfig> { new ServerConfig { Name = "demo" } });
            }
        }
    }
}
