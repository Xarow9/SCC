using System;
using System.Collections.Generic;
using System.Text;

namespace SCCEngine.Domain.Models.TagModels
{
    public record struct Tag
        (
        int Id,
        DateTime Timestamp,
        object Value,
        Quality Quality,
        bool IsActive,
        MsgType MsgType
        );
}
