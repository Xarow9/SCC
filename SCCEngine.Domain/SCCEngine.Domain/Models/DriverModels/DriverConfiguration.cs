using System;
using System.Collections.Generic;
using System.Text;

namespace SCCEngine.Domain.Models.DriverModels
{
    public class DriverConfiguration
    {
        public int Id;
        public string Name;
        public AddressType AddressType;
        public ProtocolType ProtocolType;
        public int? PollingIntervalMs;
    }
}
