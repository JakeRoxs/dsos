using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Loader
{
  public static class NetUtils
  {
    private static readonly string DefaultPublicIpServiceUrl = $"{Uri.UriSchemeHttps}://api.ipify.org";
    private static readonly HttpClient PublicIpClient = new HttpClient();

    private static string ResolvePublicIpServiceUrl()
    {
      string configuredUrl = Environment.GetEnvironmentVariable("REKINDLED_PUBLIC_IP_SERVICE_URL");
      if (!string.IsNullOrWhiteSpace(configuredUrl) && Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri configuredUri))
      {
        return configuredUri.ToString();
      }

      return DefaultPublicIpServiceUrl;
    }

    public static string HostnameToIPv4(string Hostname)
    {
      try
      {
        IPAddress Address = Dns.GetHostAddresses(Hostname).FirstOrDefault(Addr => Addr.AddressFamily == AddressFamily.InterNetwork);
        return Address != null ? Address.ToString() : string.Empty;
      }
      catch (SocketException)
      {
        // DNS lookup failed.
      }
      catch (ArgumentException)
      {
        // Placeholder catch all exception ...
      }
      return "";
    }

    public static string GetMachineIPv4(bool GetPublicAddress)
    {
      try
      {
        if (GetPublicAddress)
        {
          // synchronous call for simplicity
          return PublicIpClient.GetStringAsync(ResolvePublicIpServiceUrl()).GetAwaiter().GetResult();
        }
        else
        {
          return HostnameToIPv4(Dns.GetHostName());
        }
      }
      catch (HttpRequestException)
      {
        // Failed to retrieve public IP.
      }
      catch (TaskCanceledException)
      {
        // Request timed out or was cancelled.
      }
      catch (SocketException)
      {
        // Network lookup failed.
      }
      return "";
    }
  }
}
