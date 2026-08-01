using System.Collections.Generic;

namespace DynamicDashboardCommon.Models
{

        public class SchemaTableDto
        {
            public string TableName { get; set; }
            public string AdminTableName { get; set; }
            public string AdminDescription { get; set; }
            public List<SchemaColumnDto> Columns { get; set; } = new List<SchemaColumnDto>();

            // Add this property for relationship handling
            public List<SchemaRelationshipDto> Relationships { get; set; } = new List<SchemaRelationshipDto>();
        }

        public class SchemaColumnDto
        {
            public string ColumnName { get; set; }
            public string AdminColumnName { get; set; }
            public string DataType { get; set; }
            public bool IsNullable { get; set; }
            public bool IsPrimary { get; set; }
            public bool IsForeignKey { get; set; }
            public string AdminDescription { get; set; }
            public bool IsLookupColumn { get; set; }
        }

        // Add this class for relationship modeling
        public class SchemaRelationshipDto
        {
        public string SourceTable { get; set; }
        public string SourceColumn { get; set; }
        public string TargetTable { get; set; }
        public string TargetColumn { get; set; }
        public string RelationshipType { get; set; }
        public string Description { get; set; }
    }

    // Add to models

    // Add at the bottom of SchemaAnalysisService.cs or in a new file

    /// <summary>
    /// Priority table class for batching
    /// </summary>
    public class PriortizedTable
    {
        public string Name { get; set; }
        public TableSchema Schema { get; set; }
        public int Priority { get; set; }
        public int ColumnCount { get; set; }
    }

    /// <summary>
    /// Analysis status for UI
    /// </summary>
    public class AnalysisStatus
    {
        public int TablesAnalyzed { get; set; }
        public int TotalTables { get; set; }
        public int ColumnsAnalyzed { get; set; }
        public int TotalColumns { get; set; }
        public int RelationshipsFound { get; set; }
        public DateTime? LastAnalyzed { get; set; }
        public double TableProgress => TotalTables > 0 ? (TablesAnalyzed * 100.0 / TotalTables) : 0;
        public double ColumnProgress => TotalColumns > 0 ? (ColumnsAnalyzed * 100.0 / TotalColumns) : 0;
    }

    /// <summary>
    /// Progress update model for real-time feedback
    /// </summary>
    public class AnalysisProgress
    {
        public int ProcessedCount { get; set; }
        public int TotalCount { get; set; }
        public string CurrentItem { get; set; }
        public string Phase { get; set; }
        public int PercentComplete => TotalCount > 0
            ? (int)((ProcessedCount / (double)TotalCount) * 100)
            : 0;
    }

    /// <summary>
    /// Error tracking model
    /// </summary>
    public class AnalysisError
    {
        public string TableName { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Result from chunked/paginated schema analysis.
    /// Enables progressive loading and real-time progress display.
    /// </summary>
    public class ChunkedAnalysisResult
    {
        /// <summary>
        /// Whether this chunk was processed successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if chunk processing failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Table descriptions analyzed in this chunk
        /// </summary>
        public List<TableDescription> TableResults { get; set; } = new();

        /// <summary>
        /// Column descriptions analyzed in this chunk
        /// </summary>
        public List<ColumnDescription> ColumnResults { get; set; } = new();

        /// <summary>
        /// Number of items processed so far (cumulative)
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// Total number of items to process
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Number of items remaining after this chunk
        /// </summary>
        public int RemainingCount => TotalCount - ProcessedCount;

        /// <summary>
        /// Progress percentage (0-100)
        /// </summary>
        public int ProgressPercent => TotalCount > 0
            ? (int)Math.Round((ProcessedCount * 100.0) / TotalCount)
            : 0;

        /// <summary>
        /// Whether all items have been processed
        /// </summary>
        public bool IsComplete => ProcessedCount >= TotalCount;

        /// <summary>
        /// Name of current/last item being processed (for display)
        /// </summary>
        public string CurrentItemName { get; set; }

        /// <summary>
        /// How long this chunk took to process
        /// </summary>
        public TimeSpan ChunkDuration { get; set; }

        /// <summary>
        /// Errors encountered during this chunk (non-fatal)
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Number of items skipped (already analyzed)
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// Counts of items needing analysis (for progress setup)
        /// </summary>
        
    }

    public class AnalysisCountResult
    {
        public int TotalTables { get; set; }
        public int TablesToAnalyze { get; set; }
        public int TotalColumns { get; set; }
        public int ColumnsToAnalyze { get; set; }
    }
}
