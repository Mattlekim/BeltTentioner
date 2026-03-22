namespace User.PluginSdkDemo
{
    /// <summary>
    /// Settings class, make sure it can be correctly serialized using JSON.net
    /// </summary>
    public class DataPluginDemoSettings
    {
        public bool Enabled { get; set; } = true;

        
    }
}