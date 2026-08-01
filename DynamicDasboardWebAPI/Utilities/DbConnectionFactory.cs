using DynamicDashboardCommon.Enums;
using DynamicDashboardCommon.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;

#region NAMESPACE_AND_DEPENDENCIES
namespace DynamicDasboardWebAPI.Utilities
{
    #endregion

    public class DbConnectionFactory
    {
        #region FIELDS

        private readonly IDbConnection _appDbConnection;
        private readonly IConfiguration _configuration;
        private readonly ConcurrentDictionary<int, string> _connectionStringCache;
        private readonly ConcurrentDictionary<int, string> _databaseTypeCache;

        #endregion

        #region CONSTRUCTOR

        public DbConnectionFactory(
            IDbConnection appDbConnection,
            IConfiguration configuration)
        {
            _appDbConnection = appDbConnection;
            _configuration = configuration;

            _connectionStringCache = new ConcurrentDictionary<int, string>();
            _databaseTypeCache = new ConcurrentDictionary<int, string>();
        }

        #endregion

        #region CREATE_CONNECTION_METHODS

        /// <summary>
        /// Creates a database connection based on the database ID.
        /// </summary>
        /// <param name="databaseId">The ID of the database to connect to</param>
        /// <returns>An open IDbConnection</returns>
        public IDbConnection CreateConnection(int databaseId)
        {
            IDbConnection connection = null;
            try
            {
                var (connectionString, databaseType) = GetConnectionInfo(databaseId);
                connection = BuildConnection((EnumDatabaseType)databaseType, connectionString);

                if (connection is DbConnection dbConnection)
                {
                    dbConnection.Open();
                }
                else
                {
                    connection.Open();
                }

                return connection;
            }
            catch (Exception)
            {
                connection?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates and opens a database connection asynchronously.
        /// </summary>
        /// <param name="databaseId">The ID of the database to connect to</param>
        /// <returns>A Task with the open IDbConnection</returns>
        public async Task<IDbConnection> CreateOpenConnectionAsync(int databaseId)
        {
            var (connectionString, databaseType) = GetConnectionInfo(databaseId);

            if (string.IsNullOrWhiteSpace(connectionString))
                return null;

            IDbConnection connection = null;

            try
            {
                connection = BuildConnection((EnumDatabaseType)databaseType, connectionString);

                if (connection is DbConnection dbConnection)
                {
                    await dbConnection.OpenAsync();
                }
                else
                {
                    // Dapper + IDbConnection doesn't have native async open
                    await Task.Run(() => connection.Open());
                }

                return connection;
            }
            catch
            {
                connection?.Dispose();
                throw;
            }
        }

        #endregion

        #region TEST_CONNECTION_METHODS

        /// <summary>
        /// Tests a database connection asynchronously.
        /// </summary>
        /// <param name="databaseId">The ID of the database to test</param>
        /// <returns>True if connection succeeds</returns>
        public async Task<bool> TestConnectionAsync(int databaseId)
        {
            try
            {
                using var connection = await CreateOpenConnectionAsync(databaseId);
                return true;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Tests a database connection using explicit connection parameters.
        /// </summary>
        /// <param name="database">Database configuration object</param>
        /// <param name="connectionString">Optional connection string (if not provided, will be built)</param>
        /// <returns>True if connection succeeds</returns>
        public async Task<bool> TestConnectionAsync(Database database, string connectionString = null)
        {
            if (database == null)
                return false;

            try
            {
                if (string.IsNullOrEmpty(connectionString))
                {
                    connectionString = BuildConnectionString(database);
                }

                using IDbConnection connection = BuildConnection((EnumDatabaseType)database.TypeID, connectionString);

                if (connection == null)
                    return false;

                // Open connection
                if (connection is DbConnection dbConnection)
                {
                    await dbConnection.OpenAsync();
                }
                else
                {
                    await Task.Run(() => connection.Open());
                }

                // Execute simple query to verify connection is truly working
                string testQuery = database.TypeID switch
                {
                    (int)EnumDatabaseType.Oracle => "SELECT 1 FROM DUAL",
                    _ => "SELECT 1"
                };

                await connection.ExecuteAsync(testQuery);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection test failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region BUILD_CONNECTION_AND_STRING_METHODS

        /// <summary>
        /// Builds a connection string from a Database object
        /// </summary>
        /// <param name="database">Database configuration object</param>
        /// <returns>Connection string</returns>
        public string BuildConnectionString(Database database)
        {
            try
            {
                if (database == null)
                    return string.Empty;

                // Use existing connection string if provided
                if (!string.IsNullOrWhiteSpace(database.ConnectionString))
                    return database.ConnectionString;

                // Build the connection string based on database type
                return database.TypeID switch
                {
                    (int)EnumDatabaseType.SQLServer => BuildSqlServerConnectionString(database),
                    (int)EnumDatabaseType.MySQL => BuildMySqlConnectionString(database),
                    (int)EnumDatabaseType.PostgreSQL => BuildPostgreSqlConnectionString(database),
                    (int)EnumDatabaseType.Oracle => BuildOracleConnectionString(database),
                    _ => string.Empty
                };
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Builds an IDbConnection instance for the specified database type
        /// </summary>
        /// <param name="dbType">Database type enum</param>
        /// <param name="connectionString">Connection string</param>
        /// <returns>IDbConnection instance</returns>
        public IDbConnection BuildConnection(EnumDatabaseType dbType, string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return null;
            }

            return dbType switch
            {
                EnumDatabaseType.SQLServer => new SqlConnection(connectionString),
                EnumDatabaseType.MySQL => new MySqlConnection(connectionString),
                EnumDatabaseType.PostgreSQL => new NpgsqlConnection(connectionString),
                EnumDatabaseType.Oracle => new OracleConnection(connectionString),
                _ => null
            };
        }

        /// <summary>
        /// Gets list of databases from a server using connection details
        /// </summary>
        /// <param name="serverInfo">Server configuration</param>
        /// <returns>List of database names</returns>
        public async Task<List<string>> GetDatabasesOnServerAsync(Database serverInfo)
        {
            var databases = new List<string>();

            if (serverInfo == null)
                return databases;

            try
            {
                // Build connection string WITHOUT specific database (connect to server)
                string connectionString = BuildServerConnectionString(serverInfo);

                using IDbConnection connection = BuildConnection((EnumDatabaseType)serverInfo.TypeID, connectionString);

                if (connection is DbConnection dbConnection)
                {
                    await dbConnection.OpenAsync();
                }
                else
                {
                    await Task.Run(() => connection.Open());
                }

                string query = serverInfo.TypeID switch
                {
                    (int)EnumDatabaseType.SQLServer => @"
                SELECT name FROM sys.databases 
                WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb')
                AND state = 0 
                ORDER BY name",

                    (int)EnumDatabaseType.MySQL => @"
                SELECT SCHEMA_NAME as name 
                FROM information_schema.SCHEMATA 
                WHERE SCHEMA_NAME NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
                ORDER BY SCHEMA_NAME",

                    (int)EnumDatabaseType.PostgreSQL => @"
                SELECT datname as name 
                FROM pg_database 
                WHERE datistemplate = false 
                AND datname NOT IN ('postgres', 'template0', 'template1')
                ORDER BY datname",

                    (int)EnumDatabaseType.Oracle => @"
                SELECT username as name 
                FROM all_users 
                WHERE username NOT IN ('SYS', 'SYSTEM', 'DBSNMP', 'SYSMAN', 'OUTLN', 'MDSYS', 'ORDSYS', 'EXFSYS', 'WMSYS', 'APPQOSSYS', 'APEX_030200', 'OWBSYS_AUDIT', 'ORDDATA', 'CTXSYS', 'ANONYMOUS', 'XDB', 'ORDPLUGINS', 'OWBSYS', 'SI_INFORMTN_SCHEMA', 'OLAPSYS', 'ORACLE_OCM', 'XS$NULL', 'MDDATA', 'DIP', 'APEX_PUBLIC_USER', 'SPATIAL_CSW_ADMIN_USR', 'SPATIAL_WFS_ADMIN_USR')
                ORDER BY username",

                    _ => throw new NotSupportedException($"Database type {serverInfo.TypeID} not supported")
                };

                var result = await connection.QueryAsync<string>(query);
                databases = result.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting databases: {ex.Message}");
            }

            return databases;
        }

        /// <summary>
        /// Builds a connection string to connect to server without specific database
        /// </summary>
        private string BuildServerConnectionString(Database serverInfo)
        {
            switch (serverInfo.TypeID)
            {
                case (int)EnumDatabaseType.SQLServer:
                    var sqlBuilder = new SqlConnectionStringBuilder
                    {
                        DataSource = serverInfo.Port > 0 && serverInfo.Port != 1433
                            ? $"{serverInfo.ServerAddress},{serverInfo.Port}"
                            : serverInfo.ServerAddress,
                        InitialCatalog = "master",
                        ConnectTimeout = 30,
                        TrustServerCertificate = true
                    };

                    string sqlPassword = DecryptCredentials(serverInfo.EncryptedCredentials);
                    if (string.IsNullOrEmpty(serverInfo.Username))
                    {
                        sqlBuilder.IntegratedSecurity = true;
                    }
                    else
                    {
                        sqlBuilder.UserID = serverInfo.Username;
                        sqlBuilder.Password = sqlPassword;
                        sqlBuilder.IntegratedSecurity = false;
                    }
                    return sqlBuilder.ConnectionString;

                case (int)EnumDatabaseType.MySQL:
                    var mysqlBuilder = new MySqlConnectionStringBuilder
                    {
                        Server = serverInfo.ServerAddress,
                        Port = serverInfo.Port > 0 ? (uint)serverInfo.Port : 3306,
                        ConnectionTimeout = 30,
                        SslMode = MySqlSslMode.Preferred
                    };

                    string mysqlPassword = DecryptCredentials(serverInfo.EncryptedCredentials);
                    if (!string.IsNullOrEmpty(serverInfo.Username))
                    {
                        mysqlBuilder.UserID = serverInfo.Username;
                        mysqlBuilder.Password = mysqlPassword;
                    }
                    return mysqlBuilder.ConnectionString;

                case (int)EnumDatabaseType.PostgreSQL:
                    var pgBuilder = new NpgsqlConnectionStringBuilder
                    {
                        Host = serverInfo.ServerAddress,
                        Port = serverInfo.Port > 0 ? serverInfo.Port : 5432,
                        Database = "postgres",
                        Timeout = 30,
                        Pooling = true,
                        MinPoolSize = 5,
                        MaxPoolSize = 100,
                        SslMode = serverInfo.SslMode?.ToLower() switch
                        {
                            "none" => SslMode.Disable,
                            "allow" => SslMode.Allow,
                            "prefer" => SslMode.Prefer,
                            "require" => SslMode.Require,
                            "verify-ca" => SslMode.VerifyCA,
                            "verify-full" => SslMode.VerifyFull,
                            _ => SslMode.Prefer
                        }
                    };

                    string pgPassword = DecryptCredentials(serverInfo.EncryptedCredentials);
                    if (!string.IsNullOrEmpty(serverInfo.Username))
                    {
                        pgBuilder.Username = serverInfo.Username;
                        pgBuilder.Password = pgPassword;
                    }
                    return pgBuilder.ConnectionString;

                case (int)EnumDatabaseType.Oracle:
                    var oracleBuilder = new OracleConnectionStringBuilder
                    {
                        DataSource = serverInfo.Port > 0
                            ? $"{serverInfo.ServerAddress}:{serverInfo.Port}"
                            : $"{serverInfo.ServerAddress}:1521",
                        ConnectionTimeout = 30
                    };

                    string oraclePassword = DecryptCredentials(serverInfo.EncryptedCredentials);
                    if (!string.IsNullOrEmpty(serverInfo.Username))
                    {
                        oracleBuilder.UserID = serverInfo.Username;
                        oracleBuilder.Password = oraclePassword;
                    }
                    return oracleBuilder.ConnectionString;

                default:
                    throw new NotSupportedException($"Database type {serverInfo.TypeID} not supported");
            }
        }

        #endregion

        #region CACHE_METHODS

        /// <summary>
        /// Clears the connection string cache
        /// </summary>
        public void ClearCache()
        {
            _connectionStringCache.Clear();
            _databaseTypeCache.Clear();
        }

        #endregion

        #region EXECUTE_WITH_CONNECTION_METHODS

        /// <summary>
        /// Executes an operation with proper connection management for a specific database
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="databaseId">Database ID</param>
        /// <param name="operation">Operation to execute</param>
        /// <param name="retryCount">Number of retry attempts</param>
        /// <param name="initialDelayMs">Initial delay between retries in milliseconds</param>
        /// <returns>Result of the operation</returns>
        public async Task<T> ExecuteWithConnectionAsync<T>(
            int databaseId,
            Func<IDbConnection, Task<T>> operation,
            int retryCount = 3,
            int initialDelayMs = 1000)
        {
            if (operation == null)
            {
                return default;
            }

            if (databaseId <= 0)
            {
                return default;
            }

            Exception lastException = null;
            int delay = initialDelayMs;

            for (int i = 0; i < retryCount; i++)
            {
                IDbConnection connection = null;

                try
                {
                    // Create and open connection
                    connection = await CreateOpenConnectionAsync(databaseId);

                    // Execute the operation
                    T result = await operation(connection);

                    // Update connection status to successful
                    try
                    {
                        await UpdateConnectionStatusAsync(databaseId, true);
                    }
                    catch
                    {
                        // Logging or rethrow can occur here
                        throw;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    if (i < retryCount - 1)
                    {
                        await Task.Delay(delay);
                        delay *= 2; // Exponential backoff
                    }
                }
                finally
                {
                    if (connection != null && connection.State != ConnectionState.Closed)
                    {
                        try
                        {
                            connection.Close();
                            (connection as IDisposable)?.Dispose();
                        }
                        catch
                        {
                            // Logging or rethrow can occur here
                            throw;
                        }
                    }
                }
            }

            // Update connection status to failed.
            try
            {
                await UpdateConnectionStatusAsync(databaseId, false);
            }
            catch
            {
                // Logging or rethrow
                throw;
            }

            return default;
        }

        /// <summary>
        /// Executes an operation with proper connection management for a specific database (void return)
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        /// <param name="operation">Operation to execute</param>
        /// <param name="retryCount">Number of retry attempts</param>
        /// <param name="initialDelayMs">Initial delay between retries in milliseconds</param>
        public async Task ExecuteWithConnectionAsync(
            int databaseId,
            Func<IDbConnection, Task> operation,
            int retryCount = 3,
            int initialDelayMs = 1000)
        {
            await ExecuteWithConnectionAsync<object>(
                databaseId,
                async (conn) =>
                {
                    await operation(conn);
                    return null;
                },
                retryCount,
                initialDelayMs);
        }

        /// <summary>
        /// Executes an operation with proper connection management using the application database connection
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="operation">Operation to execute</param>
        /// <returns>Result of the operation</returns>
        public async Task<T> ExecuteWithAppConnectionAsync<T>(Func<IDbConnection, Task<T>> operation)
        {
            if (operation == null) return default;

            bool wasOpen = _appDbConnection.State == ConnectionState.Open;

            try
            {
                if (!wasOpen)
                    _appDbConnection.Open();

                return await operation(_appDbConnection);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (!wasOpen && _appDbConnection.State == ConnectionState.Open)
                    _appDbConnection.Close();
            }
        }

        /// <summary>
        /// Executes an operation with proper connection management using the application database connection (void return)
        /// </summary>
        /// <param name="operation">Operation to execute</param>
        public async Task ExecuteWithAppConnectionAsync(Func<IDbConnection, Task> operation)
        {
            if (operation == null)
                return;

            bool wasOpen = _appDbConnection.State == ConnectionState.Open;

            try
            {
                if (!wasOpen)
                    _appDbConnection.Open();

                await operation(_appDbConnection);
            }
            finally
            {
                if (!wasOpen && _appDbConnection.State == ConnectionState.Open)
                    _appDbConnection.Close();
            }
        }

        #endregion

        #region PRIVATE_HELPERS

        /// <summary>
        /// Gets connection information for a database by ID
        /// </summary>
        /// <param name="databaseId">The database ID</param>
        /// <returns>A tuple containing connection string and database type ID</returns>
        private (string ConnectionString, int DatabaseTypeId) GetConnectionInfo(int databaseId)
        {
            if (databaseId <= 0)
                return (null, 0);

            string connectionString = string.Empty;
            int databaseTypeId = 0;

            try
            {
                // Check if connection string is in cache
                bool connectionStringCached = _connectionStringCache.TryGetValue(databaseId, out connectionString);
                bool databaseTypeCached = _databaseTypeCache.TryGetValue(databaseId, out string databaseTypeStr);

                // If both are cached, return them
                if (connectionStringCached && databaseTypeCached && int.TryParse(databaseTypeStr, out databaseTypeId))
                {
                    return (connectionString, databaseTypeId);
                }

                // At least one value is not cached, fetch database info
                string query = @"
                    SELECT d.*, dt.TypeName as DatabaseTypeName 
                    FROM Databases d 
                    LEFT JOIN DatabaseTypes dt ON d.TypeID = dt.TypeID 
                    WHERE d.DatabaseID = @DatabaseID AND d.IsActive = 1";

                var database = _appDbConnection.QueryFirstOrDefault<Database>(query, new { DatabaseID = databaseId });
                if (database == null)
                    return (null, 0);

                // Update connection string if not cached
                if (!connectionStringCached)
                {
                    connectionString = database.ConnectionString ?? BuildConnectionString(database);
                    _connectionStringCache.TryAdd(databaseId, connectionString);
                }

                // Update database type if not cached
                if (!databaseTypeCached)
                {
                    databaseTypeId = database.TypeID;
                    _databaseTypeCache.TryAdd(databaseId, databaseTypeId.ToString());
                }
                else if (!int.TryParse(databaseTypeStr, out databaseTypeId))
                {
                    // If cached value exists but couldn't be parsed as int
                    databaseTypeId = database.TypeID;
                    _databaseTypeCache.TryAdd(databaseId, databaseTypeId.ToString());
                }
            }
            catch
            {
                throw;
            }

            return (connectionString, databaseTypeId);
        }

        /// <summary>
        /// Builds SQL Server connection string
        /// </summary>
        private string BuildSqlServerConnectionString(Database database)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = database.Port > 0 && database.Port != 1433
                    ? $"{database.ServerAddress},{database.Port}"
                    : database.ServerAddress,
                InitialCatalog = database.Name,
                ConnectTimeout = 30,
                Pooling = true,
                MinPoolSize = 5,
                MaxPoolSize = 100,
                TrustServerCertificate = database.TrustServerCertificate,
                Encrypt = database.SslMode?.ToLower() != "none"
            };

            string decryptedPassword = DecryptCredentials(database.EncryptedCredentials);

            if (string.IsNullOrEmpty(database.Username))
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = database.Username;
                builder.Password = decryptedPassword;
                builder.IntegratedSecurity = false;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Builds MySQL connection string
        /// </summary>
        private string BuildMySqlConnectionString(Database database)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = database.ServerAddress,
                Database = database.Name,
                Port = database.Port > 0 ? (uint)database.Port : 3306,
                ConnectionTimeout = 30,
                Pooling = true,
                MinimumPoolSize = 5,
                MaximumPoolSize = 100,
                SslMode = database.SslMode?.ToLower() switch
                {
                    "none" => MySqlSslMode.None,
                    "preferred" => MySqlSslMode.Preferred,
                    "required" => MySqlSslMode.Required,
                    "verifyca" => MySqlSslMode.VerifyCA,
                    "verifyfull" => MySqlSslMode.VerifyFull,
                    _ => MySqlSslMode.Required
                },
                AllowPublicKeyRetrieval = database.AllowPublicKeyRetrieval
            };

            string decryptedPassword = DecryptCredentials(database.EncryptedCredentials);

            if (!string.IsNullOrEmpty(database.Username))
            {
                builder.UserID = database.Username;
                builder.Password = decryptedPassword;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Builds PostgreSQL connection string
        /// </summary>
        private string BuildPostgreSqlConnectionString(Database database)
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = database.ServerAddress,
                Port = database.Port > 0 ? database.Port : 5432,
                Database = database.Name,
                Timeout = 30,
                Pooling = true,
                MinPoolSize = 5,
                MaxPoolSize = 100,
                SslMode = database.SslMode?.ToLower() switch
                {
                    "none" => SslMode.Disable,
                    "allow" => SslMode.Allow,
                    "prefer" => SslMode.Prefer,
                    "require" => SslMode.Require,
                    "verify-ca" => SslMode.VerifyCA,
                    "verify-full" => SslMode.VerifyFull,
                    _ => SslMode.Prefer
                },
                IncludeErrorDetail = true
            };

            string decryptedPassword = DecryptCredentials(database.EncryptedCredentials);

            if (!string.IsNullOrEmpty(database.Username))
            {
                builder.Username = database.Username;
                builder.Password = decryptedPassword;
            }

            // Additional PostgreSQL options
            if (database.ApplicationName != null)
            {
                builder.ApplicationName = database.ApplicationName;
            }

            if (database.CommandTimeout.HasValue)
            {
                builder.CommandTimeout = database.CommandTimeout.Value;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Builds Oracle connection string
        /// </summary>
        private string BuildOracleConnectionString(Database database)
        {
            var builder = new OracleConnectionStringBuilder
            {
                ConnectionTimeout = 30,
                Pooling = true,
                MinPoolSize = 5,
                MaxPoolSize = 100
            };

            // Oracle DataSource: Easy Connect format (host:port/service_name)
            if (database.Port > 0)
            {
                builder.DataSource = $"{database.ServerAddress}:{database.Port}/{database.Name}";
            }
            else
            {
                // Default Oracle port 1521
                builder.DataSource = $"{database.ServerAddress}:1521/{database.Name}";
            }

            string decryptedPassword = DecryptCredentials(database.EncryptedCredentials);

            if (!string.IsNullOrEmpty(database.Username))
            {
                builder.UserID = database.Username;
                builder.Password = decryptedPassword;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Decrypts encrypted credentials
        /// </summary>
        /// <param name="encryptedCredentials">Encrypted credentials string</param>
        /// <returns>Decrypted password</returns>
        private string DecryptCredentials(string encryptedCredentials)
        {
            if (string.IsNullOrEmpty(encryptedCredentials))
                return string.Empty;

            try
            {
                // TODO: Implement actual decryption logic
                // In production, use secure decryption (e.g., Azure Key Vault, AWS KMS, or protected configuration)
                // For example: return _cryptoService.Decrypt(encryptedCredentials);

                // Temporary implementation - replace with secure decryption
                // This should be moved to a dedicated encryption service
                return encryptedCredentials;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Updates the connection status for a database
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        /// <param name="status">Connection status</param>
        private async Task UpdateConnectionStatusAsync(int databaseId, bool status)
        {
            await ExecuteWithAppConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE Databases 
                    SET LastConnectionStatus = @Status, 
                        LastTransactionDate = GETDATE()
                    WHERE DatabaseID = @DatabaseID",
                    new { Status = status, DatabaseID = databaseId });
            });
        }

        #endregion
    }
}