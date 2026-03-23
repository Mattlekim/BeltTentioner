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

        internal float ClampToMaxMotorPower(float value)
        {
            float sFactor = (MaxPower / 100f);

            float mRange = Math.Abs(LeftMaximumAngle - LeftMinimumAngle);
            if (value > mRange * sFactor)
                value = mRange * sFactor;
            return value;
        }

        private float ScaleToMotorRange(float value)
        {
            float scaledValue = (value * (LeftMaximumAngle - LeftMinimumAngle)) + LeftMinimumAngle;
            scaledValue = Math.Clamp(scaledValue, LeftMinimumAngle, LeftMaximumAngle);
            return scaledValue;
        }

        public MotorOutputValues Setup(float SimLongValue, float SimLateralValue, float SimVerValue, int restingPoint)
        {
            SimLongValue = Math.Clamp(SimLongValue, -4, SurgeGForceScale);
            SimLateralValue = Math.Clamp(SimLateralValue, 0, SwayGForceScale);
            SimVerValue = Math.Clamp(SimVerValue, -2, HeaveGForceScale);

            MotorOutputValues motorOutput = MotorOutputValues.Zero;

            motorOutput.LongForceInput = SimLongValue;

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
