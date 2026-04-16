using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeltAPI
{
    public class CarSettings
    {

        public const float SurgeGForceScale = 7.0f; // 7g max for long
        public const float SwayGForceScale = 5.0f; // 5g max for lateral and vertical
        public const float HeaveGForceScale = 3.0f;

        public int MaxPower { get; set; } = 100;

        public float SurgeStrenght { get; set; } = 1.0f;
        public float SurgeCurveAmount { get; set; } = 1.0f;
        public bool InvertSurge { get; set; } = false; // Added for invert braking

        public float SwayStrength { get; set; } = 1.0f;
        public float SwayCurveAmount { get; set; } = 1.0f; // NEW: Cornering curve amount
        public bool InvertSway { get; set; } = false; // Added for invert cornering

        public float HeaveStrength { get; set; } = 1.0f;
        public bool InvertHeave { get; set; } = false; // Added for invert vertical

        public float AbsStrength { get; set; } = 1.0f; // NEW: ABS strength
        public bool AbsEnabled { get; set; } = false;  // NEW: ABS enabled


        public int RestingPoint { get; set; } = 0; // NEW: Resting point for the motor

        public float NegativeSway { get; set; } = 0f;

        public float PitchStrength { get; set; } = 10f;
        public bool InvertPitch { get; set; } = false;
        public float RollStrength { get; set; } = 10f;
        public bool InvertRoll { get; set; } = false;
     
        public float MasterTiltStrength { get; set; } = 10f;

        public CarSettings DeepCopy()
        {
            return new CarSettings
            {
                MaxPower = this.MaxPower,
                SurgeStrenght = this.SurgeStrenght,
                SurgeCurveAmount = this.SurgeCurveAmount,
                InvertSurge = this.InvertSurge,
                SwayStrength = this.SwayStrength,
                SwayCurveAmount = this.SwayCurveAmount,
                InvertSway = this.InvertSway,
                HeaveStrength = this.HeaveStrength,
                InvertHeave = this.InvertHeave,
                AbsStrength = this.AbsStrength,
                AbsEnabled = this.AbsEnabled,
                RestingPoint = this.RestingPoint,
                NegativeSway = this.NegativeSway,
                PitchStrength = this.PitchStrength,
                InvertPitch = this.InvertPitch,
                RollStrength = this.RollStrength,
                InvertRoll = this.InvertRoll,
         
                MasterTiltStrength = this.MasterTiltStrength
            };
        }
    }
}
