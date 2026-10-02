using DynamicDasboardWebAPI.Services.LLM;
using DynamicDasboardWebAPI.Utilities;
using DynamicDashboardCommon.Enums;
using DynamicDashboardCommon.Helper;

using DynamicDashboardCommon.Models;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace DynamicDasboardWebAPI.Services
{
    /// <summary>
    /// Service for generating dashboards using LLM with intelligent component suggestions.
    /// Templates define LAYOUT (positions), LLM generates CONTENT (titles, queries, chart types).
    /// </summary>
    public class DashboardGenerationService : IDashboardGenerationService
    {
        private readonly ILLMService _llmService;
        private readonly DatabaseSchemaService _schemaService;
        //private readonly IDatabaseService _databaseService
            private readonly DatabaseService _databaseService;
        private readonly ILogsService _logsService;
        private readonly string _templatesFilePath;
        private readonly IConfiguration _configuration;
        private readonly bool _useMockData;

        // Option A: check (and repair) every generated query before the dashboard is returned
        private readonly IComponentSqlRepairService _repairService;
        private readonly bool _validateGeneratedSql;
        private readonly int _maxParallelValidations;
        private readonly int _maxGenerationRepairs;
        private readonly TimeSpan _generationRepairBudget;

        public DashboardGenerationService(
            ILLMService llmService,
            DatabaseSchemaService schemaService,
            DatabaseService databaseService,
            ILogsService logsService,
            string templatesFilePath,
            IConfiguration configuration,
            IComponentSqlRepairService repairService)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _logsService = logsService ?? throw new ArgumentNullException(nameof(logsService));
            _templatesFilePath = templatesFilePath ?? throw new ArgumentNullException(nameof(templatesFilePath));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Read mock mode from configuration (default: false for production)
            _useMockData = _configuration.GetValue<bool>("Dashboard:UseMockData", false);

            _repairService = repairService ?? throw new ArgumentNullException(nameof(repairService));
            _validateGeneratedSql = _configuration.GetValue<bool>("DashboardAI:ValidateGeneratedSql", true);
            _maxParallelValidations = Math.Max(1, _configuration.GetValue<int>("DashboardAI:MaxParallelValidations", 4));
            _maxGenerationRepairs = Math.Max(0, _configuration.GetValue<int>("DashboardAI:MaxGenerationRepairs", 6));
            _generationRepairBudget = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue<int>("DashboardAI:GenerationRepairBudgetSeconds", 120)));
        }

        #region Public Methods

        /// <summary>
        /// Generates dashboard suggestions using default executive template.
        /// </summary>
        public async Task<List<DashboardModel>> GenerateDashboardSuggestionsAsync(int databaseId)
        {
            return await GenerateDashboardSuggestionsAsync(databaseId, "executive-standard");
        }

        /// <summary>
        /// Generates a dashboard using template layout and LLM-generated content.
        /// Template defines positions (grid layout), LLM generates content (titles, queries, chart types).
        /// </summary>
        /// <param name="databaseId">Target database ID to analyze schema from</param>
        /// <param name="templateId">Template ID defining layout and AI guidance</param>
        /// <returns>List containing generated dashboard with intelligent components</returns>
        public async Task<List<DashboardModel>> GenerateDashboardSuggestionsAsync(int databaseId, string templateId)
        {
            try
            {
                // 1. Load template
                var template = DashboardTemplateHelper.GetTemplateById(templateId, _templatesFilePath);
                if (template == null)
                {
                    throw new ArgumentException($"Template '{templateId}' not found");
                }

                string llmResponse;

                if (_useMockData)
                {
                    // Use mock data for testing
                    llmResponse = GetMockLLMResponse(template);
                    await Task.Delay(500); // Simulate network delay
                }
                else
                {
                    // 2. Get database info to get DatabaseTypeName
                    var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                    if (database == null)
                    {
                        throw new ArgumentException($"Database with ID {databaseId} not found");
                    }

                    // 3. Get database schema
                    var schemaObj = await _schemaService.GetSchemaObject(databaseId);
                    if (schemaObj == null)
                    {
                        throw new ArgumentException($"Schema not found for database ID {databaseId}");
                    }

                    string optimizedSchema = _schemaService.BuildOptimizedSchemaString(schemaObj);

                    // 4. Get database type from Database model
                    string databaseType = database.DatabaseTypeName ?? "SQL Server";

                    // 5. Build intelligent prompts
                    var (systemPrompt, userPrompt) = BuildIntelligentPrompts(template, optimizedSchema, databaseType);

                    // 6. Call LLM
                    llmResponse = await _llmService.GenerateDashboardSuggestionsAsync(systemPrompt, userPrompt);
                }

                // 7. Parse response and build dashboard
                var dashboard = ParseAndBuildDashboard(llmResponse, template, databaseId);

                // 8. Option A: test-run every query and repair failing or empty ones.
                //    Skipped in mock mode, whose sample SQL targets a test table.
                if (!_useMockData && _validateGeneratedSql)
                {
                    await ValidateAndRepairComponentsAsync(dashboard, databaseId);
                }

                return new List<DashboardModel> { dashboard };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to generate dashboard: {ex.Message}", ex);
            }
        }

        #endregion

        #region Prompt Building

        /// <summary>
        /// Builds intelligent system and user prompts for LLM dashboard generation.
        /// </summary>
        private (string systemPrompt, string userPrompt) BuildIntelligentPrompts(
            DashboardTemplate template,
            string schema,
            string databaseType)
        {
            // ============================================
            // SYSTEM PROMPT
            // ============================================
            var systemSb = new StringBuilder();

            systemSb.AppendLine("You are an expert Business Intelligence analyst and dashboard designer.");
            systemSb.AppendLine("Your task is to analyze a database schema and generate meaningful dashboard components.");
            systemSb.AppendLine();
            systemSb.AppendLine("CRITICAL RULES:");
            systemSb.AppendLine("1. Generate ONLY valid JSON output - no markdown, no explanations");
            systemSb.AppendLine("2. All SQL queries MUST be valid for the specified database type");
            systemSb.AppendLine("3. Titles must be specific and derived from actual data columns");
            systemSb.AppendLine("4. DO NOT use generic titles like 'Total Records' or 'Main Chart'");
            systemSb.AppendLine("5. Each component must provide meaningful business insights");

            string systemPrompt = systemSb.ToString();

            // ============================================
            // USER PROMPT
            // ============================================
            var userSb = new StringBuilder();

            // Section 1: Dashboard Context
            userSb.AppendLine("## DASHBOARD CONTEXT");
            userSb.AppendLine();
            userSb.AppendLine($"**Dashboard Type:** {template.Name}");
            userSb.AppendLine($"**Category:** {template.Category}");
            userSb.AppendLine($"**Description:** {template.Description}");
            userSb.AppendLine();

            // AI Guidance from template
            if (template.AIGuidance != null)
            {
                userSb.AppendLine($"**Target Audience:** {template.AIGuidance.TargetAudience}");
                userSb.AppendLine();

                if (template.AIGuidance.FocusAreas?.Any() == true)
                {
                    userSb.AppendLine("**Focus Areas:**");
                    foreach (var area in template.AIGuidance.FocusAreas)
                    {
                        userSb.AppendLine($"  - {area}");
                    }
                    userSb.AppendLine();
                }

                if (template.AIGuidance.TimeGranularity?.Any() == true)
                {
                    userSb.AppendLine($"**Preferred Time Granularity:** {string.Join(", ", template.AIGuidance.TimeGranularity)}");
                    userSb.AppendLine();
                }

                if (template.AIGuidance.AvoidPatterns?.Any() == true)
                {
                    userSb.AppendLine("**Patterns to AVOID:**");
                    foreach (var pattern in template.AIGuidance.AvoidPatterns)
                    {
                        userSb.AppendLine($"  - {pattern}");
                    }
                    userSb.AppendLine();
                }

                if (!string.IsNullOrEmpty(template.AIGuidance.AdditionalGuidance))
                {
                    userSb.AppendLine($"**Additional Guidance:** {template.AIGuidance.AdditionalGuidance}");
                    userSb.AppendLine();
                }
            }

            // Section 2: Database Syntax Rules
            userSb.AppendLine("## DATABASE SYNTAX RULES");
            userSb.AppendLine();
            userSb.AppendLine($"**Target Database:** {databaseType}");
            userSb.AppendLine();
            userSb.AppendLine(SqlDialectGuidance.For(databaseType));
            userSb.AppendLine();

            // Section 3: Database Schema
            userSb.AppendLine("## DATABASE SCHEMA");
            userSb.AppendLine();
            userSb.AppendLine("Analyze the following schema to understand available tables, columns, and relationships:");
            userSb.AppendLine();
            userSb.AppendLine("```");
            userSb.AppendLine(schema);
            userSb.AppendLine("```");
            userSb.AppendLine();

            // Section 4: Component Requirements
            userSb.AppendLine("## COMPONENTS TO GENERATE");
            userSb.AppendLine();

            var kpiCount = template.Components.Count(c => c.Type.Equals("kpi", StringComparison.OrdinalIgnoreCase));
            var chartCount = template.Components.Count(c => c.Type.Equals("chart", StringComparison.OrdinalIgnoreCase));
            var tableCount = template.Components.Count(c => c.Type.Equals("table", StringComparison.OrdinalIgnoreCase));

            userSb.AppendLine($"Generate the following components based on the schema and dashboard context:");
            userSb.AppendLine();
            userSb.AppendLine($"- **{kpiCount} KPIs:** Key metrics displayed as single numbers");
            userSb.AppendLine($"- **{chartCount} Charts:** Visualizations (YOU decide the best chart type: line, bar, pie, area, donut)");
            userSb.AppendLine($"- **{tableCount} Table(s):** Data tables showing detailed records");
            userSb.AppendLine();

            userSb.AppendLine("**Slot Assignments:**");
            foreach (var slot in template.Components.OrderBy(c => c.Slot))
            {
                userSb.AppendLine($"  - Slot {slot.Slot}: {slot.Type.ToUpper()}");
            }
            userSb.AppendLine();

            // Section 5: Output Requirements
            userSb.AppendLine("## OUTPUT REQUIREMENTS");
            userSb.AppendLine();
            userSb.AppendLine("For each slot, generate:");
            userSb.AppendLine();
            userSb.AppendLine("**For KPIs:**");
            userSb.AppendLine("  - `title`: Descriptive title based on actual data (e.g., 'Total Test Executions')");
            userSb.AppendLine("  - `description`: Brief explanation of what this KPI shows");
            userSb.AppendLine("  - `queryText`: SQL query that returns a SINGLE numeric value");
            userSb.AppendLine("  - `queryIntent`: Explanation of what the query measures");
            userSb.AppendLine();
            userSb.AppendLine("**For Charts:**");
            userSb.AppendLine("  - `title`: Descriptive title based on actual data");
            userSb.AppendLine("  - `description`: What insight this chart provides");
            userSb.AppendLine("  - `chartType`: Choose the BEST type based on data pattern:");
            userSb.AppendLine("      - `line`: For trends over time (requires date/datetime column)");
            userSb.AppendLine("      - `bar`: For comparing categories or rankings");
            userSb.AppendLine("      - `pie` or `donut`: For showing distribution/proportions (max 7 categories)");
            userSb.AppendLine("      - `area`: For cumulative trends");
            userSb.AppendLine("  - `queryText`: SQL query returning data suitable for the chart type");
            userSb.AppendLine("  - `queryIntent`: Explanation of the visualization purpose");
            userSb.AppendLine();
            userSb.AppendLine("**For Tables:**");
            userSb.AppendLine("  - `title`: Descriptive title");
            userSb.AppendLine("  - `description`: What data this table shows");
            userSb.AppendLine("  - `queryText`: SQL query with relevant columns");
            userSb.AppendLine("  - `queryIntent`: Explanation of what records are displayed");
            userSb.AppendLine();

            // Section 6: SQL Guidelines
            userSb.AppendLine("## SQL QUERY GUIDELINES");
            userSb.AppendLine();
            userSb.AppendLine("1. **Column Aliasing:** Always use meaningful aliases (e.g., `COUNT(*) as TotalCount`)");
            userSb.AppendLine("2. **Chart Queries:** Must return appropriate columns:");
            userSb.AppendLine("   - Line/Area: `label` (date) and `value` (numeric)");
            userSb.AppendLine("   - Bar: `category` and `value`");
            userSb.AppendLine("   - Pie: `segment` and `value`");
            userSb.AppendLine("3. **KPI Queries:** Return single value with alias");
            userSb.AppendLine("4. **Table Queries:** Include ORDER BY when applicable");
            userSb.AppendLine();

            // Section 7: Response Format
            userSb.AppendLine("## RESPONSE FORMAT");
            userSb.AppendLine();
            userSb.AppendLine("Respond with ONLY valid JSON (no markdown, no explanation):");
            userSb.AppendLine();
            userSb.AppendLine(@"{
  ""dashboardTitle"": ""<Meaningful title based on data>"",
  ""dashboardDescription"": ""<Description of what this dashboard shows>"",
  ""components"": [
    {
      ""slot"": 1,
      ""title"": ""<Component title>"",
      ""description"": ""<Component description>"",
      ""chartType"": ""<line|bar|pie|donut|area|null>"",
      ""queryText"": ""<Valid SQL query>"",
      ""queryIntent"": ""<What this component shows>""
    }
  ]
}");

            string userPrompt = userSb.ToString();

            return (systemPrompt, userPrompt);
        }

        #endregion

        #region Response Parsing

        /// <summary>
        /// Parses the LLM response and builds a DashboardModel.
        /// </summary>
        private DashboardModel ParseAndBuildDashboard(string llmResponse, DashboardTemplate template, int databaseId)
        {
            try
            {
                // Clean response
                string cleanJson = CleanLLMResponse(llmResponse);

                // Parse JSON
                var jsonDoc = JsonDocument.Parse(cleanJson);
                var root = jsonDoc.RootElement;

                // Create dashboard
                var dashboard = new DashboardModel
                {
                    Title = GetJsonString(root, "dashboardTitle") ?? template.Name,
                    Description = GetJsonString(root, "dashboardDescription") ?? template.Description,
                    DatabaseID = databaseId,
                    CategoryID = GetCategoryId(template.Category),
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow,
                    IsAIGenerated = true,
                    ValidationStatus = DashboardValidationStatus.Draft,
                    SharingStatus = DashboardSharingStatus.Private,
                    Components = new List<DashboardComponent>()
                };

                // Parse components
                if (root.TryGetProperty("components", out var componentsArray))
                {
                    foreach (var llmComp in componentsArray.EnumerateArray())
                    {
                        int slot = llmComp.GetProperty("slot").GetInt32();

                        // Find matching template slot for position
                        var templateSlot = template.Components.FirstOrDefault(s => s.Slot == slot);
                        if (templateSlot == null) continue;

                        // Get chart type from LLM response
                        string chartType = GetJsonString(llmComp, "chartType");

                        var component = new DashboardComponent
                        {
                            ComponentID = slot,

                            // Content from LLM
                            Title = GetJsonString(llmComp, "title") ?? $"Component {slot}",
                            Description = GetJsonString(llmComp, "description") ?? "",
                            QueryText = GetJsonString(llmComp, "queryText") ?? "",
                            QueryIntent = GetJsonString(llmComp, "queryIntent") ?? "",
                            ChartType = chartType,

                            // Type from template
                            DataViewingTypeID = GetDataViewingTypeId(templateSlot.Type),

                            // Position from template
                            GridX = templateSlot.GridX,
                            GridY = templateSlot.GridY,
                            GridWidth = templateSlot.GridWidth,
                            GridHeight = templateSlot.GridHeight,

                            // Visualization config
                            VisualizationConfig = BuildVisualizationConfig(templateSlot.Type, chartType),

                            // Default values
                            FilterExpression = "",
                            RefreshInterval = 0,
                            IsAIGenerated = true,
                            IsValidated = false,
                            IsVisible = true,
                            CreatedAt = DateTime.UtcNow,
                            LastUpdated = DateTime.UtcNow
                        };

                        dashboard.Components.Add(component);
                    }
                }

                return dashboard;
            }
            catch (JsonException ex)
            {
                throw new Exception($"Failed to parse LLM response: {ex.Message}. Response: {llmResponse}", ex);
            }
        }

        /// <summary>
        /// Cleans LLM response by removing markdown code fences.
        /// </summary>
        private string CleanLLMResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                throw new ArgumentException("LLM response is empty.");
            }

            string cleanJson = response.Trim();

            // Remove ```json and ``` if present
            if (cleanJson.StartsWith("```"))
            {
                int firstNewline = cleanJson.IndexOf('\n');
                if (firstNewline > 0)
                {
                    cleanJson = cleanJson.Substring(firstNewline + 1);
                }

                if (cleanJson.EndsWith("```"))
                {
                    cleanJson = cleanJson.Substring(0, cleanJson.LastIndexOf("```"));
                }

                cleanJson = cleanJson.Trim();
            }

            return cleanJson;
        }

        /// <summary>
        /// Generates suggested questions based on database schema
        /// </summary>
        public async Task<List<string>> GenerateSuggestedQuestionsAsync(int databaseId)
        {
            try
            {
                // Get schema
                var schemaObj = await _schemaService.GetSchemaObject(databaseId);
                if (schemaObj == null)
                    return new List<string>();

                string schema = _schemaService.BuildOptimizedSchemaString(schemaObj);

                // Build prompt
                string prompt = $@"Based on this database schema, suggest exactly 20 business questions a user might want to visualize in a dashboard chart.

Schema:
{schema}

Rules:
- Questions should be practical and business-focused
- Include a mix of: totals, trends over time, comparisons, rankings, distributions
- Keep questions short and clear (under 15 words each)
- Return ONLY a JSON array of strings, no markdown, no explanation

Example format:
[""Show total sales by month"", ""Top 10 customers by revenue"", ""Order status distribution""]";

                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                // Clean response and parse JSON
                string cleanJson = response.Trim();
                if (cleanJson.StartsWith("```"))
                {
                    int start = cleanJson.IndexOf('[');
                    int end = cleanJson.LastIndexOf(']') + 1;
                    if (start >= 0 && end > start)
                    {
                        cleanJson = cleanJson.Substring(start, end - start);
                    }
                }

                var questions = JsonSerializer.Deserialize<List<string>>(cleanJson);
                return questions ?? new List<string>();
            }
            catch (Exception)
            {
                // Return empty list on error
                return new List<string>();
            }
        }

        #endregion

        #region Generated SQL Validation (Option A)

        /// <summary>
        /// Option A: every generated query is test-run (1 row) before the dashboard is returned.
        /// Queries that fail or return no rows are repaired by the shared ComponentSqlRepairService
        /// (same title, purpose and type), so the dashboard arrives with working components.
        ///
        /// - Checks run in parallel (DashboardAI:MaxParallelValidations). The first check runs alone
        ///   so the database's connection details are cached before parallel connections open.
        /// - Repairs run one at a time, because each one calls the LLM.
        /// - Repairs stop after DashboardAI:MaxGenerationRepairs components or once
        ///   DashboardAI:GenerationRepairBudgetSeconds have passed, so generation stays well within
        ///   the frontend's request timeout. Anything left is shown on its card in the builder,
        ///   with "Fix with AI".
        /// - This step never fails the generation: on an unexpected error the dashboard is returned
        ///   as generated and the problem is logged.
        /// </summary>
        private async Task ValidateAndRepairComponentsAsync(DashboardModel dashboard, int databaseId)
        {
            try
            {
                var components = dashboard.Components?
                    .Where(c => c.DataViewingTypeID != (int)DataViewingTypeEnum.Label && !string.IsNullOrWhiteSpace(c.QueryText))
                    .ToList() ?? new List<DashboardComponent>();

                if (!components.Any())
                {
                    return;
                }

                // 1. Check every query
                var checks = new List<GeneratedQueryCheck>
                {
                    await CheckComponentAsync(components[0], databaseId)
                };

                using (var throttler = new SemaphoreSlim(_maxParallelValidations))
                {
                    var parallelChecks = components.Skip(1).Select(async component =>
                    {
                        await throttler.WaitAsync();
                        try
                        {
                            return await CheckComponentAsync(component, databaseId);
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }).ToList();

                    checks.AddRange(await Task.WhenAll(parallelChecks));
                }

                // 2. Repair the ones that failed or returned no rows (one at a time: each calls the LLM)
                var failedChecks = checks.Where(c => !c.Result.IsValid).ToList();
                var repaired = 0;
                var notRepaired = 0;
                var notAttempted = 0;
                var repairTimer = System.Diagnostics.Stopwatch.StartNew();

                foreach (var check in failedChecks)
                {
                    if (repaired + notRepaired >= _maxGenerationRepairs || repairTimer.Elapsed >= _generationRepairBudget)
                    {
                        notAttempted++;
                        continue;
                    }

                    var component = check.Component;
                    var repair = await _repairService.RepairAsync(new ComponentSqlRepairRequest
                    {
                        DatabaseId = databaseId,
                        Title = component.Title,
                        Description = component.Description,
                        DataViewingTypeID = component.DataViewingTypeID,
                        ChartType = component.ChartType,
                        Sql = component.QueryText
                    });

                    if (repair.Success)
                    {
                        component.QueryText = repair.Sql;
                        component.LastUpdated = DateTime.UtcNow;
                        repaired++;
                    }
                    else
                    {
                        notRepaired++;
                    }
                }

                await LogAsync(EnumLoggingType.Information,
                    $"Dashboard generation check for \"{dashboard.Title}\" (database {databaseId}): " +
                    $"{checks.Count} queries checked, {checks.Count - failedChecks.Count} worked, {repaired} repaired, " +
                    $"{notRepaired} could not be repaired, {notAttempted} not attempted " +
                    $"(limits: {_maxGenerationRepairs} repairs, {_generationRepairBudget.TotalSeconds:0} s; repairs took {repairTimer.Elapsed.TotalSeconds:0} s).");
            }
            catch (Exception ex)
            {
                await LogAsync(EnumLoggingType.Warning,
                    $"Dashboard generation check skipped for database {databaseId}: {ex.Message}");
            }
        }

        private async Task<GeneratedQueryCheck> CheckComponentAsync(DashboardComponent component, int databaseId)
        {
            return new GeneratedQueryCheck
            {
                Component = component,
                Result = await _repairService.ValidateAsync(component.QueryText, databaseId)
            };
        }

        /// <summary>A generated component and the result of test-running its query.</summary>
        private sealed class GeneratedQueryCheck
        {
            public DashboardComponent Component { get; init; }
            public ComponentSqlValidationResult Result { get; init; }
        }

        /// <summary>Writes to the application log; logging problems never break generation.</summary>
        private async Task LogAsync(EnumLoggingType type, string message)
        {
            try
            {
                await _logsService.AddLogAsync(null, type.ToString(), message);
            }
            catch (Exception)
            {
                // Logging must not break dashboard generation.
            }
        }

        #endregion

        #region Helper Methods

        private int GetDataViewingTypeId(string slotType)
        {
            return slotType?.ToLower() switch
            {
                "kpi" => (int)DataViewingTypeEnum.Number,
                "chart" => (int)DataViewingTypeEnum.Chart,
                "table" => (int)DataViewingTypeEnum.Table,
                "card" => (int)DataViewingTypeEnum.Card,
                "label" => (int)DataViewingTypeEnum.Label,
                _ => (int)DataViewingTypeEnum.Chart
            };
        }

        private string BuildVisualizationConfig(string slotType, string chartType)
        {
            if (slotType?.ToLower() == "chart" && !string.IsNullOrEmpty(chartType))
            {
                return JsonSerializer.Serialize(new { chartType = chartType.ToLower() });
            }
            return "{}";
        }

        private int GetCategoryId(string category)
        {
            return category?.ToLower() switch
            {
                "executive" => 1,
                "operations" => 2,
                "finance" => 3,
                "analytics" => 4,
                "performance" => 5,
                "sales" => 6,
                "basic" => 7,
                _ => 1
            };
        }

        private string GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
                if (prop.ValueKind == JsonValueKind.Null)
                    return null;
            }
            return null;
        }

        #endregion

        #region Mock Data

        /// <summary>
        /// Returns mock LLM response for testing without actual LLM calls.
        /// </summary>
        private string GetMockLLMResponse(DashboardTemplate template)
        {
            var components = new List<object>();

            foreach (var slot in template.Components.OrderBy(c => c.Slot))
            {
                object comp = slot.Type.ToLower() switch
                {
                    "kpi" => new
                    {
                        slot = slot.Slot,
                        title = $"KPI Metric {slot.Slot}",
                        description = $"Key performance indicator {slot.Slot}",
                        chartType = (string)null,
                        queryText = "SELECT COUNT(*) as Value FROM TestAutomationJobs",
                        queryIntent = "Shows total count of records"
                    },
                    "chart" => new
                    {
                        slot = slot.Slot,
                        title = $"Chart {slot.Slot}",
                        description = $"Visualization {slot.Slot}",
                        chartType = slot.Slot % 3 == 0 ? "pie" : slot.Slot % 2 == 0 ? "bar" : "line",
                        queryText = "SELECT FORMAT(ExecutedAt, 'yyyy-MM') as label, COUNT(*) as value FROM TestAutomationJobs GROUP BY FORMAT(ExecutedAt, 'yyyy-MM')",
                        queryIntent = "Shows trend over time"
                    },
                    "table" => new
                    {
                        slot = slot.Slot,
                        title = "Recent Records",
                        description = "Latest data entries",
                        chartType = (string)null,
                        queryText = "SELECT TOP 15 * FROM TestAutomationJobs ORDER BY ExecutedAt DESC",
                        queryIntent = "Shows recent records"
                    },
                    _ => new
                    {
                        slot = slot.Slot,
                        title = $"Component {slot.Slot}",
                        description = "Auto-generated component",
                        chartType = (string)null,
                        queryText = "SELECT 1",
                        queryIntent = "Placeholder"
                    }
                };

                components.Add(comp);
            }

            var response = new
            {
                dashboardTitle = $"{template.Name} - Generated",
                dashboardDescription = template.Description,
                components = components
            };

            return JsonSerializer.Serialize(response);
        }

        #endregion
    }
}