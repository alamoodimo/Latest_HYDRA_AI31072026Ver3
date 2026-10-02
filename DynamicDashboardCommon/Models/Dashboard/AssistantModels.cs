using System.Collections.Generic;
using DynamicDashboardCommon.Enums;

namespace DynamicDashboardCommon.Models
{
    /// <summary>
    /// Request model for AI assistant chat
    /// </summary>
    public class AssistantChatRequest
    {
        public int DashboardId { get; set; }
        public int DatabaseId { get; set; }
        public List<DashboardComponent> CurrentComponents { get; set; }
    }

    /// <summary>
    /// Response model for AI assistant suggestions
    /// </summary>
    public class AssistantSuggestionResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<ComponentSuggestion> Suggestions { get; set; }
    }

    /// <summary>
    /// Individual component suggestion
    /// </summary>
    public class ComponentSuggestion
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public int DataViewingTypeID { get; set; }
        public string ChartType { get; set; }
        public string SqlTemplate { get; set; }
        public int GridWidth { get; set; }
        public int GridHeight { get; set; }
    }

    /// <summary>
    /// Request model for regenerating a failed component
    /// </summary>
    public class RegenerateComponentRequest
    {
        public int DatabaseId { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public int GridWidth { get; set; }
        public int GridHeight { get; set; }
        public string FailedTitle { get; set; }
        public string FailedSql { get; set; }
        /// <summary>
        /// The component's current description. Repair mode keeps it unchanged and gives it to the
        /// AI as context, so the corrected query keeps the same business meaning.
        /// </summary>
        public string FailedDescription { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> ExistingTitles { get; set; } = new List<string>();

        // NEW: Keep same component type
        public int DataViewingTypeID { get; set; }
        public string ChartType { get; set; }  // For charts: bar, line, pie, etc.

        /// <summary>
        /// Repair = fix this component's SQL (same title and purpose);
        /// Alternative = replace it with a different component of the same type.
        /// Unspecified (older clients) = inferred from ErrorMessage.
        /// </summary>
        public ComponentRegenerationMode Mode { get; set; }
    }

    /// <summary>
    /// Result of checking a component's SQL against its database (runs it with a 1-row limit).
    /// </summary>
    public class ComponentSqlValidationResult
    {
        /// <summary>None when the query runs and returns at least one row.</summary>
        public ComponentSqlProblem Problem { get; set; }

        /// <summary>Readable description of the problem (empty when there is none).</summary>
        public string Message { get; set; }

        /// <summary>True when the query runs and returns at least one row.</summary>
        public bool IsValid => Problem == ComponentSqlProblem.None;
    }

    /// <summary>
    /// Request to repair one dashboard component's SQL while keeping its purpose and type.
    /// </summary>
    public class ComponentSqlRepairRequest
    {
        public int DatabaseId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        /// <summary>1 = Table, 2 = Label, 3 = Number (KPI), 4 = Chart (DataViewingTypeEnum).</summary>
        public int DataViewingTypeID { get; set; }

        /// <summary>Chart type for charts (bar, line, pie, ...); ignored for other types.</summary>
        public string ChartType { get; set; }

        /// <summary>The current SQL (may be empty when the component has none yet).</summary>
        public string Sql { get; set; }
    }

    /// <summary>
    /// Outcome of a repair: the working SQL, or the last problem when no working SQL was found.
    /// </summary>
    public class ComponentSqlRepairResult
    {
        /// <summary>True when Sql runs and returns at least one row.</summary>
        public bool Success { get; set; }

        /// <summary>The working SQL (the original when it already worked); the original SQL on failure.</summary>
        public string Sql { get; set; }

        /// <summary>True when the SQL was changed by the AI.</summary>
        public bool WasRepaired { get; set; }

        /// <summary>Number of AI repair attempts used.</summary>
        public int AttemptsUsed { get; set; }

        /// <summary>The problem of the last SQL that was checked (None on success).</summary>
        public ComponentSqlProblem LastProblem { get; set; }

        /// <summary>Readable summary for the user or the log.</summary>
        public string Message { get; set; }

        /// <summary>Short note from the AI on what it changed (when repaired).</summary>
        public string Explanation { get; set; }
    }
}
