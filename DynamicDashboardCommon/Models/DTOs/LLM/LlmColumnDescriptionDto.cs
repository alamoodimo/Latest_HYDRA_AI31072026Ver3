using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace DynamicDashboardCommon.Models.DTOs.LLM
{
    public class LlmColumnDescriptionDto
    {
        [JsonPropertyName("tableName")]
        public string? TableName { get; set; }

        [JsonPropertyName("table")]
        public string? TableAlias { set { if (!string.IsNullOrWhiteSpace(value)) TableName = value; } }


        [JsonPropertyName("columnName")]
        public string? ColumnName { get; set; }

        [JsonPropertyName("column")]
        public string? ColumnAlias { set { if (!string.IsNullOrWhiteSpace(value)) ColumnName = value; } }


        [JsonPropertyName("friendlyName")]
        public string? FriendlyName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        // Present in some LLM responses, absent in others; defaults to false when missing.
        [JsonPropertyName("isLookupColumn")]
        public bool IsLookupColumn { get; set; }
    }
}
