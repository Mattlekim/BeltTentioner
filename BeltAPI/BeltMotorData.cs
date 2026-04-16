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

        public float ExperimentalSway; // 0-100: how much negative sway is applied to the opposite motor

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

        public Rotation CarRotation;
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


        private (float, float) CalculateRollForces(CarSettings carSettings, float roll)
        {
            float output = (float)Math.Sin(carSettings.InvertRoll? -roll: roll) * carSettings.RollStrength * .01f;
            return (output, output);
        }

        private (float, float) CalculatePitchForces(CarSettings carSettings, float pitch)
        {
            float output = (float)Math.Sin(carSettings.InvertPitch ? -pitch : pitch) * carSettings.PitchStrength * .01f;
            return (output, output);
        }

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


        private void CalculateForces(BeltSerialDevice device, CarSettings carSettings, bool removeGravity, Rotation carRotation)
        {

            if (removeGravity)
            {
                var (gravitySurge, gravitySway, gravityHeave) = carRotation.GravityVector();

                // gravityHeave is negative when upright (~-1g), so subtracting it removes gravity
                HeaveForceInput -= Math.Abs(gravityHeave);
                SwayForceInput -= Math.Abs(gravitySway);
                SurgeForceInput -= Math.Abs(gravitySurge);
            }

            if (carSettings.InvertHeave)
                HeaveForceInput = -HeaveForceInput;

            
             

            //  carSettings.HeaveStrength = 1;
            (lSurgeOutput, rSurgeOutput) = CalculateSurgeForces(device, carSettings);
            (lSwayOutput, rSwayOutput) = CalculateSwayForces(device, carSettings);


            float lRollOutput, rRollOutput;

            (lRollOutput, rRollOutput) = CalculateRollForces(carSettings, carRotation.Roll);
            lSwayOutput += lRollOutput;
            rSwayOutput += rRollOutput;

            float lPitchOutput, rPitchOutput;
            (lPitchOutput, rPitchOutput) = CalculatePitchForces(carSettings, carRotation.Pitch);
            lSurgeOutput += lPitchOutput;
            rSurgeOutput += rPitchOutput;

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
                rSwayOutput = -lSwayOutput * (ExperimentalSway / 100f);
            }
            else
            {
                rSwayOutput = Math.Abs(lSwayOutput);
                lSwayOutput = -rSwayOutput * (ExperimentalSway / 100f);
            }
        }

        public float CalculateDataForGraph(BeltSerialDevice device, CarSettings carSettings, bool removeGravity, Rotation carRotation = default)
        {
            CalculateForces(device, carSettings, removeGravity, carRotation);
            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces
            return signalLeft;
        }

        public float CalculateDataToSerail(BeltSerialDevice device, CarSettings carSettings, Rotation carRotation = default)
        {
            CalculateForces(device, carSettings, true, carRotation);

            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces

            float restingPointValue = (RestingPoint / 100f) ;

            (signalLeft, _) = device.DeviceMotorSettings.ClampToMaxMotorPower(signalLeft + restingPointValue, 0, carSettings); //make sure its within range of motor
   
            return signalLeft;
        }


        private (float, float) _lastMotorDataSent;

        public (float, float) GetLastMotorDataSent()
        {
            return _lastMotorDataSent;
        }
        /// <summary>
        /// Call this to calculate all forces that we will send to the belt
        /// this ussing the given motor settings and returns the motor forces
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public float SendDataToSerial(BeltSerialDevice device, CarSettings carSettings, bool removeGravity = false, Rotation carRotation = default)
        {
            
            CalculateForces(device, carSettings, removeGravity, carRotation);

            float signalLeft = (lSurgeOutput * SurgeWeight) + (lSwayOutput * SwayWeight) + (lHeaveOutput * HeaveWeight); //add all forces
            float signalRight = (rSurgeOutput * SurgeWeight) + (rSwayOutput * SwayWeight) + (rHeaveOutput * HeaveWeight); //add all forces

            float restingPointValue = (RestingPoint / 100f);

            (signalLeft, signalRight) = device.DeviceMotorSettings.ClampToMaxMotorPower(signalLeft+ restingPointValue, signalRight+ restingPointValue, carSettings); //make sure its within range of motor

            if (device.DeviceMotorSettings.LeftInverted)
                signalLeft = device.DeviceMotorSettings.LeftMaximumAngle - signalLeft;
            if (device.DeviceMotorSettings.RightInverted)
                signalRight = device.DeviceMotorSettings.RightMaximumAngle - signalRight;

            signalLeft = (float)Math.Round(signalLeft, 1);
            signalRight = (float)Math.Round(signalRight, 1);

            _lastMotorDataSent = (signalLeft, signalRight);

            device.SendValue(signalLeft, signalRight);
            return signalLeft;
        }



    }

}
