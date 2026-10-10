using System;
using System.Collections.Generic;
using System.Text;

namespace SCCEngine.Domain.Models.DriverModels
{
    public class ResilienceSettings
    {
        public int MaxRetries { get; set; } = 3;
        public int TimeoutMs { get; set; } = 1000;
        public double CircuitBreakerFailureRatio { get; set; } = 0.5;
    }
}
