using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;

namespace belttentiontest
{
    public struct MotorOutputValues
    {
        public float ConeringWeight; 
        public float VerticalWeight;
        public float BreakingWeight;

        public float ConeringForceInput;
        public float VerticalForceInput;
        public float LongForceInput;

        public float ConeringForceOutput;
        public float VerticalForceOutput;
        public float LongForceOutput;

        public float TotalForceOutput => ConeringForceOutput + VerticalForceOutput + LongForceOutput;
        public static MotorOutputValues FromValues(float conering, float vertical, float breaking)
        {
            return new MotorOutputValues
            {
                ConeringForceInput = conering,
                VerticalForceInput = vertical,
                LongForceInput = breaking,
                ConeringWeight = 1,
                VerticalWeight = 1,
                BreakingWeight = 1
            };
        }

        public void EnableAll()
        {
            ConeringWeight = 1;
            VerticalWeight = 1;
            BreakingWeight = 1;
        }

        public static MotorOutputValues Zero => new MotorOutputValues
        {
            ConeringForceInput = 0,
            VerticalForceInput = 0,
            LongForceInput = 0,
            ConeringWeight = 0,
            VerticalWeight = 0,
            BreakingWeight = 0
        };

        private float CalculateLongForces(MotorSettings settings)
        {

            float curved = settings.CalculateCurve(LongForceInput, settings.CurveAmount, MotorSettings.LongGForceScale);
            LongForceOutput = curved * settings.GForceMult;
            return LongForceOutput;

        }

        public float CalculateLateralForces(MotorSettings settings)
        {
            float curved = settings.CalculateCurve(ConeringForceInput, settings.ConeringCurveAmount, MotorSettings.ConeringGForceScale);
            ConeringForceOutput = curved * settings.ConeringStrengh;
            return ConeringForceOutput;
        }

        public float CalculateVerticalForces(MotorSettings settings)
        {
            float curved = settings.CalculateCurve(VerticalForceInput, settings.ConeringCurveAmount, MotorSettings.VerticalGForceScale);
            VerticalForceOutput = curved * settings.VerticalStrengh;
            return VerticalForceOutput;
        }

        public float CalcluateMotorSignalOutput(MotorSettings settings)
        { 
            float signal = (CalculateLongForces(settings) * BreakingWeight) + (CalculateLateralForces(settings) * ConeringWeight) + (CalculateVerticalForces(settings) * VerticalWeight); //add all forces

            signal = settings.ClampToMaxMotorPower(signal);

            if (settings.Invert)
                signal = settings.Max - signal;

            if (signal < settings.Min) signal = settings.Min;
            if (signal > settings.Max) signal = settings.Max;
            return signal;
        }
    }

    public struct MotorSettings
    {
        public const float LongGForceScale = 7.0f; // 7g max for long
        public const float ConeringGForceScale = 5.0f; // 5g max for lateral and vertical
        public const float VerticalGForceScale = 5.0f;


        public float MaxPower;
        public float GForceMult;
        public float CurveAmount;
        public float ConeringCurveAmount;
        public float ConeringStrengh;
        public float VerticalStrengh;
        public bool Invert;
        public float Min;
        public float Max;


        public float CalculateCurve(float inputValue, float curveAmount, float scale)
        {
            // Apply curve: value in [0,1023], curveAmount >= 0.0
            double normalized = inputValue / scale;
            return (float)Math.Pow(normalized, curveAmount); // 0..1
              
        }

        public float ClampToMaxMotorPower(float value)
        {
            float sFactor = (MaxPower / 100f);
            
            float mRange = Math.Abs(Max - Min);
            if (value > mRange * sFactor)
                value = mRange * sFactor;
            return value;
        }   

        public float ScaleToMotorRange(float value)
        {
            float scaledValue = (value * (Max - Min)) + Min;
            scaledValue = Math.Clamp(scaledValue, Min, Max);
            return scaledValue;
        }

        public MotorOutputValues Setup(float SimLongValue, float SimLateralValue, float SimVerValue)
        {
            SimLongValue = Math.Clamp(SimLongValue, 0, LongGForceScale);
            SimLateralValue = Math.Clamp(SimLateralValue, 0, ConeringGForceScale);
            SimVerValue = Math.Clamp(SimVerValue, 0, VerticalGForceScale);

            MotorOutputValues motorOutput = MotorOutputValues.Zero;
           
            motorOutput.LongForceInput  = SimLongValue;
            
            motorOutput.ConeringForceInput = SimLateralValue;
               
            motorOutput.VerticalForceInput = SimVerValue;

            motorOutput.EnableAll();
            return motorOutput;
        }
    }
   
}
