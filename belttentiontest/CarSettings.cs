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
        public float VerticalStrength { get; set; } = 1.0f;
        public float AbsStrength { get; set; } = 1.0f; // NEW: ABS strength
        public bool AbsEnabled { get; set; } = false;  // NEW: ABS enabled
        public bool InvertCornering { get; set; } = false; // Added for invert cornering
    }

    public class CarSettingsStore
    {
        private static CarSettingsStore? _instance;
        public static CarSettingsStore Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new CarSettingsStore();
                return _instance;
            }
            set { _instance = value; }
        }

        public Dictionary<string, CarSettings> Settings { get; set; } = new();
    }
}
