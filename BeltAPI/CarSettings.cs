using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeltAPI
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

        public bool InvertSurge { get; set; } = false; // Added for invert braking
        public bool InvertSway { get; set; } = false; // Added for invert cornering
        public bool InvertHeave { get; set; } = false; // Added for invert vertical

        public double ConeringCurveAmount { get; set; } = 1.0; // NEW: Cornering curve amount
        public int RestingPoint { get; set; } = 0; // NEW: Resting point for the motor
    }
}
