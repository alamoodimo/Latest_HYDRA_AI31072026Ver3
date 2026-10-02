using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using DynamicDashboardCommon.Models;

namespace DynamicDashboardCommon.Helper
{
    /// <summary>
    /// Shared helpers for query results (api/query/execute), used by the Dashboard Builder and
    /// the Dashboard Viewer so both read responses, errors and numbers the same way.
    /// </summary>
    public static class QueryResultHelper
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Reads an api/query/execute response, success or failure. A failure body can be the
        /// QueryExecutionResponse itself (with ErrorMessage), the API's generic { "message": ... }
        /// error, or plain text; the most specific message available ends up in ErrorMessage.
        /// Never returns null.
        /// </summary>
        public static async Task<QueryExecutionResponse> ReadQueryExecutionResponseAsync(HttpResponseMessage response)
        {
            var body = response?.Content != null ? await response.Content.ReadAsStringAsync() : string.Empty;
            QueryExecutionResponse result = null;
            string message = null;

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    var root = document.RootElement;

                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        result = JsonSerializer.Deserialize<QueryExecutionResponse>(body, JsonOptions);

                        if (root.TryGetProperty("message", out var messageElement) &&
                            messageElement.ValueKind == JsonValueKind.String)
                        {
                            message = messageElement.GetString();
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.String)
                    {
                        message = root.GetString();
                    }
                }
                catch (JsonException)
                {
                    message = body.Length > 300 ? body.Substring(0, 300) + "…" : body;
                }
            }

            result ??= new QueryExecutionResponse();
            result.Results ??= new List<Dictionary<string, object>>();

            if (response == null || !response.IsSuccessStatusCode)
            {
                result.Success = false;
            }

            if (!result.Success && string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                result.ErrorMessage = !string.IsNullOrWhiteSpace(message)
                    ? message
                    : response == null
                        ? "The query could not be run."
                        : $"The query failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
            }

            return result;
        }

        /// <summary>
        /// Reads a numeric value from a query result cell. Values arrive as JsonElement; numbers are
        /// parsed with the invariant culture so the user's locale cannot break them.
        /// </summary>
        public static bool TryGetDecimal(object value, out decimal number)
        {
            number = 0;
            switch (value)
            {
                case null:
                    return false;
                case JsonElement element when element.ValueKind == JsonValueKind.Number:
                    return element.TryGetDecimal(out number);
                case JsonElement element when element.ValueKind == JsonValueKind.String:
                    return decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number);
                case JsonElement:
                    return false;
                case decimal d:
                    number = d;
                    return true;
                case string text:
                    return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number);
                case bool:
                    return false;
                case IConvertible convertible:
                    try
                    {
                        number = convertible.ToDecimal(CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                default:
                    return false;
            }
        }

        /// <summary>
        /// KPI value: the first numeric value of the first row (falls back to the first value),
        /// so a KPI query whose number is not in the first column still shows the number.
        /// </summary>
        public static object GetKpiValue(List<Dictionary<string, object>> rows)
        {
            var firstRow = rows?.FirstOrDefault();
            if (firstRow == null || firstRow.Count == 0)
            {
                return null;
            }

            foreach (var value in firstRow.Values)
            {
                if (TryGetDecimal(value, out _))
                {
                    return value;
                }
            }

            return firstRow.Values.First();
        }
    }
}
