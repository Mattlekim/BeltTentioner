using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Windows.Media;
using SharedResources;
namespace User.PluginSdkDemo
{
    [PluginDescription("Simple Belt Tensioner Plugin To Connect To The Custom Belt Tensioner App")]
    [PluginAuthor("Mattlekim")]
    [PluginName("Belt Tensioner")]
    public class DataPluginDemo : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public DataPluginDemoSettings Settings;

        /// <summary>
        /// Instance of the current plugin manager
        /// </summary>
        public PluginManager PluginManager { get; set; }

        /// <summary>
        /// Gets the left menu icon. Icon must be 24x24 and compatible with black and white display.
        /// </summary>
        public ImageSource PictureIcon => this.ToIcon(Properties.Resources.sdkmenuicon);

        /// <summary>
        /// Gets a short plugin title to show in left menu. Return null if you want to use the title as defined in PluginName attribute.
        /// </summary>
        public string LeftMenuTitle => "Belt Tensioner";

        TelemetryMmfWriter writer = new TelemetryMmfWriter();

        public void EnablePlugin()
        {
            if (writer == null)
            {
                writer = new TelemetryMmfWriter();
                
            }
        }

        public void DisablePlugin()
        {
            if (writer != null)
            {
                writer.Dispose();
                writer = null;
            }
        }   
        /// <summary>
        /// Called one time per game data update, contains all normalized game data,
        /// raw data are intentionally "hidden" under a generic object type (A plugin SHOULD NOT USE IT)
        ///
        /// This method is on the critical path, it must execute as fast as possible and avoid throwing any error
        ///
        /// </summary>
        /// <param name="pluginManager"></param>
        /// <param name="data">Current game data, including current and previous data frame.</param>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {

            // Define the value of our property (declared in init)
            if (data.RunningGameProcessDetected)
            {
                if (data.OldData != null && data.NewData != null)
                {
                    if (data.OldData != data.NewData)
                    {
                        TelemetrySharedData tsd = new TelemetrySharedData
                        {
                            GameName = data.GameName,
                            CarName = data.NewData.CarModel != null ? data.NewData.CarModel : "Unknown",
                            SupportBraking = data.NewData.AccelerationSurge != null,
                            SupportCornering = data.NewData.AccelerationSway != null,
                            SupportVertical = data.NewData.AccelerationHeave != null,
                            Braking = data.NewData.AccelerationSurge != null ? (float)data.NewData.AccelerationSurge : 0,
                            Cornering = data.NewData.AccelerationSway != null ? (float)data.NewData.AccelerationSway : 0,
                            Vertical = data.NewData.AccelerationHeave != null ? (float)data.NewData.AccelerationHeave : 0,
                            GameRunning = true,
                            Paused = data.GameInMenu || data.GamePaused,
                        };
                        writer.Write(tsd);
                    }
                    else
                    {
                        TelemetrySharedData tsd = new TelemetrySharedData
                        {
                            GameName = data.GameName,
                            CarName = data.NewData.CarModel != null ? data.NewData.CarModel : "Unknown",
                            Braking = 0,
                            Cornering = 0,
                            Vertical = 0,
                            SupportBraking = false,
                            SupportCornering = false,
                            SupportVertical = false,
                            GameRunning = true,
                            Paused = data.GameInMenu || data.GamePaused,
                        };
                        writer.Write(tsd);
                    }


                }
                else
                {
                    TelemetrySharedData tsd = new TelemetrySharedData
                    {
                        GameName = "None",
                        CarName = "None",
                        SupportBraking = false,
                        SupportCornering = false,
                        SupportVertical = false,
                        Braking = 0,
                        Cornering = 0,
                        Vertical = 0,
                        GameRunning = false,
                        Paused = data.GameInMenu || data.GamePaused,
                    };
                    writer.Write(tsd);
                }
            }
        }

        /// <summary>
        /// Called at plugin manager stop, close/dispose anything needed here !
        /// Plugins are rebuilt at game change
        /// </summary>
        /// <param name="pluginManager"></param>
        public void End(PluginManager pluginManager)
        {
            // Save settings
            this.SaveCommonSettings("GeneralSettings", Settings);
        }

        /// <summary>
        /// Returns the settings control, return null if no settings control is required
        /// </summary>
        /// <param name="pluginManager"></param>
        /// <returns></returns>
        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControlDemo(this);
        }

        /// <summary>
        /// Called once after plugins startup
        /// Plugins are rebuilt at game change
        /// </summary>
        /// <param name="pluginManager"></param>
        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("Starting plugin");

            // Load settings
            Settings = this.ReadCommonSettings<DataPluginDemoSettings>("GeneralSettings", () => new DataPluginDemoSettings());

            // Declare a property available in the property list, this gets evaluated "on demand" (when shown or used in formulas)
            this.AttachDelegate(name: "CurrentDateTime", valueProvider: () => DateTime.Now);


            // Declare an input which can be mapped, inputs are meant to be keeping state of the source inputs,
            // they won't trigger on inputs not capable of "holding" their state.
            // Internally they work similarly to AddAction, but are restricted to a "during" behavior
            this.AddInputMapping(
                inputName: "InputPressed",
                inputPressed: (a, b) => {/* One of the mapped input has been pressed   */},
                inputReleased: (a, b) => {/* One of the mapped input has been released */}
            );
        }
    }
}