using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeltAPI
{
    public struct MotorSettings
    {
        public const float SurgeGForceScale = 7.0f; // 7g max for long
        public const float SwayGForceScale = 5.0f; // 5g max for lateral and vertical
        public const float HeaveGForceScale = 3.0f;


        public float MaxPower;
        public float SurgeStrength;
        public float CurveAmount;
        public float ConeringCurveAmount;
        public float SwayStrength;
        public float HeaveStrength;

        public bool LeftInverted;
        public float LeftMinimumAngle;
        public float LeftMaximumAngle;

        public bool RightInverted;
        public float RightMinimumAngle;
        public float RightMaximumAngle;

        /// <summary>
        /// calculate teh motor curve
        /// </summary>
        /// <param name="inputValue">the input force</param>
        /// <param name="curveAmount">the power curve</param>
        /// <param name="scale">axis we are working on</param>
        /// <returns></returns>
        internal float CalculateCurve(float inputValue, float curveAmount, Axis axis)
        {

            float scale = axis switch
            {
                Axis.Surge => SurgeGForceScale,
                Axis.Sway => SwayGForceScale,
                Axis.Heave => HeaveGForceScale,
                _ => 1f,
            };
            
            //who would have thought we would need imagainary numbers :)
            bool iNumber = inputValue < 0 ? true : false;

            inputValue = Math.Abs(inputValue); //force posite
            double normalized = inputValue / scale; //scale it to 0..1

            //make the curve
            float output = (float)Math.Pow(normalized, curveAmount);

            //return the output, if the input was negative we return a negative output
            return iNumber ? -output : output;

        }

        internal (float, float) ClampToMaxMotorPower(float lValue, float rValue)
        {

            float sFactor = (MaxPower / 100f);

            float lRange = Math.Abs(LeftMaximumAngle - LeftMinimumAngle); //get range

            lValue = lValue * lRange * sFactor; //scale to range

            float rRange = Math.Abs(RightMaximumAngle - RightMinimumAngle); //get range

            rValue = rValue * rRange * sFactor; //scale to range 

            //clamp values to make sure they are within the motor limits
            lValue = Math.Clamp(lValue, LeftMinimumAngle, LeftMaximumAngle);
            rValue = Math.Clamp(rValue, RightMinimumAngle, RightMaximumAngle);

            return (lValue, rValue);
        }

        private float ScaleToMotorRange(float value)
        {
            float scaledValue = (value * (LeftMaximumAngle - LeftMinimumAngle)) + LeftMinimumAngle;
            scaledValue = Math.Clamp(scaledValue, LeftMinimumAngle, LeftMaximumAngle);
            return scaledValue;
        }

        public BeltMotorData Setup(float SimLongValue, float SimLateralValue, float SimVerValue, int restingPoint)
        {
            SimLongValue = Math.Clamp(SimLongValue, -SwayGForceScale, SurgeGForceScale);
            SimLateralValue = Math.Clamp(SimLateralValue, -SwayGForceScale, SwayGForceScale);
            SimVerValue = Math.Clamp(SimVerValue, -HeaveGForceScale, HeaveGForceScale);

            BeltMotorData motorOutput = BeltMotorData.Zero;

            motorOutput.SurgeForceInput = SimLongValue;

            motorOutput.ConeringForceInput = SimLateralValue;

            motorOutput.VerticalForceInput = SimVerValue;

            motorOutput.RestingPoint = restingPoint;

            motorOutput.EnableAll();
            return motorOutput;
        }

        public void SendDataToBelt()
        {

        }
    }

}
