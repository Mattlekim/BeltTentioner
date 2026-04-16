using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeltAPI
{
    public struct Rotation
    {
        public float Pitch { get; set; }
        public float Roll { get; set; }
        public float Yaw { get; set; }


        public Rotation(float pitch, float roll, float yaw)
        {
            Pitch = pitch;
            Roll = roll;
            Yaw = yaw;
        }

        public (float surge, float sway, float heave) GravityVector(float gravity = 1f)
        {
            float sinP = (float)Math.Sin(Pitch);
            float cosP = (float)Math.Cos(Pitch);
            float sinR = (float)Math.Sin(Roll);
            float cosR = (float)Math.Cos(Roll);

            // Rotate world gravity (0, -gravity, 0) by pitch and roll only
            // Yaw doesn't affect gravity distribution in the car's local frame
            return (
                surge:  gravity * sinP,          // nose-up pitch pushes gravity forward
                sway:  -gravity * cosP * sinR,   // roll pushes gravity sideways
                heave: -gravity * cosP * cosR    // straight down when level = -1g
            );
        }

        public static Rotation Zero => new Rotation(0, 0, 0);

    }

    public struct Vector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3 Transform(Rotation rotation)
        {
            float sinP = (float)Math.Sin(rotation.Pitch);
            float cosP = (float)Math.Cos(rotation.Pitch);
            float sinR = (float)Math.Sin(rotation.Roll);
            float cosR = (float)Math.Cos(rotation.Roll);
            float sinY = (float)Math.Sin(rotation.Yaw);
            float cosY = (float)Math.Cos(rotation.Yaw);

            // Yaw -> Pitch -> Roll
            return new Vector3(
                cosY * cosP * X + (cosY * sinP * sinR - sinY * cosR) * Y + (cosY * sinP * cosR + sinY * sinR) * Z,
                sinY * cosP * X + (sinY * sinP * sinR + cosY * cosR) * Y + (sinY * sinP * cosR - cosY * sinR) * Z,
                       -sinP * X +                        cosP * sinR  * Y +                        cosP * cosR  * Z
            );
        }

        
    }
}
