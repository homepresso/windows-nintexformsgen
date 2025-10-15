using SourceCode.SmartObjects.Management;
using SourceCode.Hosting.Client.BaseAPI;

namespace K2SmartObjectGenerator.Utilities
{
    public class ServerConnectionManager
    {
        private SmartObjectManagementServer _smoManagementServer;
        private SCConnectionStringBuilder _connectionString;
        private readonly string _serverName;
        private readonly uint _serverPort;

        public ServerConnectionManager(string serverName = "localhost", uint serverPort = 5555)
        {
            _serverName = serverName;
            _serverPort = serverPort;
            InitializeConnectionString();
        }

        public SCConnectionStringBuilder ConnectionString => _connectionString;

        private void InitializeConnectionString()
        {
            _connectionString = new SCConnectionStringBuilder();
            _connectionString.Host = _serverName;
            _connectionString.Port = _serverPort;
            _connectionString.IsPrimaryLogin = true;
            _connectionString.Integrated = true;
        }

        public void Connect()
        {
            if (_smoManagementServer == null)
            {
                _smoManagementServer = new SmartObjectManagementServer();
            }
            if (_smoManagementServer.Connection == null)
            {
                _smoManagementServer.CreateConnection();
            }
            if (!_smoManagementServer.Connection.IsConnected)
            {
                _smoManagementServer.Connection.Open(_connectionString.ConnectionString);
            }
        }

        public void Disconnect()
        {
            if (_smoManagementServer != null)
            {
                if (_smoManagementServer.Connection != null)
                {
                    if (_smoManagementServer.Connection.IsConnected)
                    {
                        _smoManagementServer.Connection.Close();
                    }
                }
            }
        }

        public SmartObjectManagementServer ManagementServer
        {
            get
            {
                Connect();
                return _smoManagementServer;
            }
        }
    }
}