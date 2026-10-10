using System;
using System.Collections.Generic;
using System.Text;

namespace SCCEngine.Domain
{
    public enum DataType
    {

    }

    public enum AddressType
    {
        ASCII,
        Registers
    }

    public enum Quality
    {
        Good,
        Bad,
        Uncertain
    }

    public enum MsgType
    {
        Value,
        Activate,
        Deactivate
    }

    public enum ProtocolType
    {
        ModbusRTU,
        Proprietary
    }
}
