using DynamicDashboardCommon.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicDashboardCommon.Models.LLM
{

    /// <summary>
    /// Provider-agnostic result of a single LLM generation call.
    /// Carries both the generated text and the normalized reason the model
    /// stopped, so callers can distinguish a complete response from a
    /// truncated one (FinishReason == LlmFinishReason.Length) without
    /// re-parsing provider-specific JSON.
    ///
    /// Every provider (DeepSeek, Claude, Databricks, SQLCoder) maps its own
    /// raw response into this type. It intentionally contains NO
    /// provider-specific fields so it stays usable across the LLM factory.
    /// </summary>
    public class LlmResponse
    {
        /// <summary>
        /// The raw text content returned by the model. This is the same value
        /// the legacy string-returning method returns; may be null or empty if
        /// the model produced no output.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// The normalized reason generation stopped. Defaults to Unknown when a
        /// provider does not supply a recognizable finish reason.
        /// </summary>
        public LlmFinishReason FinishReason { get; set; } = LlmFinishReason.Unknown;

        /// <summary>
        /// Convenience flag: true when the response was cut off by the token
        /// limit and is therefore incomplete. Orchestration uses this to decide
        /// whether a chunk must be retried or re-split.
        /// </summary>
        public bool IsTruncated => FinishReason == LlmFinishReason.Length;

        /// <summary>
        /// Convenience flag: true when there is no usable content, regardless of
        /// finish reason. Guards against the "200 OK with empty content" case.
        /// </summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Content);
    }
}
