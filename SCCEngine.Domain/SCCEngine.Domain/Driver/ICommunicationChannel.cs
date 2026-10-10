using System;
using System.Collections.Generic;
using System.Text;

namespace SCCEngine.Domain.DriverLayer
{
    public interface ICommunicationChannel : IAsyncDisposable
    {
        bool IsConnected { get; }
        ValueTask<int> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            Memory<byte> responseBuffer,
            CancellationToken cancellationToken = default
            );
    }
}
