using System;
using System.Collections.Generic;

namespace DynamicDashboardCommon.Models.SchemaAnalysis
{
    public enum AnalysisPhase
    {
        NotStarted = 0,
        DomainGrouping = 1,
        TableAnalysis = 2,
        ColumnAnalysis = 3,
        RelationshipAnalysis = 4
    }

    //public enum AnalysisStatus
    //{
    //    NotStarted,
    //    InProgress,
    //    AwaitingApproval,
    //    Completed,
    //    Failed,
    //    Paused
    //}

    public enum PhaseStatus
    {
        NotStarted,
        InProgress,
        Complete,
        Partial,
        Failed
    }

    public class SequentialAnalysisState
    {
        public int DatabaseId { get; set; }
        public AnalysisStatus Status { get; set; }
        public AnalysisPhase CurrentPhase { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        
        public PhaseResult Phase1Result { get; set; }
        public PhaseResult Phase2Result { get; set; }
        public PhaseResult Phase3Result { get; set; }
        public PhaseResult Phase4Result { get; set; }
    }

    public class PhaseResult
    {
        public AnalysisPhase Phase { get; set; }
        public PhaseStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public string DataJson { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public string RawResponse { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }

    // Phase 1: Domain Grouping Models
    public class DomainGroupingResult
    {
        public List<DomainInfo> Domains { get; set; } = new List<DomainInfo>();
        public List<SharedTableInfo> SharedTables { get; set; } = new List<SharedTableInfo>();
    }

    public class DomainInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public int Priority { get; set; }
        public List<string> TableNames { get; set; } = new List<string>();
        public List<string> SharedTablesReferenced { get; set; } = new List<string>();
        public string EstimatedComplexity { get; set; }
    }

    public class SharedTableInfo
    {
        public string TableName { get; set; }
        public List<string> ReferencedByDomains { get; set; } = new List<string>();
        public List<string> ReferencedByTables { get; set; } = new List<string>();
        public string Reason { get; set; }
        public string TreatAsPrimaryDomain { get; set; }
    }

    // Phase 2: Table Analysis Models
    public class TableAnalysisResult
    {
        public List<TableAnalysisInfo> Tables { get; set; } = new List<TableAnalysisInfo>();
    }

    public class TableAnalysisInfo
    {
        public string TableName { get; set; }
        public string FriendlyName { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }  // FACT or DIMENSION
        public int BusinessImportance { get; set; }
        public bool NeedsReview { get; set; }
        public bool IsSharedTable { get; set; }
        public string DomainSpecificUsage { get; set; }
        public List<ColumnSampleRequest> ColumnsNeedingSampleData { get; set; } = new List<ColumnSampleRequest>();
    }

    public class ColumnSampleRequest
    {
        public string ColumnName { get; set; }
        public string Reason { get; set; }
    }

    // Phase 3: Column Analysis Models
    public class ColumnAnalysisResult
    {
        public List<ColumnAnalysisInfo> Columns { get; set; } = new List<ColumnAnalysisInfo>();
    }

    public class ColumnAnalysisInfo
    {
        public string TableName { get; set; }
        public string ColumnName { get; set; }
        public string FriendlyName { get; set; }
        public string Description { get; set; }
        public string DataCategory { get; set; }  // Identifier, Measure, Attribute, Date, Flag, Description, Code
        public int BusinessImportance { get; set; }
        public bool IsLookupColumn { get; set; }
        public string LookupTarget { get; set; }
        public bool IsValueSet { get; set; }
        public List<string> DetectedValues { get; set; } = new List<string>();
        public bool NeedsReview { get; set; }
        public string ReviewReason { get; set; }
    }

    // Phase 4: Relationship Analysis Models
    public class RelationshipAnalysisResult
    {
        public List<SuggestedRelationshipInfo> MissingRelationships { get; set; } = new List<SuggestedRelationshipInfo>();
        public List<CrossDomainRelationshipInfo> CrossDomainRelationships { get; set; } = new List<CrossDomainRelationshipInfo>();
    }

    public class SuggestedRelationshipInfo
    {
        public string SourceTable { get; set; }
        public string SourceColumn { get; set; }
        public string TargetTable { get; set; }
        public string TargetColumn { get; set; }
        public string RelationshipType { get; set; }  // OneToOne, OneToMany, ManyToOne, ManyToMany
        public string BusinessReason { get; set; }
        public int Importance { get; set; }
        public string RelationType { get; set; }  // WithinDomain, ToSharedTable, CrossDomain
    }

    public class CrossDomainRelationshipInfo
    {
        public string SourceTable { get; set; }
        public string SourceDomain { get; set; }
        public string SourceColumn { get; set; }
        public string TargetTable { get; set; }
        public string TargetDomain { get; set; }
        public string TargetColumn { get; set; }
        public string BusinessReason { get; set; }
        public int Importance { get; set; }
    }

    // Sample Data Models
    public class SampleDataResult
    {
        public string TableName { get; set; }
        public string ColumnName { get; set; }
        public string DataType { get; set; }  // ValueSet or FreeText
        public List<object> DistinctValues { get; set; } = new List<object>();
        public Dictionary<string, int> ValueCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<object, string> ResolvedValues { get; set; } = new Dictionary<object, string>();
        public List<object> SampleValues { get; set; } = new List<object>();
    }
}
