using System;
using System.Collections.Generic;
using System.Text;
using SCCEngine.Domain.Models.TagModels;

namespace SCCEngine.Domain.Models.DriverModels
{
    public record MacrosSetup(int DeviceId, string Address, string Value);
    public class DriverConsumerConfiguration
    {
        public Tag[] Tags { get; set; } = Array.Empty<Tag>();
        public Tag[] ActiveTags { get; set; } = Array.Empty<Tag>();
        public MacrosSetup? Setup { get; set; }
    }
}
