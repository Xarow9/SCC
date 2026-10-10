using SCCEngine.Domain.Models.TagModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace SCCEngine.Domain.DriverLayer
{
    public interface IProtocol
    {
        // Методы для сериализации
        int BuildReadRequest(IReadOnlyCollection<Tag> tags, Memory<byte> buffer);
        int BuildWriteRequest(IReadOnlyCollection<Tag> tags, Memory<byte> buffer);

        // Методы для десериализации
        void ParseReadResponse(ReadOnlyMemory<byte> response, IReadOnlyCollection<Tag> tagsToUpdate);
        bool ParseWriteResponse(ReadOnlyMemory<byte> responseBuffer);
    }
}
