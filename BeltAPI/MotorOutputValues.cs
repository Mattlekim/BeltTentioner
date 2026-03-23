using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BeltAPI
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

        /// <summary>
        /// this is the point where the motors are in the ideal position, 
        /// this is used to add a resting point to the motor output, 
        /// if motors go negative you get lighening
        /// positive you get more force, 
        public float RestingPoint;

        /// <summary>
        /// the total force going to the belt made up of all forces combinded
        /// </summary>
        public float TotalForceOutput => ConeringForceOutput + VerticalForceOutput + LongForceOutput;
        public static MotorOutputValues FromValues(float conering, float vertical, float breaking, int restingpoint)
        {
            return new MotorOutputValues
            {
                ConeringForceInput = conering,
                VerticalForceInput = vertical,
                LongForceInput = breaking,
                ConeringWeight = 1,
                VerticalWeight = 1,
                BreakingWeight = 1,
                RestingPoint = restingpoint
            };
        }

        /// <summary>
        /// enable everything
        /// I am using weights as a way to disable or enable certain forces, this is for ease of use in the future if we want to add more forces or have a need to disable one without changing the code
        /// </summary>
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
            BreakingWeight = 0,
            RestingPoint = 0
        };

      
        private float CalculateSurgeForces(MotorSettings settings)
        {
            float curved = settings.CalculateCurve(LongForceInput, settings.CurveAmount, Axis.Surge);
            LongForceOutput = curved * settings.SurgeStrength;
            return LongForceOutput;
        }

        private float CalculateSwayForces(MotorSettings settings)
        {
            float curved = settings.CalculateCurve(ConeringForceInput, settings.ConeringCurveAmount, Axis.Sway);
            ConeringForceOutput = curved * settings.SwayStrength;
            return ConeringForceOutput;
        }

        private float CalculateHeaveForces(MotorSettings settings)
        {
            float curved = settings.CalculateCurve(VerticalForceInput, 1, Axis.Heave);
            VerticalForceOutput = curved * settings.HeaveStrength;
            return VerticalForceOutput;
        }

        /// <summary>
        /// Call this to calculate all forces that we will send to the belt
        /// this ussing the given motor settings and returns the motor forces
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public float CalcluateMotorSignalOutput(MotorSettings settings)
        {
            float signal = (CalculateSurgeForces(settings) * BreakingWeight) + (CalculateSwayForces(settings) * ConeringWeight) + (CalculateHeaveForces(settings) * VerticalWeight); //add all forces
            signal = settings.ClampToMaxMotorPower(signal + RestingPoint); //make sure its within range of motor
            if (settings.LeftInverted)
                signal = settings.LeftMaximumAngle - signal;

            if (signal < settings.LeftMinimumAngle) signal = settings.LeftMinimumAngle;
            if (signal > settings.LeftMaximumAngle) signal = settings.LeftMaximumAngle;
            return signal;
        }

    }

}
