using System;
using SourceCode.SmartObjects.Management;

namespace K2SmartObjectGenerator.Utilities
{
    /// <summary>
    /// Manages connections to K2 SmartObject Management Server
    /// </summary>
    public class ServerConnectionManager : IDisposable
    {
        private readonly string _serverName;
        private readonly uint _port;
        private SmartObjectManagementServer? _managementServer;

        public ServerConnectionManager(string serverName, uint port)
        {
            _serverName = serverName ?? throw new ArgumentNullException(nameof(serverName));
            _port = port;
        }

        /// <summary>
        /// Gets the K2 SmartObject Management Server instance
        /// </summary>
        public SmartObjectManagementServer? ManagementServer => _managementServer;

        /// <summary>
        /// Connects to the K2 SmartObject Management Server
        /// </summary>
        public void Connect()
        {
            try
            {
                _managementServer = new SmartObjectManagementServer();

                // Create connection string
                var connectionString = $"Integrated=True;IsPrimaryLogin=True;Authenticate=True;EncryptedPassword=False;Host={_serverName};Port={_port}";

                _managementServer.CreateConnection();
                _managementServer.Connection.Open(connectionString);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to connect to K2 server at {_serverName}:{_port}", ex);
            }
        }

        /// <summary>
        /// Disconnects from the K2 SmartObject Management Server
        /// </summary>
        public void Disconnect()
        {
            if (_managementServer?.Connection != null)
            {
                try
                {
                    if (_managementServer.Connection.IsConnected)
                    {
                        _managementServer.Connection.Close();
                    }
                }
                catch
                {
                    // Suppress errors during disconnect
                }
            }
        }

        public void Dispose()
        {
            Disconnect();
            _managementServer = null;
        }
    }
}
