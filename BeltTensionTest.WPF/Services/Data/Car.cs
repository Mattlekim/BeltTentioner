using System;
using IRSDKSharper;

namespace BeltTensionTest.WPF.Services.Data
{
    /// <summary>
    /// Live data for one car in the session, collected from the iRacing SDK.
    /// Per-car telemetry comes from the CarIdx* array vars (read at this car's
    /// index via IRacingSdkDatum, same pattern as IracingService); identity and
    /// incident data come from the session info YAML (DriverInfo.Drivers).
    /// Call <see cref="Update"/> once per telemetry tick.
    /// </summary>
    public sealed class Car
    {
        // CarIdxTrackSurface values (irsdk TrkLoc enum).
        private const int SurfaceNotInWorld = -1;
        private const int SurfaceInPitStall = 1;

        public int CarIdx { get; private set; } = -1;

        // ----- Session info (YAML) -----
        public string DriverName { get; private set; } = string.Empty;
        public string CarName { get; private set; } = string.Empty;
        public string CarClass { get; private set; } = string.Empty;

        /// <summary>iRacing car class id (CarClassID); -1 until session info arrives.</summary>
        public int CarClassId { get; private set; } = -1;

        /// <summary>
        /// Reference lap time for this car's class (CarClassEstLapTime). This
        /// is the lap-time scale CarIdxEstTime is computed against, so use it
        /// (not a driver's best lap) when wrap-correcting EstTime deltas.
        /// </summary>
        public float ClassEstLapTime { get; private set; }

        /// <summary>Technical incident points of the current driver (CurDriverIncidentCount).</summary>
        public int IncidentPoints { get; private set; }

        // ----- Telemetry (CarIdx* arrays) -----
        public int Gear { get; private set; }
        public int Lap { get; private set; }
        public int LapCompleted { get; private set; }
        public int Position { get; private set; }
        public int ClassPosition { get; private set; }

        /// <summary>Tire compound in use (CarIdxTireCompound); -1 when the series does not report it.</summary>
        public int TireCompound { get; private set; } = -1;

        public float LastLapTime { get; private set; }
        public float BestLapTime { get; private set; }

        /// <summary>
        /// Race gap behind the session leader in seconds (CarIdxF2Time). In
        /// practice/qualifying iRacing fills this with best-lap deltas, so use
        /// <see cref="BestLapTime"/> comparisons there instead.
        /// </summary>
        public float F2Time { get; private set; }

        /// <summary>Estimated time to reach this point on track from start/finish (CarIdxEstTime).</summary>
        public float EstTime { get; private set; }

        /// <summary>True while the car is on pit road (CarIdxOnPitRoad).</summary>
        public bool OnPitRoad { get; private set; }

        /// <summary>Fraction of the lap completed, 0..1 (CarIdxLapDistPct).</summary>
        public float LapDistPct { get; private set; }

        /// <summary>
        /// True while the car sits in the garage. Only the player's car reports
        /// this directly (IsInGarage); for other cars it falls back to
        /// "not in world" from CarIdxTrackSurface.
        /// </summary>
        public bool IsInGarage { get; private set; }

        public bool IsOnTrack { get; private set; }

        private IRacingSdkDatum? _datumGear;
        private IRacingSdkDatum? _datumLap;
        private IRacingSdkDatum? _datumLapCompleted;
        private IRacingSdkDatum? _datumPosition;
        private IRacingSdkDatum? _datumClassPosition;
        private IRacingSdkDatum? _datumLastLapTime;
        private IRacingSdkDatum? _datumBestLapTime;
        private IRacingSdkDatum? _datumF2Time;
        private IRacingSdkDatum? _datumEstTime;
        private IRacingSdkDatum? _datumOnPitRoad;
        private IRacingSdkDatum? _datumLapDistPct;
        private IRacingSdkDatum? _datumTrackSurface;
        private IRacingSdkDatum? _datumTireCompound;   // not present in every session
        private IRacingSdkDatum? _datumIsInGarage;     // player car only
        private IRacingSdkDatum? _datumIsOnTrack;      // player car only
        private bool _datumsReady;

