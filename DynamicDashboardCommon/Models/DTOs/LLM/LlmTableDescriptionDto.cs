using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace DynamicDashboardCommon.Models.DTOs.LLM
{
    /// <summary>
    /// Raw "table" entry exactly as emitted by the LLM schema-analysis JSON.
    /// Deliberately separate from the domain <see cref="TableDescription"/> so the
    /// LLM's key names (friendlyName/description) never leak into the API/Blazor
    /// contract. Mapped into TableDescription in SchemaAnalysisService.
    /// </summary>
    public class LlmTableDescriptionDto
    {
        [JsonPropertyName("tableName")]
        public string? TableName { get; set; }

        [JsonPropertyName("table")]
        public string? TableAlias { set { if (!string.IsNullOrWhiteSpace(value)) TableName = value; } }


        [JsonPropertyName("friendlyName")]
        public string? FriendlyName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
