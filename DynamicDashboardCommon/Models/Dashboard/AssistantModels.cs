using System.Collections.Generic;

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
        public string ErrorMessage { get; set; }
        public List<string> ExistingTitles { get; set; } = new List<string>();

        // NEW: Keep same component type
        public int DataViewingTypeID { get; set; }
        public string ChartType { get; set; }  // For charts: bar, line, pie, etc.
    }
}
