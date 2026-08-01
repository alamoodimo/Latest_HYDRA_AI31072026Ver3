using System;

namespace DynamicDashboardCommon.Models
{
    /// <summary>
    /// Represents a database connection in the system with comprehensive details
    /// </summary>
    public class Database
    {



        /// <summary>
        /// Unique identifier for the database connection
        /// </summary>
        public int DatabaseID { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Type ID of the database (Foreign key to DatabaseTypes)
        /// </summary>
        public int TypeID { get; set; }

        /// <summary>
        /// Server address for the database connection
        /// </summary>
        public string ServerAddress { get; set; } = string.Empty;

        /// <summary>
        /// Actual name of the database
        /// </summary>
        public string FriendlyName { get; set; } = string.Empty;

        /// <summary>
        /// Port number for the database connection
        /// </summary>
        public int Port { get; set; } = 0;

        /// <summary>
        /// Username for database authentication
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Encrypted credentials for secure storage
        /// </summary>
        public string EncryptedCredentials { get; set; } = string.Empty;

        /// <summary>
        /// SSL mode for secure connections (None, Preferred, Required, VerifyCA, VerifyFull)
        /// </summary>
        public string SslMode { get; set; } = "Preferred";

        /// <summary>
        /// Whether to allow public key retrieval (for MySQL cloud databases)
        /// </summary>
        public bool AllowPublicKeyRetrieval { get; set; } = true;

        /// <summary>
        /// Whether to trust server certificate (for SQL Server)
        /// </summary>
        public bool TrustServerCertificate { get; set; } = true;

        /// <summary>
        /// Timestamp of database connection creation
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// User ID of the creator
        /// </summary>
        public int CreatedBy { get; set; } = 0;

        /// <summary>
        /// Description of the database connection
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Indicates if the database connection is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Timestamp of the last transaction
        /// </summary>
        public DateTime? LastTransactionDate { get; set; }

        /// <summary>
        /// Indicates the last connection status
        /// </summary>
        public bool? LastConnectionStatus { get; set; }

        /// <summary>
        /// Database creation script
        /// </summary>
        public string DBCreationScript { get; set; } = string.Empty;

        /// <summary>
        /// Connection string for the database
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Type name of the database (not a database column, but useful for display)
        /// </summary>
        public string DatabaseTypeName { get; set; } = string.Empty;


        /// <summary>
        /// JSON string containing example questions for this database.
        /// </summary>
        public string SuggestedQuestions { get; set; } = string.Empty;

        // PostgreSQL specific (add to model if needed)
        public string ApplicationName { get; set; } = string.Empty;
        public int? CommandTimeout { get; set; } = 30;
        public string SearchPath { get; set; } = string.Empty;

        // Find your Database.cs model and add these properties:

        public string AnalysisStatus { get; set; } = string.Empty;
        public int? AnalysisCurrentPhase { get; set; }
        public DateTime? AnalysisLastUpdated { get; set; }
        public DateTime? AnalysisStartedAt { get; set; }
        public DateTime? AnalysisCompletedAt { get; set; }

        public string AnalysisPhase1Data { get; set; } = string.Empty;
        public string AnalysisPhase1Status { get; set; } = string.Empty;
        public string AnalysisPhase1Issues { get; set; } = string.Empty;
        public string AnalysisPhase1RawResponse { get; set; } = string.Empty;

        public string AnalysisPhase2Data { get; set; } = string.Empty;
        public string AnalysisPhase2Status { get; set; } = string.Empty;
        public string AnalysisPhase2Issues { get; set; } = string.Empty;
        public string AnalysisPhase2RawResponse { get; set; } = string.Empty;

        public string AnalysisPhase3Data { get; set; } = string.Empty;
        public string AnalysisPhase3Status { get; set; } = string.Empty;
        public string AnalysisPhase3Issues { get; set; } = string.Empty;
        public string AnalysisPhase3RawResponse { get; set; } = string.Empty;

        public string AnalysisPhase4Data { get; set; } = string.Empty;
        public string AnalysisPhase4Status { get; set; } = string.Empty;
        public string AnalysisPhase4Issues { get; set; } = string.Empty;
        public string AnalysisPhase4RawResponse { get; set; } = string.Empty;
    }
}