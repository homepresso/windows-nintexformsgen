using System;
using System.Data;
using Microsoft.Data.SqlClient;  // Use Microsoft.Data.SqlClient instead
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace FormGenerator.Services
{
    /// <summary>
    /// Service for managing SQL Server connections and operations
    /// </summary>
    public class SqlConnectionService
    {
        private string _connectionString;

        /// <summary>
        /// Builds a connection string based on authentication type with certificate trust option
        /// </summary>
        public string BuildConnectionString(string server, string database, bool useWindowsAuth,
            string username = null, string password = null, bool trustServerCertificate = true)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                ConnectTimeout = 30
            };

            if (useWindowsAuth)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = username;
                builder.Password = password;
                builder.IntegratedSecurity = false;
            }

            // Enable Multiple Active Result Sets for better performance
            builder.MultipleActiveResultSets = true;

            // Set application name for better monitoring
            builder.ApplicationName = "Nintex Forms Generator";

            // IMPORTANT: Handle SSL certificate trust issues
            // This is needed for SQL Server instances using self-signed certificates
            builder.TrustServerCertificate = trustServerCertificate;

            _connectionString = builder.ConnectionString;
            return _connectionString;
        }

        /// <summary>
        /// Tests the SQL connection and verifies database exists
        /// </summary>
        public async Task<(bool Success, string Message, string ServerVersion)> TestConnectionAsync(string connectionString = null)
        {
            var connStr = connectionString ?? _connectionString;

            if (string.IsNullOrEmpty(connStr))
            {
                return (false, "Connection string is not configured", null);
            }

            string serverVersion = null;  // Declare at method scope

            try
            {
                var builder = new SqlConnectionStringBuilder(connStr);
                var targetDatabase = builder.InitialCatalog;

                // First, try to connect to master database to check if SQL Server is accessible
                builder.InitialCatalog = "master";

                using (var masterConnection = new SqlConnection(builder.ConnectionString))
                {
                    await masterConnection.OpenAsync();

                    // Get server version
                    serverVersion = masterConnection.ServerVersion;

                    // Check if the target database exists
                    var checkDbQuery = @"
                        SELECT COUNT(*) 
                        FROM sys.databases 
                        WHERE name = @dbName";

                    using (var checkCommand = new SqlCommand(checkDbQuery, masterConnection))
                    {
                        checkCommand.Parameters.AddWithValue("@dbName", targetDatabase);
                        var dbCount = (int)await checkCommand.ExecuteScalarAsync();

                        if (dbCount == 0)
                        {
                            // Database doesn't exist
                            return (false, $"Database '{targetDatabase}' does not exist on server. Would you like to create it?", serverVersion);
                        }
                    }
                }

                // Now try to connect to the actual database
                using (var connection = new SqlConnection(connStr))
                {
                    await connection.OpenAsync();

                    // Test with a simple query to ensure we have proper permissions
                    using (var command = new SqlCommand("SELECT @@VERSION", connection))
                    {
                        var version = await command.ExecuteScalarAsync();

                        // Parse SQL Server version info
                        var versionStr = version?.ToString() ?? "";
                        var versionLines = versionStr.Split('\n');
                        var shortVersion = versionLines.Length > 0 ? versionLines[0] : serverVersion;

                        // Additional permission check - try to read table information
                        var permissionQuery = @"
                            SELECT COUNT(*) 
                            FROM INFORMATION_SCHEMA.TABLES 
                            WHERE TABLE_SCHEMA = 'dbo'";

                        using (var permCommand = new SqlCommand(permissionQuery, connection))
                        {
                            try
                            {
                                await permCommand.ExecuteScalarAsync();
                            }
                            catch
                            {
                                return (false, $"Connected to database '{targetDatabase}' but insufficient permissions", shortVersion);
                            }
                        }

                        return (true, $"Connection successful to database '{targetDatabase}'", shortVersion);
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                // Provide specific error messages based on SQL error number
                var errorMessage = sqlEx.Number switch
                {
                    -1 => "Connection timeout. Please check the server name and network connectivity.",
                    -2 => "Connection timeout. The server may be unreachable or the instance name may be incorrect.",
                    2 => "Server not found. Please verify the server name and instance.",
                    4060 => $"Cannot open database. The database does not exist or you don't have access.",
                    18456 => "Login failed. Please check your username and password.",
                    53 => "Network error. Cannot connect to SQL Server. Check if SQL Server is running and allows remote connections.",
                    _ => $"SQL Error {sqlEx.Number}: {sqlEx.Message}"
                };

                return (false, errorMessage, null);
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Checks if a database exists
        /// </summary>
        public async Task<bool> DatabaseExistsAsync(string databaseName)
        {
            if (string.IsNullOrEmpty(_connectionString))
                return false;

            try
            {
                // Create a connection to master database to check if target database exists
                var builder = new SqlConnectionStringBuilder(_connectionString)
                {
                    InitialCatalog = "master"
                };

                using (var connection = new SqlConnection(builder.ConnectionString))
                {
                    await connection.OpenAsync();

                    var query = "SELECT COUNT(*) FROM sys.databases WHERE name = @dbName";
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@dbName", databaseName);
                        var count = (int)await command.ExecuteScalarAsync();
                        return count > 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a database if it doesn't exist
        /// </summary>
        public async Task<(bool Success, string Message)> CreateDatabaseIfNotExistsAsync(string databaseName)
        {
            try
            {
                if (await DatabaseExistsAsync(databaseName))
                {
                    return (true, $"Database '{databaseName}' already exists");
                }

                // Connect to master to create the database
                var builder = new SqlConnectionStringBuilder(_connectionString)
                {
                    InitialCatalog = "master"
                };

                using (var connection = new SqlConnection(builder.ConnectionString))
                {
                    await connection.OpenAsync();

                    // Create database with proper settings
                    var createDbQuery = $@"
                        CREATE DATABASE [{databaseName}]
                        COLLATE SQL_Latin1_General_CP1_CI_AS";

                    using (var command = new SqlCommand(createDbQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }

                    // Set recovery model to Simple for dev/test environments
                    var setRecoveryQuery = $@"
                        ALTER DATABASE [{databaseName}] 
                        SET RECOVERY SIMPLE";

                    using (var command = new SqlCommand(setRecoveryQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                }

                return (true, $"Database '{databaseName}' created successfully");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to create database: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a SQL script
        /// </summary>
        public async Task<(bool Success, string Message, int RowsAffected)> ExecuteScriptAsync(string script)
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                return (false, "Connection string is not configured", 0);
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Split script by GO statements
                    var batches = SplitSqlBatches(script);
                    int totalRowsAffected = 0;

                    foreach (var batch in batches)
                    {
                        if (string.IsNullOrWhiteSpace(batch))
                            continue;

                        using (var command = new SqlCommand(batch, connection))
                        {
                            command.CommandTimeout = 60; // 60 seconds timeout
                            var rows = await command.ExecuteNonQueryAsync();
                            if (rows > 0)
                                totalRowsAffected += rows;
                        }
                    }

                    return (true, "Script executed successfully", totalRowsAffected);
                }
            }
            catch (SqlException sqlEx)
            {
                return (false, $"SQL Error: {sqlEx.Message}", 0);
            }
            catch (Exception ex)
            {
                return (false, $"Error executing script: {ex.Message}", 0);
            }
        }

        /// <summary>
        /// Checks if a table exists
        /// </summary>
        public async Task<bool> TableExistsAsync(string tableName, string schema = "dbo")
        {
            if (string.IsNullOrEmpty(_connectionString))
                return false;

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"
                        SELECT COUNT(*) 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_SCHEMA = @schema 
                        AND TABLE_NAME = @tableName";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@schema", schema);
                        command.Parameters.AddWithValue("@tableName", tableName);

                        var count = (int)await command.ExecuteScalarAsync();
                        return count > 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets list of tables in the database
        /// </summary>
        public async Task<List<string>> GetTablesAsync(string schema = "dbo")
        {
            var tables = new List<string>();

            if (string.IsNullOrEmpty(_connectionString))
                return tables;

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"
                        SELECT TABLE_NAME 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_SCHEMA = @schema 
                        AND TABLE_TYPE = 'BASE TABLE'
                        ORDER BY TABLE_NAME";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@schema", schema);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                tables.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch
            {
                // Return empty list on error
            }

            return tables;
        }

        /// <summary>
        /// Splits SQL script by GO statements
        /// </summary>
        private List<string> SplitSqlBatches(string script)
        {
            var batches = new List<string>();
            var lines = script.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var currentBatch = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Check if line is a GO statement
                if (trimmedLine.Equals("GO", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("GO ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("GO\t", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentBatch.Length > 0)
                    {
                        batches.Add(currentBatch.ToString());
                        currentBatch.Clear();
                    }
                }
                else
                {
                    currentBatch.AppendLine(line);
                }
            }

            // Add the last batch if it exists
            if (currentBatch.Length > 0)
            {
                batches.Add(currentBatch.ToString());
            }

            return batches;
        }

        /// <summary>
        /// Gets the database name from connection string
        /// </summary>
        private string GetDatabaseName(string connectionString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return builder.InitialCatalog;
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Gets the current connection string
        /// </summary>
        public string GetConnectionString()
        {
            return _connectionString;
        }
    }
}