using DynamicDashboardCommon.Enums;
using DynamicDashboardCommon.Models;
using DynamicDasboardWebAPI.Repositories;
using DynamicDasboardWebAPI.Services.LLM;
using DynamicDasboardWebAPI.Utilities;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicDasboardWebAPI.Services
{
    /// <summary>
    /// Checks and repairs the SQL of a single dashboard component.
    ///
    /// Check: runs the SQL with a 1-row limit and a short timeout. It passes when it is a single
    /// read-only SELECT, runs without error, and returns at least one row.
    ///
    /// Repair: asks the LLM for corrected SQL that keeps the component's purpose and type, based on
    /// the real problem (the database error, or "returned no rows"), then checks the result.
    /// Repeats up to DashboardAI:MaxSqlRepairAttempts times; each attempt and its problem are sent
    /// back to the LLM so it does not repeat a failed idea.
    ///
    /// Time limit: the whole repair stops waiting for the LLM after DashboardAI:RepairTimeoutSeconds
    /// (default 60) and reports that the provider is busy, instead of waiting for the HTTP timeout.
    ///
    /// Used by "Fix with AI" (AssistantService) and by generation-time checks
    /// (DashboardGenerationService), so both repair SQL the same way.
    /// </summary>
    public class ComponentSqlRepairService : IComponentSqlRepairService
    {
        private readonly QueryRepository _queryRepository;
        private readonly ILLMService _llmService;
        private readonly DatabaseSchemaService _schemaService;
        private readonly DatabaseService _databaseService;
        private readonly ILogsService _logsService;

        private readonly int _maxRepairAttempts;
        private readonly int _validationTimeoutSeconds;
        private readonly int _maxSchemaTablesInPrompt;
        private readonly int _maxErrorMessageLength;
        private readonly TimeSpan _repairTimeout;
        private readonly string _providerName;

        // Statements that must never appear in a dashboard query (whole words, case-insensitive).
        private static readonly Regex WriteStatementPattern = new Regex(
            @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StringLiteralPattern = new Regex(@"'(?:[^']|'')*'", RegexOptions.Compiled);

        public ComponentSqlRepairService(
            QueryRepository queryRepository,
            ILLMService llmService,
            DatabaseSchemaService schemaService,
            DatabaseService databaseService,
            ILogsService logsService,
            IConfiguration configuration)
        {
            _queryRepository = queryRepository ?? throw new ArgumentNullException(nameof(queryRepository));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _logsService = logsService ?? throw new ArgumentNullException(nameof(logsService));

            var config = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _maxRepairAttempts = Math.Max(1, config.GetValue<int>("DashboardAI:MaxSqlRepairAttempts", 2));
            _validationTimeoutSeconds = Math.Max(5, config.GetValue<int>("DashboardAI:ValidationTimeoutSeconds", 30));
            _maxSchemaTablesInPrompt = Math.Max(5, config.GetValue<int>("DashboardAI:RepairPromptMaxTables", 40));
            _maxErrorMessageLength = Math.Max(100, config.GetValue<int>("Query:MaxErrorMessageLength", 1000));
            _repairTimeout = TimeSpan.FromSeconds(Math.Max(10, config.GetValue<int>("DashboardAI:RepairTimeoutSeconds", 60)));

            // Same setting LLMServiceFactory uses to pick the provider (its default is Claude).
            _providerName = string.IsNullOrWhiteSpace(config["LlmService:Provider"]) ? "Claude" : config["LlmService:Provider"];
        }

        // ============================================
        // CHECK
        // ============================================

        /// <inheritdoc />
        public async Task<ComponentSqlValidationResult> ValidateAsync(string sql, int databaseId)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return Problem(ComponentSqlProblem.MissingSql, "The component has no SQL query.");
            }

            if (!IsSingleReadOnlySelect(sql))
            {
                return Problem(ComponentSqlProblem.NotReadOnly,
                    "The query must be a single read-only SELECT statement (no INSERT, UPDATE, DELETE or DDL).");
            }

            try
            {
                var rows = await _queryRepository.ExecuteQueryOnDatabaseAsync(
                    sql, databaseId, maxRows: 1, commandTimeoutSeconds: _validationTimeoutSeconds);

                return rows.Count > 0
                    ? new ComponentSqlValidationResult { Problem = ComponentSqlProblem.None, Message = string.Empty }
                    : Problem(ComponentSqlProblem.NoRows,
                        "The query ran successfully but returned no rows (the table may be empty or the filters too strict).");
            }
            catch (Exception ex) when (QueryErrorFormatter.IsQueryExecutionFailure(ex))
            {
                return Problem(ComponentSqlProblem.QueryFailed, QueryErrorFormatter.BuildMessage(ex, _maxErrorMessageLength));
            }
        }

        // ============================================
        // REPAIR
        // ============================================

        /// <inheritdoc />
        public async Task<ComponentSqlRepairResult> RepairAsync(ComponentSqlRepairRequest request)
        {
            if (request == null || request.DatabaseId <= 0)
            {
                return Failure(request?.Sql, 0, ComponentSqlProblem.MissingSql, "A valid database is required to repair a query.");
            }

            var originalSql = request.Sql?.Trim() ?? string.Empty;

            // 1. Check the current SQL first: nothing to repair when it already works.
            var problem = await ValidateAsync(originalSql, request.DatabaseId);
            if (problem.IsValid)
            {
                return new ComponentSqlRepairResult
                {
                    Success = true,
                    Sql = originalSql,
                    WasRepaired = false,
                    AttemptsUsed = 0,
                    LastProblem = ComponentSqlProblem.None,
                    Message = "The query already works."
                };
            }

            // 2. Context for the LLM: database type and the relevant part of the schema.
            var database = await _databaseService.GetDatabaseByIdAsync(request.DatabaseId);
            var databaseType = database?.DatabaseTypeName ?? "SQL Server";

            var schema = await _schemaService.GetSchemaObject(request.DatabaseId, useCache: true);
            if (schema?.Tables == null || !schema.Tables.Any())
            {
                return Failure(originalSql, 0, problem.Problem, "Could not read the database schema, so the query cannot be repaired.");
            }

            var systemPrompt = BuildRepairSystemPrompt(request, databaseType);
            var schemaText = SchemaPromptBuilder.Build(schema, $"{originalSql} {request.Title}", _maxSchemaTablesInPrompt);

            // 3. Ask, check, and feed the new problem back until it works, attempts run out,
            //    or the time limit for the whole repair is reached.
            var previousAttempts = new List<string>();
            var lastSql = originalSql;
            var deadline = DateTime.UtcNow + _repairTimeout;

            for (var attempt = 1; attempt <= _maxRepairAttempts; attempt++)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return await TimedOutAsync(request, originalSql, attempt - 1, problem, previousAttempts.Count > 0);
                }

                var userPrompt = BuildRepairUserPrompt(request, originalSql, problem, previousAttempts, schemaText, databaseType);

                var llmCall = await CallLlmWithTimeLimitAsync(systemPrompt, userPrompt, remaining);
                if (llmCall.TimedOut)
                {
                    return await TimedOutAsync(request, originalSql, attempt - 1, problem, previousAttempts.Count > 0);
                }

                if (llmCall.Error != null)
                {
                    await LogWarningAsync($"SQL repair: AI request failed for '{request.Title}' (database {request.DatabaseId}): {llmCall.Error.Message}");
                    return Failure(originalSql, attempt, problem.Problem, $"The AI service could not be reached: {llmCall.Error.Message}");
                }

                var (candidateSql, explanation) = ParseRepairResponse(llmCall.Response);
                if (string.IsNullOrWhiteSpace(candidateSql))
                {
                    previousAttempts.Add($"Attempt {attempt}: the response contained no SQL.");
                    continue;
                }

                var check = await ValidateAsync(candidateSql, request.DatabaseId);
                if (check.IsValid)
                {
                    return new ComponentSqlRepairResult
                    {
                        Success = true,
                        Sql = candidateSql,
                        WasRepaired = true,
                        AttemptsUsed = attempt,
                        LastProblem = ComponentSqlProblem.None,
                        Message = $"Query repaired after {attempt} attempt(s).",
                        Explanation = explanation
                    };
                }

                previousAttempts.Add($"Attempt {attempt}:{Environment.NewLine}{candidateSql}{Environment.NewLine}Problem: {check.Message}");
                lastSql = candidateSql;
                problem = check;
            }

            await LogWarningAsync(
                $"SQL repair failed for '{request.Title}' (database {request.DatabaseId}) after {_maxRepairAttempts} attempt(s). " +
                $"Last problem: {problem.Message}{Environment.NewLine}Last SQL: {lastSql}");

            return Failure(originalSql, _maxRepairAttempts, problem.Problem,
                $"The AI could not produce a working query after {_maxRepairAttempts} attempt(s). Last problem: {problem.Message}");
        }

        // ============================================
        // TIME LIMIT
        // ============================================

        /// <summary>Outcome of one LLM call made under a time limit.</summary>
        private sealed class LlmCallOutcome
        {
            public string Response { get; init; }
            public bool TimedOut { get; init; }
            public Exception Error { get; init; }
        }

        /// <summary>
        /// Calls the LLM but stops waiting after <paramref name="timeLimit"/>. The provider interface has
        /// no cancellation support, so a call that runs over ends on its own in the background; its
        /// outcome is observed so a late failure is never reported as an unobserved task exception.
        /// An HTTP timeout raised by the provider itself is also treated as "did not answer in time".
        /// </summary>
        private async Task<LlmCallOutcome> CallLlmWithTimeLimitAsync(string systemPrompt, string userPrompt, TimeSpan timeLimit)
        {
            Task<string> llmTask;
            try
            {
                llmTask = _llmService.GenerateDashboardSuggestionsAsync(systemPrompt, userPrompt);
            }
            catch (Exception ex)
            {
                return IsTimeout(ex) ? new LlmCallOutcome { TimedOut = true } : new LlmCallOutcome { Error = ex };
            }

            using var delayCancellation = new CancellationTokenSource();
            var finished = await Task.WhenAny(llmTask, Task.Delay(timeLimit, delayCancellation.Token));

            if (finished != llmTask)
            {
                _ = llmTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return new LlmCallOutcome { TimedOut = true };
            }

            delayCancellation.Cancel();

            try
            {
                return new LlmCallOutcome { Response = await llmTask };
            }
            catch (Exception ex) when (IsTimeout(ex))
            {
                return new LlmCallOutcome { TimedOut = true };
            }
            catch (Exception ex)
            {
                return new LlmCallOutcome { Error = ex };
            }
        }

        /// <summary>
        /// Result for a repair that ran out of time. The original SQL is kept, a warning is logged,
        /// and the message names the provider so the user knows the AI service is busy, not the app.
        /// </summary>
        private async Task<ComponentSqlRepairResult> TimedOutAsync(
            ComponentSqlRepairRequest request,
            string originalSql,
            int attemptsCompleted,
            ComponentSqlValidationResult lastProblem,
            bool hadFailedAttempts)
        {
            var seconds = _repairTimeout.TotalSeconds.ToString("0");
            var message = $"{_providerName} did not answer within {seconds} seconds (the service may be busy). Try again later.";

            if (hadFailedAttempts)
            {
                message += $" Last problem after {attemptsCompleted} attempt(s): {lastProblem.Message}";
            }

            await LogWarningAsync($"SQL repair timed out for '{request.Title}' (database {request.DatabaseId}): " +
                                  $"{_providerName} did not answer within {seconds} seconds.");

            return Failure(originalSql, attemptsCompleted, lastProblem.Problem, message);
        }

        /// <summary>True when the exception chain contains a timeout or a cancellation (e.g. HttpClient.Timeout).</summary>
        private static bool IsTimeout(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is TimeoutException || current is OperationCanceledException)
                {
                    return true;
                }
            }
            return false;
        }

        // ============================================
        // PROMPTS
        // ============================================

        private string BuildRepairSystemPrompt(ComponentSqlRepairRequest request, string databaseType)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an expert SQL developer repairing the query of ONE dashboard component.");
            sb.AppendLine($"Return a corrected query that runs without errors on {databaseType} and returns at least one row,");
            sb.AppendLine("while keeping the component's business meaning (its title and description) and its type.");
            sb.AppendLine();
            sb.AppendLine("RULES:");
            sb.AppendLine("1. Keep the same business meaning as the title and description. Do not switch to an unrelated metric.");
            sb.AppendLine("2. Use only tables and columns that exist in the schema provided, spelled exactly as shown.");
            sb.AppendLine("3. Fix the specific problem reported. If the query returned no rows, the table may be empty or the");
            sb.AppendLine("   filters too strict: loosen or remove filters, or use another table that holds the same information.");
            sb.AppendLine("4. Do not filter relative to today's date unless the title explicitly asks for a recent period;");
            sb.AppendLine("   the data may be historical.");
            sb.AppendLine("5. A single read-only SELECT statement (common table expressions allowed). No INSERT, UPDATE, DELETE or DDL.");
            sb.AppendLine("6. Never repeat an attempt listed under PREVIOUS ATTEMPTS.");
            sb.AppendLine();
            sb.AppendLine("RESULT SHAPE:");
            sb.AppendLine(GetResultShapeRule(request.DataViewingTypeID, request.ChartType));
            sb.AppendLine();
            sb.AppendLine("## DATABASE SYNTAX RULES - CRITICAL");
            sb.AppendLine(SqlDialectGuidance.For(databaseType));
            sb.AppendLine();
            sb.AppendLine("OUTPUT: return ONLY a JSON object, no markdown:");
            sb.AppendLine("{\"sql\": \"<the corrected query>\", \"explanation\": \"<one short sentence on what you changed>\"}");

            return sb.ToString();
        }

        private static string BuildRepairUserPrompt(
            ComponentSqlRepairRequest request,
            string originalSql,
            ComponentSqlValidationResult problem,
            List<string> previousAttempts,
            string schemaText,
            string databaseType)
        {
            var sb = new StringBuilder();

            sb.AppendLine("COMPONENT:");
            sb.AppendLine($"- Title: {request.Title}");
            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                sb.AppendLine($"- Description: {request.Description}");
            }
            sb.AppendLine($"- Type: {GetComponentTypeName(request.DataViewingTypeID)}" +
                          (request.DataViewingTypeID == (int)DataViewingTypeEnum.Chart && !string.IsNullOrWhiteSpace(request.ChartType)
                              ? $" ({request.ChartType})"
                              : string.Empty));
            sb.AppendLine();

            sb.AppendLine("CURRENT SQL:");
            sb.AppendLine(string.IsNullOrWhiteSpace(originalSql) ? "(none - write a new query for this component)" : originalSql);
            sb.AppendLine();

            sb.AppendLine("PROBLEM TO FIX:");
            sb.AppendLine(problem.Message);
            sb.AppendLine();

            if (previousAttempts.Any())
            {
                sb.AppendLine("PREVIOUS ATTEMPTS (all failed - do not repeat them):");
                foreach (var attempt in previousAttempts)
                {
                    sb.AppendLine(attempt);
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"DATABASE SCHEMA ({databaseType}):");
            sb.AppendLine(schemaText);

            return sb.ToString();
        }

        /// <summary>Expected result columns per component type (matches how the builder draws them).</summary>
        private static string GetResultShapeRule(int dataViewingTypeId, string chartType)
        {
            switch (dataViewingTypeId)
            {
                case (int)DataViewingTypeEnum.Number:
                    return "KPI: exactly one row with one numeric column (e.g. SUM, COUNT or AVG), with a clear alias.";

                case (int)DataViewingTypeEnum.Table:
                    return "Table: readable columns with clear aliases; limit the result to 100 rows using the database's row-limit syntax.";

                case (int)DataViewingTypeEnum.Chart:
                    var type = (chartType ?? string.Empty).ToLowerInvariant();
                    return type == "pie" || type == "donut" || type == "doughnut"
                        ? "Pie/donut chart: two columns - a text category aliased 'label' and one numeric column aliased 'value'; at most 10 rows."
                        : "Chart: the first column is the category or period aliased 'label'; then one to four numeric columns " +
                          "(alias a single one 'value'). At most 50 rows, ordered by label for time series.";

                default:
                    return "Return readable columns with clear aliases.";
            }
        }

        // ============================================
        // HELPERS
        // ============================================

        /// <summary>
        /// Reads {"sql": "...", "explanation": "..."} from the LLM response. Falls back to a ```sql
        /// block or to plain text starting with SELECT/WITH. Trailing semicolons are removed
        /// (some providers reject them).
        /// </summary>
        private static (string Sql, string Explanation) ParseRepairResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return (null, null);
            }

            string sql = null;
            string explanation = null;

            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try
                {
                    using var document = JsonDocument.Parse(response.Substring(start, end - start + 1));
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String) continue;

                        if (property.Name.Equals("sql", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("sqlTemplate", StringComparison.OrdinalIgnoreCase))
                        {
                            sql = property.Value.GetString();
                        }
                        else if (property.Name.Equals("explanation", StringComparison.OrdinalIgnoreCase))
                        {
                            explanation = property.Value.GetString();
                        }
                    }
                }
                catch (JsonException)
                {
                    // Not valid JSON; try the fallbacks below.
                }
            }

            if (string.IsNullOrWhiteSpace(sql))
            {
                var fence = Regex.Match(response, @"```(?:sql)?\s*(?<sql>[\s\S]*?)```", RegexOptions.IgnoreCase);
                if (fence.Success)
                {
                    sql = fence.Groups["sql"].Value;
                }
                else if (Regex.IsMatch(response.TrimStart(), @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase))
                {
                    sql = response;
                }
            }

            sql = sql?.Trim().TrimEnd(';').Trim();
            return (string.IsNullOrWhiteSpace(sql) ? null : sql, explanation);
        }

        /// <summary>
        /// True for one SELECT (or WITH ... SELECT) statement without data-changing keywords.
        /// Text inside string literals is ignored, so values like 'Deleted' do not count.
        /// </summary>
        private static bool IsSingleReadOnlySelect(string sql)
        {
            var withoutStrings = StringLiteralPattern.Replace(sql, "''").Trim().TrimEnd(';').Trim();

            if (!Regex.IsMatch(withoutStrings, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            {
                return false;
            }

            return !withoutStrings.Contains(';') && !WriteStatementPattern.IsMatch(withoutStrings);
        }

        private static string GetComponentTypeName(int dataViewingTypeId)
        {
            return dataViewingTypeId switch
            {
                (int)DataViewingTypeEnum.Table => "Table",
                (int)DataViewingTypeEnum.Label => "Label",
                (int)DataViewingTypeEnum.Number => "KPI (single number)",
                (int)DataViewingTypeEnum.Chart => "Chart",
                _ => "Component"
            };
        }

        private static ComponentSqlValidationResult Problem(ComponentSqlProblem problem, string message)
        {
            return new ComponentSqlValidationResult { Problem = problem, Message = message };
        }

        private static ComponentSqlRepairResult Failure(string sql, int attempts, ComponentSqlProblem problem, string message)
        {
            return new ComponentSqlRepairResult
            {
                Success = false,
                Sql = sql,
                WasRepaired = false,
                AttemptsUsed = attempts,
                LastProblem = problem,
                Message = message
            };
        }

        /// <summary>Writes a warning to the application log; logging problems never break a repair.</summary>
        private async Task LogWarningAsync(string message)
        {
            try
            {
                await _logsService.AddLogAsync(null, EnumLoggingType.Warning.ToString(), message);
            }
            catch (Exception)
            {
                // Logging must not break the repair.
            }
        }
    }
}