        /// <summary>Forget cached datums; call on SDK disconnect so a new session re-resolves them.</summary>
        public void Reset()
        {
            _datumsReady = false;
            CarIdx = -1;
        }

        private bool SetupDatums(IRacingSdk sdk)
        {
            if (_datumsReady) return true;

            try
            {
                var vars = sdk.Data.TelemetryDataProperties;
                _datumGear = vars["CarIdxGear"];
                _datumLap = vars["CarIdxLap"];
                _datumLapCompleted = vars["CarIdxLapCompleted"];
                _datumPosition = vars["CarIdxPosition"];
                _datumClassPosition = vars["CarIdxClassPosition"];
                _datumLastLapTime = vars["CarIdxLastLapTime"];
                _datumBestLapTime = vars["CarIdxBestLapTime"];
                _datumF2Time = vars["CarIdxF2Time"];
                _datumEstTime = vars["CarIdxEstTime"];
                _datumOnPitRoad = vars["CarIdxOnPitRoad"];
                _datumLapDistPct = vars["CarIdxLapDistPct"];
                _datumTrackSurface = vars["CarIdxTrackSurface"];
                _datumIsInGarage = vars["IsInGarage"];
                _datumIsOnTrack = vars["IsOnTrack"];
                try { _datumTireCompound = vars["CarIdxTireCompound"]; } catch { _datumTireCompound = null; }

                _datumsReady = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Pull this car's current values out of the SDK.</summary>
        public void Update(IRacingSdk sdk, int carIdx)
        {
            if (carIdx < 0 || !SetupDatums(sdk)) return;
            CarIdx = carIdx;

            // Identity from the session YAML. Drivers are keyed by CarIdx, not
            // list position, so find the matching entry.
            try
            {
                var driverInfo = sdk.Data.SessionInfo?.DriverInfo;
                if (driverInfo != null)
                {
                    foreach (var d in driverInfo.Drivers)
                    {
                        if (d.CarIdx != carIdx) continue;
                        DriverName = d.UserName ?? string.Empty;
                        CarName = d.CarScreenName ?? string.Empty;
                        CarClass = d.CarClassShortName ?? string.Empty;
                        CarClassId = d.CarClassID;
                        ClassEstLapTime = d.CarClassEstLapTime;
                        IncidentPoints = d.CurDriverIncidentCount;
                        break;
                    }
                }
            }
            catch { }

            try
            {
                Gear = sdk.Data.GetInt(_datumGear, carIdx);
                Lap = sdk.Data.GetInt(_datumLap, carIdx);
                LapCompleted = sdk.Data.GetInt(_datumLapCompleted, carIdx);
                Position = sdk.Data.GetInt(_datumPosition, carIdx);
                ClassPosition = sdk.Data.GetInt(_datumClassPosition, carIdx);
                LastLapTime = sdk.Data.GetFloat(_datumLastLapTime, carIdx);
                BestLapTime = sdk.Data.GetFloat(_datumBestLapTime, carIdx);
                F2Time = sdk.Data.GetFloat(_datumF2Time, carIdx);
                EstTime = sdk.Data.GetFloat(_datumEstTime, carIdx);
                OnPitRoad = sdk.Data.GetBool(_datumOnPitRoad, carIdx);
                LapDistPct = sdk.Data.GetFloat(_datumLapDistPct, carIdx);
                if (_datumTireCompound != null)
                    TireCompound = sdk.Data.GetInt(_datumTireCompound, carIdx);

                int surface = sdk.Data.GetInt(_datumTrackSurface, carIdx);
                bool isPlayerCar = sdk.Data.SessionInfo?.DriverInfo?.DriverCarIdx == carIdx;
                if (isPlayerCar)
                {
                    IsInGarage = sdk.Data.GetBool(_datumIsInGarage);
                    IsOnTrack = sdk.Data.GetBool(_datumIsOnTrack);
                }
                else
                {
                    IsInGarage = surface == SurfaceNotInWorld;
                    IsOnTrack = surface != SurfaceNotInWorld && surface != SurfaceInPitStall;
                }
            }
            catch { }
        }
    }
}
