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
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Loader
{
  public static class HubApi
  {
    private static readonly HttpClient Client = new HttpClient();

    private class BaseResponse
    {
      public string Status { get; set; } = string.Empty;
      public string Message { get; set; } = string.Empty;
    }

    private class ListServersResponse : BaseResponse
    {
      public List<ServerConfig> Servers { get; set; } = new List<ServerConfig>();
    }

    private class GetPublicKeyRequest : BaseResponse
    {
      public string Password { get; set; } = string.Empty;
    }

    private class GetPublicKeyResponse : BaseResponse
    {
      public string PublicKey { get; set; } = string.Empty;
    }

    static HubApi()
    {
      Client.DefaultRequestHeaders.Accept.Clear();
      Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // This is a super shitty way of doing most of this, it should be async, but got a bunch of wierd deadlocks when
    // I tried it before, and fixing it would require me learning more C# than I want to ...

    private static ResultType DoRequest<ResultType>(HttpMethod Method, string Uri, HttpContent Content = null)
        where ResultType : BaseResponse
    {
      ResultType Result = null;

      try
      {
        using HttpRequestMessage Request = new HttpRequestMessage(Method, Uri);
        Request.Content = Content;

        using HttpResponseMessage Response = Client.Send(Request);
        if (!Response.IsSuccessStatusCode)
        {
          Debug.WriteLine("Got error status when trying to query hub server: {0}", Response.StatusCode);
          return null;
        }

        Task<ResultType> ResponseTask = Response.Content.ReadFromJsonAsync<ResultType>();
        ResponseTask.ConfigureAwait(false);
        ResponseTask.Wait();
        if (!ResponseTask.IsCompletedSuccessfully)
        {
          Debug.WriteLine("Got error status when trying to query hub server.");
          return null;
        }

        ResultType TypedResponse = ResponseTask.Result;
        if (TypedResponse.Status != "success")
        {
          Debug.WriteLine("Got error when trying to query hub server: {0}", TypedResponse.Status);
          return null;
        }

        Result = ResponseTask.Result;
      }
      catch (HttpRequestException Ex)
      {
        Debug.WriteLine("Received HTTP exception when trying to get servers: {0}", Ex.Message);
      }
      catch (TaskCanceledException Ex)
      {
        Debug.WriteLine("Received timeout/cancellation when trying to get servers: {0}", Ex.Message);
      }
      catch (NotSupportedException Ex)
      {
        Debug.WriteLine("Received unsupported-content exception when trying to get servers: {0}", Ex.Message);
      }
      catch (System.Text.Json.JsonException Ex)
      {
        Debug.WriteLine("Received JSON exception when trying to get servers: {0}", Ex.Message);
      }
      catch (InvalidOperationException Ex)
      {
        Debug.WriteLine("Received exception when trying to get servers: {0}", Ex.Message);
      }

      return Result;
    }

    public static List<ServerConfig> ListServers()
    {
      ListServersResponse Result = DoRequest<ListServersResponse>(HttpMethod.Get, ProgramSettings.Default.hub_server_url + "/api/v1/servers");
      if (Result != null && Result.Servers != null)
      {
        foreach (ServerConfig config in Result.Servers.Where(config => string.IsNullOrEmpty(config.Id) && !string.IsNullOrEmpty(config.IpAddress)))
        {
          config.Id = config.IpAddress;
        }
        return Result.Servers;
      }

      return new List<ServerConfig>();
    }

    public static string GetPublicKey(string ServerId, string Password)
    {
      GetPublicKeyRequest Request = new GetPublicKeyRequest();
      Request.Password = Password;

      GetPublicKeyResponse Result = DoRequest<GetPublicKeyResponse>(HttpMethod.Post, ProgramSettings.Default.hub_server_url + "/api/v1/servers/" + ServerId + "/public_key", JsonContent.Create<GetPublicKeyRequest>(Request));
      if (Result != null)
      {
        return Result.PublicKey;
      }
      return "";
    }
  }
}
