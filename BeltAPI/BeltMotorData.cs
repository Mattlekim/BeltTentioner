using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BeltAPI
{
    public struct BeltMotorData
    {
        public float SwayWeight;
        public float HeaveWeight;
        public float SurgeWeight;

        public float SwayForceInput;
        public float HeaveForceInput;
        public float SurgeForceInput;

   

        /// <summary>
        /// this is the point where the motors are in the ideal position, 
        /// this is used to add a resting point to the motor output, 
        /// if motors go negative you get lighening
        /// positive you get more force, 
        public float RestingPoint;

        /// <summary>
        /// the total force going to the belt made up of all forces combinded
        /// </summary>
        public float TotalForceOutput => lSurgeOutput + lSwayOutput + lHeaveOutput;
   

        /// <summary>
        /// enable everything
        /// I am using weights as a way to disable or enable certain forces, this is for ease of use in the future if we want to add more forces or have a need to disable one without changing the code
        /// </summary>
        public void EnableAll()
        {
            SwayWeight = 1;
            HeaveWeight = 1;
            SurgeWeight = 1;
        }

        public static BeltMotorData Zero => new BeltMotorData
        {
            SwayForceInput = 0,
            HeaveForceInput = 0,
            SurgeForceInput = 0,
            SwayWeight = 0,
            HeaveWeight = 0,
            SurgeWeight = 0,
            RestingPoint = 0
        };

      
        float lSurgeOutput, lSwayOutput, lHeaveOutput;
        float rSurgeOutput, rSwayOutput, rHeaveOutput;

        //use just left values to output to any graph you want
        public float LeftSurgeOutput => lSurgeOutput;
        public float LeftSwayOutput => lSwayOutput;
        public float LeftHeaveOutput => lHeaveOutput;


        //use just left values to output to any graph you want
        public float RightSurgeOutput => rSurgeOutput;
        public float RightSwayOutput => rSwayOutput;
        public float RightHeaveOutput => rHeaveOutput;
        private (float, float) CalculateSurgeForces(MotorSettings settings)
        {
            float curved = settings.CalculateCurve(SurgeForceInput, settings.CurveAmount, Axis.Surge);
            float output = curved * settings.SurgeStrength * .01f;
            return (output, output);
        }

        private (float, float) CalculateSwayForces(MotorSettings settings)
        {
            float curved = settings.CalculateCurve(SwayForceInput, settings.ConeringCurveAmount, Axis.Sway);
            float output = curved * settings.SwayStrength * .01f;
            return (output, output);
        }

        private (float, float) CalculateHeaveForces(MotorSettings settings)
        {
            float curved = settings.CalculateCurve(HeaveForceInput, 1, Axis.Heave);
            float output = curved * settings.HeaveStrength * .01f;
            return (output, output);
        }


        private void CalculateForces(MotorSettings settings, CarSettings carSettings)
        {
            (lSurgeOutput, rSurgeOutput) = CalculateSurgeForces(settings);
            (lSwayOutput, rSwayOutput) = CalculateSwayForces(settings);

            if (carSettings.InvertHeave)
            {
                HeaveForceInput += 1;
            }
            else
            {
                HeaveForceInput -= 1;
                
            }
                (lHeaveOutput, rHeaveOutput) = CalculateHeaveForces(settings);

            if (carSettings.InvertSurge)
            {
                lSurgeOutput = -lSurgeOutput;
                rSurgeOutput = -rSurgeOutput;
            }

            if (carSettings.InvertSway) //cornering is sway
            {
                lSwayOutput = -lSwayOutput;
            }
            if (lSwayOutput < 0)
            {
                lSwayOutput = Math.Abs(lSwayOutput);
                rSwayOutput = 0;
            }
            else
            {
                rSwayOutput = Math.Abs(lSwayOutput);
                lSwayOutput = 0;
            }
            
           
            


            if (carSettings.InvertHeave)
            {
                
                lHeaveOutput = -lHeaveOutput;
                rHeaveOutput = -rHeaveOutput;
            }
        }

        public float CalculateDataForGraph(MotorSettings settings, CarSettings carSettings)
        {
            CalculateForces(settings, carSettings);
            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces
            return signalLeft;
        }

        public float CalculateDataToSerail(MotorSettings settings, CarSettings carSettings)
        {
            CalculateForces(settings, carSettings);

            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces

            float restingPointValue = (RestingPoint / 100f) ;

            (signalLeft, _) = settings.ClampToMaxMotorPower(signalLeft + restingPointValue, 0); //make sure its within range of motor
   
            return signalLeft;
        }
        /// <summary>
        /// Call this to calculate all forces that we will send to the belt
        /// this ussing the given motor settings and returns the motor forces
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public float SendDataToSerial(MotorSettings settings, BeltSerialDevice _serial, CarSettings carSettings)
        {
            CalculateForces(settings, carSettings);

            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces
            float signalRight = (rSurgeOutput * SurgeWeight) + (rSwayOutput * SwayWeight) + (rHeaveOutput * HeaveWeight); //add all forces

            float restingPointValue = (RestingPoint / 100f);

            (signalLeft, signalRight) = settings.ClampToMaxMotorPower(signalLeft+ restingPointValue, signalRight+ restingPointValue); //make sure its within range of motor

            if (settings.LeftInverted)
                signalLeft = settings.LeftMaximumAngle - signalLeft;
            if (settings.RightInverted)
                signalRight = settings.RightMaximumAngle - signalRight;

            _serial.SendValue(signalLeft, signalRight);
            return signalLeft;
        }



    }

}
