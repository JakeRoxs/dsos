using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Loader.Services
{
  public class ServerQueryService : IDisposable
  {
    private Task<List<ServerConfig>?>? _currentQueryTask;
    private CancellationTokenSource? _internalCts;
    private bool _disposed;

    public bool IsQueryInProgress => _currentQueryTask != null && !_currentQueryTask.IsCompleted;

    public virtual Task<List<ServerConfig>?> QueryServersFromMasterAsync(CancellationToken cancellationToken)
    {
      // MasterServerApi.ListServers can return null on failure.
      return Task.Run(() => MasterServerApi.ListServers(), cancellationToken);
    }

    public async Task<List<ServerConfig>?> QueryServersAsync(CancellationToken cancellationToken)
    {
      Debug.WriteLine("Querying master server ...");

      if (IsQueryInProgress)
      {
        return null;
      }

      _internalCts?.Cancel();
      _internalCts?.Dispose();
      _internalCts = new CancellationTokenSource();

      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _internalCts.Token);
      _currentQueryTask = QueryServersFromMasterAsync(linkedCts.Token);

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
      if (_disposed)
      {
        return;
      }

      _internalCts?.Cancel();
      _internalCts?.Dispose();
      _internalCts = null;

      _disposed = true;
      GC.SuppressFinalize(this);
    }
  }
}
