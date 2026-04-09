#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Loader.Services
{
  public class ServerQueryService : IDisposable
  {
    private Task<List<ServerConfig>>? _currentQueryTask;
    private CancellationTokenSource? _internalCts;
    private bool _disposed;

    public bool IsQueryInProgress => _currentQueryTask != null && !_currentQueryTask.IsCompleted;

    public virtual Task<List<ServerConfig>> QueryServersFromHubAsync(CancellationToken cancellationToken)
    {
      // HubApi.ListServers now returns an empty list on failure instead of null.
      return Task.Run(() => HubApi.ListServers(), cancellationToken);
    }

    public async Task<List<ServerConfig>?> QueryServersAsync(CancellationToken cancellationToken)
    {
      Debug.WriteLine("Querying hub server ...");

      if (IsQueryInProgress)
      {
        return null;
      }

      if (_internalCts != null)
      {
        await _internalCts.CancelAsync();
        _internalCts.Dispose();
      }
      _internalCts = new CancellationTokenSource();

      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _internalCts.Token);
      _currentQueryTask = QueryServersFromHubAsync(linkedCts.Token);

      try
      {
        return await _currentQueryTask;
      }
      catch (OperationCanceledException)
      {
        return null;
      }
      finally
      {
        if (_currentQueryTask != null && _currentQueryTask.IsCompleted)
        {
          _currentQueryTask = null;
        }
      }
    }

    public void Cancel()
    {
      _internalCts?.Cancel();
    }

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
      if (_disposed)
      {
        return;
      }

      if (disposing)
      {
        _internalCts?.Cancel();
        _internalCts?.Dispose();
        _internalCts = null;
      }

      _disposed = true;
    }
  }
}
