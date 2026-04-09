using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Loader.Tests
{
  [TestClass]
  public class LauncherTests
  {
    [TestMethod]
    public void IsProcessRunning_ReturnsFalseWhenNoProcessSet()
    {
      var launcher = new Loader.Services.Launcher();
      Assert.IsFalse(launcher.IsProcessRunning());
    }

    [TestMethod]
    public void IsProcessRunning_ReturnsTrueForCurrentProcess()
    {
      var launcher = new Loader.Services.Launcher();
      var property = typeof(Loader.Services.Launcher).GetProperty("RunningProcessId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
      Assert.IsNotNull(property);

      property!.SetValue(launcher, (uint)Process.GetCurrentProcess().Id);
      Assert.IsTrue(launcher.IsProcessRunning());
    }

    [TestMethod]
    public void GetProcessModuleBaseAddress_CurrentProcess_ReturnsNonZero()
    {
      using var process = Process.GetCurrentProcess();
      IntPtr baseAddress = Loader.WinAPI.GetProcessModuleBaseAddress(process.Id);
      Assert.AreNotEqual(IntPtr.Zero, baseAddress);
    }
  }
}
