using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicDashboardCommon.Enums
{
    /// <summary>
    /// Represents why an LLM stopped generating a response, normalized across
    /// all providers (DeepSeek, Claude, Databricks, SQLCoder). Each provider maps
    /// its own raw "finish_reason" string into this enum so the rest of the
    /// application reasons about completion state in one consistent way.
    /// </summary>
    public enum LlmFinishReason
    {
        /// <summary>
        /// Finish reason was missing, empty, or not recognized. Treated as a
        /// non-committal state so callers can decide how strictly to handle it.
        /// </summary>
        [Description("Unknown")]
        Unknown = 0,

        /// <summary>
        /// The model finished naturally and the response is complete.
        /// Provider strings: "stop", "end_turn".
        /// </summary>
        [Description("Completed")]
        Stop = 1,

        /// <summary>
        /// The model hit the max_tokens / output limit and the response is
        /// TRUNCATED. This is the signal that a chunk must be retried or re-split.
        /// Provider strings: "length", "max_tokens".
        /// </summary>
        /// 

        /// <summary>
        /// The model hit the max_tokens / output limit and the response is
        /// TRUNCATED. This is the signal that a chunk must be retried or re-split.
        /// Provider strings: "length", "max_tokens".
        /// </summary>
        [Description("Truncated (token limit reached)")]
        Length = 2,

        /// <summary>
        /// Generation was stopped by the provider's content filter.
        /// Provider strings: "content_filter".
        /// </summary>
        [Description("Stopped by content filter")]
        ContentFilter = 3
    }
}
