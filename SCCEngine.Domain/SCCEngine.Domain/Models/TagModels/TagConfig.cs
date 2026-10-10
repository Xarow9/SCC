using System;
using System.Collections.Generic;
using System.Text;

namespace SCCEngine.Domain.Models.TagModels
{
    public class TagConfig
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public string Name { get; set; }
        public string DeviceName { get; set; }
        public object Address {  get; set; }
        public AddressType AddressType { get; set; }

        public TagConfig(int id, int deviceId, string name, string deviceName, object address, AddressType addressType)
        {
            Id = id;
            DeviceId = deviceId;
            Name = name;
            DeviceName = deviceName;
            Address = address;
            AddressType = addressType;
        }

    }
}
