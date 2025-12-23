using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace belttentiontest
{
    public struct MotorOutputValues
    {
        public float ConeringWeight; 
        public float VerticalWeight;
        public float BreakingWeight;

        public float ConeringForce;
        public float VerticalForce;
        public float BreakingForce;

        public static MotorOutputValues FromValues(float conering, float vertical, float breaking)
        {
            return new MotorOutputValues
            {
                ConeringForce = conering,
                VerticalForce = vertical,
                BreakingForce = breaking,
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
            ConeringForce = 0,
            VerticalForce = 0,
            BreakingForce = 0,
            ConeringWeight = 0,
            VerticalWeight = 0,
            BreakingWeight = 0
        };

        public float CalcluateMotorSignalOutput(MotorSettings settings)
        {

            float signal = BreakingForce * BreakingWeight + ConeringForce * ConeringWeight + VerticalForce * VerticalWeight; //add all forces
            if (settings.Invert)
                signal = settings.Max - signal;
            if (signal < settings.Min) signal = settings.Min;
            if (signal > settings.Max) signal = settings.Max;
            return signal;
        }
    }

    public struct MotorSettings
    {
        public float MaxPower;
        public float GForceMult;
        public float CurveAmount;
        public float ConeringCurveAmount;
        public float ConeringStrengh;
        public float VerticalStrengh;
        public bool Invert;
        public float Min;
        public float Max;


        public MotorOutputValues CalculateMotorValue(float SimLongValue, float SimLateralValue, float SimVerValue)
        {
            SimLongValue = Math.Clamp(SimLongValue, 0, 7);
            SimLateralValue = Math.Clamp(SimLateralValue, 0, 5);
            SimVerValue = Math.Clamp(SimVerValue, 0, 5);

            MotorOutputValues motorOutput = MotorOutputValues.Zero;
            // Apply curve: value in [0,1023], curveAmount >= 0.0
            float inputValue = Math.Clamp(SimLongValue, 0, 7);
            double normalized = inputValue / 7.0;
            double curved = Math.Pow(normalized, CurveAmount); // 0..1
            float yValue = (float)curved * GForceMult; // full scale
            yValue *= (MaxPower / 100f);
            yValue = Math.Clamp(yValue, 0, 7f);
            float maxV = Max - Min;

           
            yValue = (yValue * (Max - Min)) + Min;

            motorOutput.BreakingForce = yValue;

            SimLateralValue = Math.Clamp(SimLateralValue, 0, 5);

            float lat_normal = SimLateralValue / 5.0f; // normalize to 0..1
            double lat_curved = Math.Pow(lat_normal, ConeringCurveAmount);
            SimLateralValue = (float)lat_curved * 5f; // scale back to 0..5
           
            
            motorOutput.ConeringForce = SimLateralValue * ConeringStrengh;
               
            motorOutput.VerticalForce = SimVerValue * VerticalStrengh;
                
            


            motorOutput.EnableAll();
            return motorOutput;
        }
    }
   
}
