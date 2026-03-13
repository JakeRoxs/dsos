using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;

namespace Loader.Tests
{
    [TestClass]
    public class MainFormAsyncTests
    {
        private class TestMainForm : Loader.MainForm
        {
            public TestMainForm()
            {
                // Avoid designer initialization complexity.
            }

            public Task<string> TestResolveConnectIpAsync(ServerConfig config, CancellationToken cancellationToken)
            {
                return ResolveConnectIpAsync(config, cancellationToken);
            }

            public Task<string> TestGetPublicKeyAsync(string id, string password, CancellationToken cancellationToken)
            {
                return GetPublicKeyAsync(id, password, cancellationToken);
            }

            public Task SystemQueryServersAsync(CancellationToken cancellationToken)
            {
                return QueryServersAsync(cancellationToken);
            }
        }

        [TestMethod]
        public async Task ResolveConnectIpAsync_CancelsImmediately()
        {
            var form = new TestMainForm();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => form.TestResolveConnectIpAsync(new ServerConfig(), cts.Token));
        }

        [TestMethod]
        public async Task GetPublicKeyAsync_CancelsImmediately()
        {
            var form = new TestMainForm();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => form.TestGetPublicKeyAsync("id", "", cts.Token));
        }
    }
}
