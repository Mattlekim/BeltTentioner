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
        private (float, float) CalculateSurgeForces(BeltSerialDevice device, CarSettings carSettings)
        {
            float curved = device.DeviceMotorSettings.CalculateCurve(SurgeForceInput, carSettings.SurgeCurveAmount, Axis.Surge);
            float output = curved * carSettings.SurgeStrenght * .01f;
            return (output, output);
        }

        private (float, float) CalculateSwayForces(BeltSerialDevice device, CarSettings carSettings)
        {
            float curved = device.DeviceMotorSettings.CalculateCurve(SwayForceInput, carSettings.SwayCurveAmount, Axis.Sway);
            float output = curved * carSettings.SwayStrength * .01f;
            return (output, output);
        }

        private (float, float) CalculateHeaveForces(BeltSerialDevice device, CarSettings carSettings)
        {
         
            float curved = device.DeviceMotorSettings.CalculateCurve(HeaveForceInput, 1, Axis.Heave);
            float output = curved * carSettings.HeaveStrength * .01f;
            return (output, output);
        }


        private void CalculateForces(BeltSerialDevice device, CarSettings carSettings)
        {
          //  carSettings.HeaveStrength = 1;
            (lSurgeOutput, rSurgeOutput) = CalculateSurgeForces(device, carSettings);
            (lSwayOutput, rSwayOutput) = CalculateSwayForces(device, carSettings);

            if (carSettings.InvertHeave)
            {
                HeaveForceInput = -HeaveForceInput;
                HeaveForceInput += 1;
            }
            else
            {
                HeaveForceInput -= 1;

            }
            (lHeaveOutput, rHeaveOutput) = CalculateHeaveForces(device, carSettings);

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

        public float CalculateDataForGraph(BeltSerialDevice device, CarSettings carSettings)
        {
            CalculateForces(device, carSettings);
            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces
            return signalLeft;
        }

        public float CalculateDataToSerail(BeltSerialDevice device, CarSettings carSettings)
        {
            CalculateForces(device, carSettings);

            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces

            float restingPointValue = (RestingPoint / 100f) ;

            (signalLeft, _) = device.DeviceMotorSettings.ClampToMaxMotorPower(signalLeft + restingPointValue, 0, carSettings); //make sure its within range of motor
   
            return signalLeft;
        }

     

        /// <summary>
        /// Call this to calculate all forces that we will send to the belt
        /// this ussing the given motor settings and returns the motor forces
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public float SendDataToSerial(BeltSerialDevice device, CarSettings carSettings)
        {
            
            CalculateForces(device, carSettings);

            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces
            float signalRight = (rSurgeOutput * SurgeWeight) + (rSwayOutput * SwayWeight) + (rHeaveOutput * HeaveWeight); //add all forces

            float restingPointValue = (RestingPoint / 100f);

            (signalLeft, signalRight) = device.DeviceMotorSettings.ClampToMaxMotorPower(signalLeft+ restingPointValue, signalRight+ restingPointValue, carSettings); //make sure its within range of motor

            if (device.DeviceMotorSettings.LeftInverted)
                signalLeft = device.DeviceMotorSettings.LeftMaximumAngle - signalLeft;
            if (device.DeviceMotorSettings.RightInverted)
                signalRight = device.DeviceMotorSettings.RightMaximumAngle - signalRight;

            device.SendValue(signalLeft, signalRight);
            return signalLeft;
        }



    }

}
