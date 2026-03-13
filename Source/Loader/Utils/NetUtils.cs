using System;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace Loader
{
    public static class NetUtils
    {
        public static string HostnameToIPv4(string Hostname)
        {
            try
            {
                IPAddress[] Addresses = Dns.GetHostAddresses(Hostname);
                foreach (IPAddress Addr in Addresses)
                {
                    if (Addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return Addr.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // Shitty catch all exception ...
            }
            return "";
        }

        public static string GetMachineIPv4(bool GetPublicAddress)
        {
            try
            { 
                if (GetPublicAddress)
                {
                    using (HttpClient client = new HttpClient())
                    {
                        // synchronous call for simplicity
                        return client.GetStringAsync("http://api.ipify.org").GetAwaiter().GetResult();
                    }
                }
                else
                {
                    return HostnameToIPv4(Dns.GetHostName());
                }
            }
            catch (Exception)
            {
                // Shitty catch all exception ...
            }
            return "";
        }
    }
}
