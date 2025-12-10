using System;
using System.Collections.Generic;

namespace belttentiontest
{
    public class CarSettings
    {
        public float MaxGForceMult { get; set; } = 1.0f;
        public int MaxPower { get; set; } = 100;
        public double CurveAmount { get; set; } = 1.0;
        public float CorneringStrength { get; set; } = 1.0f;
    }

    public class CarSettingsStore
    {
        public Dictionary<string, CarSettings> Settings { get; set; } = new();
    }
}
