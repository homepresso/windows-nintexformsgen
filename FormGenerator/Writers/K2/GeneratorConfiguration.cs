using System;

namespace K2SmartObjectGenerator.Config
{
    /// <summary>
    /// Configuration for K2 SmartObject, View, and Form generation
    /// </summary>
    public class GeneratorConfiguration
    {
        public GeneratorConfiguration()
        {
            Server = new ServerConfiguration();
            Form = new FormConfiguration();
            K2 = new K2Configuration();
        }

        public ServerConfiguration Server { get; set; }
        public FormConfiguration Form { get; set; }
        public K2Configuration K2 { get; set; }
    }

    /// <summary>
    /// K2 server connection configuration
    /// </summary>
    public class ServerConfiguration
    {
        public string HostName { get; set; } = "localhost";
        public uint Port { get; set; } = 5555;
    }

    /// <summary>
    /// Form generation configuration
    /// </summary>
    public class FormConfiguration
    {
        public string Theme { get; set; } = "Default";
        public bool UseTimestamp { get; set; } = false;
        public bool ForceCleanup { get; set; } = false;
        public string TargetFolder { get; set; } = "Generated";
    }

    /// <summary>
    /// K2-specific configuration
    /// </summary>
    public class K2Configuration
    {
        public string SmartBoxGuid { get; set; } = "e5609413-d844-4325-98c3-db3cacbd406d";
    }
}
