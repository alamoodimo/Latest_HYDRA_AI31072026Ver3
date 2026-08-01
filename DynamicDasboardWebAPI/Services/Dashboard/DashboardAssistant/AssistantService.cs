using DynamicDashboardCommon.Enums;
using DynamicDashboardCommon.Models;
using DynamicDasboardWebAPI.Services.LLM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DynamicDasboardWebAPI.Services
{
    /// <summary>
    /// Service for AI assistant operations with LLM integration
    /// </summary>
    public class AssistantService : IAssistantService
    {
        private readonly ILogsService _logsService;
        private readonly ILLMService _llmService;
        private readonly DatabaseSchemaService _schemaService;
        private readonly DatabaseService _databaseService; // NEW: Added

        public AssistantService(
            ILogsService logsService,
            ILLMService llmService,
            DatabaseSchemaService schemaService,
            DatabaseService databaseService) // NEW: Added
        {
            _logsService = logsService ?? throw new ArgumentNullException(nameof(logsService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService)); // NEW
        }

        public async Task<AssistantSuggestionResponse> GenerateSuggestionsAsync(AssistantChatRequest request)
        {
            try
            {
                if (request.DashboardId <= 0)
                {
                    return new AssistantSuggestionResponse
                    {
                        Success = false,
                        Message = "Invalid dashboard ID",
                        Suggestions = new List<ComponentSuggestion>()
                    };
                }

                if (request.DatabaseId <= 0)
                {
                    return new AssistantSuggestionResponse
                    {
                        Success = false,
                        Message = "Invalid database ID",
                        Suggestions = new List<ComponentSuggestion>()
                    };
                }

                var schema = await _schemaService.GetSchemaObject(request.DatabaseId, useCache: true);
                if (schema == null)
                {
                    return new AssistantSuggestionResponse
                    {
                        Success = false,
                        Message = "Could not retrieve database schema",
                        Suggestions = new List<ComponentSuggestion>()
                    };
                }

                // NEW: Get database type
                var database = await _databaseService.GetDatabaseByIdAsync(request.DatabaseId);
                string databaseType = database?.DatabaseTypeName ?? "SQL Server";

                var systemPrompt = BuildSystemPrompt(databaseType);
                var userPrompt = BuildUserPrompt(request, schema, databaseType);

                var llmResponse = await _llmService.GenerateDashboardSuggestionsAsync(
                    systemPrompt,
                    userPrompt
                );

                if (string.IsNullOrWhiteSpace(llmResponse))
                {
                    return new AssistantSuggestionResponse
                    {
                        Success = false,
                        Message = "LLM returned empty response",
                        Suggestions = new List<ComponentSuggestion>()
                    };
                }

                var suggestions = ParseLLMResponse(llmResponse);

                return new AssistantSuggestionResponse
                {
                    Success = true,
                    Message = suggestions.Any()
                        ? $"Found {suggestions.Count} suggestions for your dashboard"
                        : "Your dashboard looks great! No suggestions at this time.",
                    Suggestions = suggestions
                };
            }
            catch (Exception ex)
            {
                return new AssistantSuggestionResponse
                {
                    Success = false,
                    Message = $"Error generating suggestions: {ex.Message}",
                    Suggestions = new List<ComponentSuggestion>()
                };
            }
        }

        private string BuildSystemPrompt(string databaseType)
        {
            var sb = new StringBuilder();

            sb.AppendLine(@"You are an expert business intelligence analyst, data analyst, and dashboard designer. Your role is to analyze database schemas and suggest valuable dashboard components that provide business insights.

When suggesting components:
1. Consider what components already exist to avoid duplication
2. Suggest components that add real analytical value based on business intelligence principles
3. Provide SQL queries that are optimized and correct according to provided database type and schema structure tables and columns
4. Choose appropriate visualization types for the data
5. Consider business metrics and KPIs that matter
6. Think like a BI expert - focus on actionable insights");

            // NEW: Add database-specific syntax rules
            sb.AppendLine();
            sb.AppendLine("## DATABASE SYNTAX RULES - CRITICAL");
            sb.AppendLine();
            sb.AppendLine($"Target Database: {databaseType}");
            sb.AppendLine();
            sb.AppendLine(GetDbSyntaxGuidance(databaseType));
            sb.AppendLine();

            sb.AppendLine(@"Output Format:
Return ONLY a valid JSON array of suggestions. Each suggestion must have:
- title: Component title (concise, business-friendly)
- description: What business insight this provides (1 sentence)
- icon: Font Awesome icon name (without 'fa-' prefix, e.g., 'chart-pie', 'dollar-sign')
- dataViewingTypeID: 1=Table, 2=Label, 3=Number/KPI, 4=Chart
- chartType: ""bar"", ""line"", ""pie"", ""donut"", ""area"", or null for KPI/Table
- sqlTemplate: Valid SQL query that returns data
- gridWidth: 6 (column span in 12-column grid)
- gridHeight: 4  (row span)

Rules:
- Return maximum 5 suggestions
- If dashboard is complete, return empty array []
- Ensure SQL is valid for the database type and schema structure
- Focus on actionable business insights, not just data display
- Avoid duplicating existing components
- Use business-friendly titles and descriptions

Example output:
[
  {
    ""title"": ""Revenue Growth Rate"",
    ""description"": ""Track month-over-month revenue growth percentage"",
    ""icon"": ""chart-line"",
    ""dataViewingTypeID"": 4,
    ""chartType"": ""line"",
    ""sqlTemplate"": ""SELECT MONTH(SaleDate) as Month, SUM(Amount) as Revenue FROM Sales GROUP BY MONTH(SaleDate) ORDER BY Month"",
    ""gridWidth"": 6,
    ""gridHeight"": 4
  }
]");

            return sb.ToString();
        }

        private string BuildUserPrompt(AssistantChatRequest request, DatabaseSchema schema, string databaseType)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine("Analyze this dashboard and suggest improvements:");
            prompt.AppendLine();

            // NEW: Remind about database type
            prompt.AppendLine($"**IMPORTANT: Generate SQL queries valid for {databaseType}** and schema structure");
            prompt.AppendLine();

            // Database info
            prompt.AppendLine("DATABASE SCHEMA:");
            prompt.AppendLine($"Database: {schema.Name}");
            prompt.AppendLine("Tables:");

            foreach (var table in schema.Tables)
            {
                prompt.AppendLine($"- {table.DBName ?? table.FriendlyName}");
                if (table.Columns != null && table.Columns.Any())
                {
                    var columns = table.Columns.Take(15).Select(c => $"{c.DBName ?? c.FriendlyName} ({c.DataType})");
                    prompt.AppendLine($"  Columns: {string.Join(", ", columns)}");
                }
            }

            prompt.AppendLine();

            // Current dashboard state
            var components = request.CurrentComponents ?? new List<DashboardComponent>();
            prompt.AppendLine("CURRENT DASHBOARD:");
            prompt.AppendLine($"- Total Components: {components.Count}");
            prompt.AppendLine();

            // Existing components with details INCLUDING SQL
            if (components.Any())
            {
                prompt.AppendLine("EXISTING COMPONENTS:");
                for (int i = 0; i < components.Count; i++)
                {
                    var comp = components[i];
                    var typeName = GetComponentTypeName(comp.DataViewingTypeID);
                    var chartInfo = !string.IsNullOrEmpty(comp.ChartType) ? $" ({comp.ChartType})" : "";

                    prompt.AppendLine($"{i + 1}. \"{comp.Title}\" - {typeName}{chartInfo}");

                    if (!string.IsNullOrEmpty(comp.Description))
                    {
                        prompt.AppendLine($"   Description: {comp.Description}");
                    }

                    if (!string.IsNullOrEmpty(comp.QueryText))
                    {
                        prompt.AppendLine($"   SQL Query: {comp.QueryText}");
                    }
                }
                prompt.AppendLine();
            }

            // Task
            // Task - Simple but Insightful
            // Task - Generic and Schema-Agnostic
            prompt.AppendLine("TASK:");
            prompt.AppendLine("Suggest up to 8 NEW visual components based on the ACTUAL tables and columns in the provided schema.");
            prompt.AppendLine();
            //prompt.AppendLine("═══════════════════════════════════════════════════════════");
            //prompt.AppendLine("📊 BAR CHARTS - Comparisons & Rankings and other which fit bar charts");
            //prompt.AppendLine("═══════════════════════════════════════════════════════════");
            //prompt.AppendLine("🥧 PIE/DONUT CHARTS - Distributions");

            //prompt.AppendLine("═══════════════════════════════════════════════════════════");
            //prompt.AppendLine("📈 LINE/AREA CHARTS - Trends Over Time");

            prompt.AppendLine("═══════════════════════════════════════════════════════════");
            prompt.AppendLine("📋 TABLES - Detailed Lists");
            prompt.AppendLine("═══════════════════════════════════════════════════════════");
            prompt.AppendLine("RULES:");
            prompt.AppendLine("═══════════════════════════════════════════════════════════");
            prompt.AppendLine("1. Use ONLY tables and columns from the schema provided");
            prompt.AppendLine("2. generate only tables or bar charts ");
            prompt.AppendLine("3. Keep SQL simple as possible");
            prompt.AppendLine("4. use column aliases: COUNT(*) as Total, SUM(Amount) as Value when applicable");
            
            //prompt.AppendLine("5. make most of the suggestion tables instead of charts");
            //prompt.AppendLine("   - Bar: [category] as label, [value] as value");
            //prompt.AppendLine("   - Pie/Donut: [segment] as label, [count/sum] as value");
            //prompt.AppendLine("   - Line: [date] as label, [metric] as value");
            prompt.AppendLine("5. Create business-friendly title");
            prompt.AppendLine();

            prompt.AppendLine("Return suggestions as JSON array following the format specified in the system prompt.");

            return prompt.ToString();
        }

        /// <summary>
        /// Regenerates a single failed component with new title, SQL, and optionally different type
        /// </summary>
        public async Task<AssistantSuggestionResponse> RegenerateComponentAsync(RegenerateComponentRequest request)
        {
            try
            {
                if (request.DatabaseId <= 0)
                {
                    return new AssistantSuggestionResponse
                    {
                        Success = false,
                        Message = "Invalid database ID",
                        Suggestions = new List<ComponentSuggestion>()
                    };
                }

                var schema = await _schemaService.GetSchemaObject(request.DatabaseId, useCache: true);
                if (schema == null)
                {
                    return new AssistantSuggestionResponse
                    {
                        Success = false,
                        Message = "Could not retrieve database schema",
                        Suggestions = new List<ComponentSuggestion>()
                    };
                }

                // NEW: Get database type
                var database = await _databaseService.GetDatabaseByIdAsync(request.DatabaseId);
                string databaseType = database?.DatabaseTypeName ?? "SQL Server";

                // Pass component type AND database type to system prompt
                var systemPrompt = BuildRegenerateSystemPrompt(request.DataViewingTypeID, request.ChartType, databaseType);
                var userPrompt = BuildRegenerateUserPrompt(request, schema, databaseType);

                var llmResponse = await _llmService.GenerateDashboardSuggestionsAsync(systemPrompt, userPrompt);

                if (string.IsNullOrWhiteSpace(llmResponse))
                {
                    return new AssistantSuggestionResponse
                    {
                        Success = false,
                        Message = "LLM returned empty response",
                        Suggestions = new List<ComponentSuggestion>()
                    };
                }

                var suggestions = ParseLLMResponse(llmResponse);

                // Take only the first suggestion and enforce same type
                var replacement = suggestions.FirstOrDefault();
                if (replacement != null)
                {
                    // Enforce same type (in case LLM didn't follow instructions)
                    replacement.DataViewingTypeID = request.DataViewingTypeID;
                    if (request.DataViewingTypeID == 4)
                    {
                        replacement.ChartType = request.ChartType;
                    }
                    replacement.GridWidth = request.GridWidth;
                    replacement.GridHeight = request.GridHeight;
                }

                return new AssistantSuggestionResponse
                {
                    Success = replacement != null,
                    Message = replacement != null
                        ? "Generated replacement component"
                        : "Could not generate replacement",
                    Suggestions = replacement != null
                        ? new List<ComponentSuggestion> { replacement }
                        : new List<ComponentSuggestion>()
                };
            }
            catch (Exception ex)
            {
                return new AssistantSuggestionResponse
                {
                    Success = false,
                    Message = $"Error regenerating component: {ex.Message}",
                    Suggestions = new List<ComponentSuggestion>()
                };
            }
        }

        private string BuildRegenerateSystemPrompt(int dataViewingTypeId, string chartType, string databaseType)
        {
            string componentTypeName = dataViewingTypeId switch
            {
                1 => "Table",
                2 => "Label/Text",
                3 => "KPI/Number",
                4 => "Chart",
                _ => "Component"
            };

            string chartTypeInfo = dataViewingTypeId == 4 && !string.IsNullOrEmpty(chartType)
                ? $" (Chart Type: {chartType})"
                : "";

            var sb = new StringBuilder();

            sb.AppendLine($@"You are an expert business intelligence analyst. Your task is to generate ONE replacement dashboard component.

The previous component failed or the user wants a different one. Generate a COMPLETELY DIFFERENT component but keep the SAME TYPE.

CRITICAL RULES:
1. Component type MUST be: {componentTypeName}{chartTypeInfo}
2. dataViewingTypeID MUST be: {dataViewingTypeId}
{(dataViewingTypeId == 4 ? $"3. chartType MUST be: \"{chartType}\"" : "")}

4. Provide a new, meaningful title
5. SQL must be valid and return data according to provided database type and schema structure");

            // NEW: Add database-specific syntax rules
            sb.AppendLine();
            sb.AppendLine("## DATABASE SYNTAX RULES - CRITICAL");
            sb.AppendLine();
            sb.AppendLine($"Target Database: {databaseType}");
            sb.AppendLine();
            sb.AppendLine(GetDbSyntaxGuidance(databaseType));
            sb.AppendLine();

            sb.AppendLine($@"Output Format:
Return ONLY a valid JSON array with exactly ONE component:
[
  {{
    ""title"": ""New Component Title"",
    ""description"": ""What business insight this provides"",
    ""icon"": ""chart-line"",
    ""dataViewingTypeID"": {dataViewingTypeId},
    {(dataViewingTypeId == 4 ? $"\"chartType\": \"{chartType}\"," : "\"chartType\": null,")}
    ""sqlTemplate"": ""SELECT ... FROM ... WHERE ..."",
    ""gridWidth"": 6,
    ""gridHeight"": 4
  }}
]");

            return sb.ToString();
        }

        private string BuildRegenerateUserPrompt(RegenerateComponentRequest request, DatabaseSchema schema, string databaseType)
        {
            var prompt = new StringBuilder();

            string componentTypeName = request.DataViewingTypeID switch
            {
                1 => "Table",
                2 => "Label/Text",
                3 => "KPI/Number",
                4 => "Chart",
                _ => "Component"
            };

            bool isUserRequested = request.ErrorMessage == "User requested different component";

            if (isUserRequested)
            {
                prompt.AppendLine($"Generate ONE alternative {componentTypeName} component to replace the current one.");
                prompt.AppendLine("The user wants a DIFFERENT visualization or insight, but SAME component type.");
            }
            else
            {
                prompt.AppendLine($"Generate ONE replacement {componentTypeName} component for a failed dashboard component.");
            }
            prompt.AppendLine();

            // NEW: Remind about database type
            prompt.AppendLine($"**IMPORTANT: Generate SQL queries valid for {databaseType}** and schema structure");
            prompt.AppendLine();

            // Component type constraint
            prompt.AppendLine("COMPONENT TYPE (MUST KEEP SAME):");
            prompt.AppendLine($"- Type: {componentTypeName}");
            prompt.AppendLine($"- dataViewingTypeID: {request.DataViewingTypeID}");
            if (request.DataViewingTypeID == 4 && !string.IsNullOrEmpty(request.ChartType))
            {
                prompt.AppendLine($"- chartType: {request.ChartType}");
            }
            prompt.AppendLine();

            // Current/Failed component info
            prompt.AppendLine("CURRENT COMPONENT:");
            prompt.AppendLine($"- Title: {request.FailedTitle}");
            prompt.AppendLine($"- SQL: {request.FailedSql}");
            if (!isUserRequested)
            {
                prompt.AppendLine($"- Error: {request.ErrorMessage}");
            }
            prompt.AppendLine();

            // Grid constraints
            prompt.AppendLine("GRID POSITION (keep these values):");
            prompt.AppendLine($"- gridWidth: {request.GridWidth}");
            prompt.AppendLine($"- gridHeight: {request.GridHeight}");
            prompt.AppendLine();

            // Existing titles to avoid
            if (request.ExistingTitles?.Any() == true)
            {
                prompt.AppendLine("EXISTING TITLES (avoid duplicates):");
                foreach (var title in request.ExistingTitles)
                {
                    prompt.AppendLine($"- {title}");
                }
                prompt.AppendLine();
            }

            // Database schema
            prompt.AppendLine("DATABASE SCHEMA:");
            foreach (var table in schema.Tables.Take(15))
            {
                prompt.AppendLine($"- {table.DBName ?? table.FriendlyName}");
                if (table.Columns != null && table.Columns.Any())
                {
                    var columns = table.Columns.Take(10).Select(c => $"{c.DBName ?? c.FriendlyName} ({c.DataType})");
                    prompt.AppendLine($"  Columns: {string.Join(", ", columns)}");
                }
            }
            prompt.AppendLine();

            prompt.AppendLine($"Generate ONE new {componentTypeName} with completely different SQL but SAME type.");

            return prompt.ToString();
        }

        /// <summary>
        /// Returns database-specific SQL syntax guidance for the LLM.
        /// </summary>
        private string GetDbSyntaxGuidance(string databaseType)
        {
            var dbType = databaseType?.ToLower() ?? "sql server";

            if (dbType.Contains("mysql"))
            {
                return @"**MySQL Syntax Rules - YOU MUST FOLLOW:**
- Use `LIMIT N` to restrict rows (e.g., `SELECT * FROM table LIMIT 10`)
- Use `DATE_FORMAT(date, '%Y-%m')` for date formatting
- Use `NOW()` or `CURDATE()` for current date/time
- Use `IFNULL(column, default)` for null handling
- Use `CONCAT(str1, str2)` for string concatenation
- Use `YEAR(date)`, `MONTH(date)`, `DAY(date)` for date parts
- Use `DATEDIFF(date1, date2)` for date difference (returns days)
- Boolean: Use `TRUE`/`FALSE` or `1`/`0`
- Use `DATE_ADD(date, INTERVAL 1 DAY)` for date arithmetic
- DO NOT use: TOP, GETDATE(), ISNULL(), FORMAT()";
            }
            else if (dbType.Contains("oracle"))
            {
                return @"**Oracle Syntax Rules - YOU MUST FOLLOW:**
- Use `FETCH FIRST N ROWS ONLY` to restrict rows
- Or use `WHERE ROWNUM <= N` for older Oracle versions
- Use `TO_CHAR(date, 'YYYY-MM')` for date formatting
- Use `SYSDATE` for current date/time
- Use `NVL(column, default)` for null handling
- Use `||` for string concatenation
- Use `EXTRACT(YEAR FROM date)` for date parts
- Use `ADD_MONTHS(date, 1)` for date arithmetic
- Every SELECT must have FROM (use `FROM DUAL` for constants)
- String literals: Use single quotes only
- DO NOT use: TOP, LIMIT, GETDATE(), ISNULL(), DATE_FORMAT()";
            }
            else if (dbType.Contains("postgres"))
            {
                return @"**PostgreSQL Syntax Rules - YOU MUST FOLLOW:**
- Use `LIMIT N` to restrict rows (e.g., `SELECT * FROM table LIMIT 10`)
- Use `TO_CHAR(date, 'YYYY-MM')` for date formatting
- Use `NOW()` or `CURRENT_DATE` for current date/time
- Use `COALESCE(column, default)` for null handling
- Use `||` or `CONCAT(str1, str2)` for string concatenation
- Use `EXTRACT(YEAR FROM date)` or `DATE_PART('year', date)` for date parts
- Boolean: Use `TRUE`/`FALSE`
- Use `INTERVAL '1 day'` for date arithmetic (e.g., `date + INTERVAL '1 day'`)
- Use `::` for type casting (e.g., `'2023-01-01'::date`)
- Use `ILIKE` for case-insensitive pattern matching
- String literals: Use single quotes only
- DO NOT use: TOP, GETDATE(), ISNULL(), FORMAT(), + for string concatenation
- JSON support: Use `->>` for JSON extraction, `@?` for JSON path queries";
            }
            else // Default: SQL Server
            {
                return @"**SQL Server Syntax Rules - YOU MUST FOLLOW:**
- Use `TOP N` to restrict rows (e.g., `SELECT TOP 10 * FROM table`)
- Use `FORMAT(date, 'yyyy-MM')` for date formatting
- Use `GETDATE()` for current date/time
- Use `ISNULL(column, default)` for null handling
- Use `+` or `CONCAT(str1, str2)` for string concatenation
- Use `YEAR(date)`, `MONTH(date)`, `DAY(date)` for date parts
- Use `DATEDIFF(day, date1, date2)` for date difference
- Use `DATEADD(day, 1, date)` for date arithmetic
- Boolean: Use `1`/`0` (no TRUE/FALSE)
- String literals: Use single quotes only
- Use `LIKE` with `%` wildcard
- DO NOT use: LIMIT, NOW(), IFNULL(), DATE_FORMAT(), NVL(), FETCH FIRST";
            }
        }

        private List<ComponentSuggestion> ParseLLMResponse(string llmResponse)
        {
            try
            {
                var cleanedResponse = llmResponse.Trim();

                if (cleanedResponse.StartsWith("```json"))
                {
                    cleanedResponse = cleanedResponse.Substring(7);
                }
                else if (cleanedResponse.StartsWith("```"))
                {
                    cleanedResponse = cleanedResponse.Substring(3);
                }

                if (cleanedResponse.EndsWith("```"))
                {
                    cleanedResponse = cleanedResponse.Substring(0, cleanedResponse.Length - 3);
                }

                cleanedResponse = cleanedResponse.Trim();

                var startIndex = cleanedResponse.IndexOf('[');
                var endIndex = cleanedResponse.LastIndexOf(']');

                if (startIndex >= 0 && endIndex > startIndex)
                {
                    cleanedResponse = cleanedResponse.Substring(startIndex, endIndex - startIndex + 1);
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var suggestions = JsonSerializer.Deserialize<List<ComponentSuggestion>>(cleanedResponse, options);

                return suggestions?
                    .Where(s => !string.IsNullOrEmpty(s.Title) && !string.IsNullOrEmpty(s.SqlTemplate))
                    .Take(5)
                    .ToList() ?? new List<ComponentSuggestion>();
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Failed to parse LLM response as JSON: {ex.Message}");
                Console.WriteLine($"Response was: {llmResponse}");
                return new List<ComponentSuggestion>();
            }
        }

        private string GetComponentTypeName(int typeId)
        {
            return typeId switch
            {
                (int)DataViewingTypeEnum.Table => "Table",
                (int)DataViewingTypeEnum.Label => "Label",
                (int)DataViewingTypeEnum.Number => "KPI/Number",
                (int)DataViewingTypeEnum.Chart => "Chart",
                (int)DataViewingTypeEnum.Card => "Card",
                _ => "Unknown"
            };
        }
    }
}