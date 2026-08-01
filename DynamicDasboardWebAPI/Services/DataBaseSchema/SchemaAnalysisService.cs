using DynamicDasboardWebAPI.Services.LLM;
using DynamicDashboardCommon.Models;
using DynamicDashboardCommon.Models.SchemaAnalysis;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicDasboardWebAPI.Services
{
    /// <summary>
    /// Service for analyzing database schemas using LLM.
    /// Provides sequential analysis of tables, columns, and relationships.
    /// </summary>
    public class SchemaAnalysisService
    {
        private readonly DatabaseSchemaService _schemaService;
        private readonly DatabaseService _databaseService;
        private readonly LLMServiceFactory _llmServiceFactory;
        private readonly ILLMService _llmService;
        private readonly ILogger<SchemaAnalysisService> _logger;

        // Configuration constants - consider moving to appsettings.json
        private const int MaxTablesPerBatch = 10;
        private const int MaxColumnsPerTableBatch = 50;
        private const int DelayBetweenLLMCallsMs = 200;

        public SchemaAnalysisService(
            DatabaseSchemaService schemaService,
            DatabaseService databaseService,
            LLMServiceFactory llmServiceFactory,
            ILogger<SchemaAnalysisService> logger)
        {
            _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _llmServiceFactory = llmServiceFactory ?? throw new ArgumentNullException(nameof(llmServiceFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _llmService = _llmServiceFactory.CreateLlmService();
        }

        #region Public Analysis Methods

        /// <summary>
        /// Analyzes the full database schema (legacy method for backward compatibility)
        /// </summary>
        public async Task<SchemaAnalysisResult> AnalyzeDatabaseSchemaAsync(int databaseId)
        {
            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                if (database == null)
                {
                    return CreateErrorResult($"Database with ID {databaseId} not found");
                }

                var schemaObj = await GetOrGenerateSchemaAsync(databaseId, database);
                if (schemaObj == null)
                {
                    return CreateErrorResult("Failed to generate schema from database");
                }

                var schemaForAnalysis = _schemaService.BuildOptimizedSchemaString(schemaObj);
                var analysisResult = await AnalyzeSchemaWithLLMAsync(schemaForAnalysis, database.Name);

                return analysisResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing schema for database {DatabaseId}", databaseId);
                return CreateErrorResult($"Error analyzing schema: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyzes tables sequentially with optional force re-analysis.
        /// Returns results for UI review - does NOT auto-save.
        /// </summary>
        /// <param name="databaseId">Database ID to analyze</param>
        /// <param name="forceReanalyze">If true, re-analyze all tables regardless of existing data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task<SchemaAnalysisResult> AnalyzeTablesSmartAsync(
            int databaseId,
            bool forceReanalyze = false,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var allTableDescriptions = new List<TableDescription>();
            var errors = new List<AnalysisError>();

            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                if (database == null)
                {
                    return CreateErrorResult($"Database with ID {databaseId} not found");
                }

                var schemaObj = await GetOrGenerateSchemaAsync(databaseId, database);
                if (schemaObj?.Tables == null || !schemaObj.Tables.Any())
                {
                    return CreateErrorResult("No tables found in database schema");
                }

                // Filter tables based on forceReanalyze flag
                var tablesToAnalyze = forceReanalyze
                    ? schemaObj.Tables.Where(t => t.IsActive).ToList()
                    : schemaObj.Tables.Where(t => t.IsActive && NeedsTableAnalysis(t)).ToList();

                if (!tablesToAnalyze.Any())
                {
                    return new SchemaAnalysisResult
                    {
                        Success = true,
                        AnalysisData = new SchemaAnalysisData { TableDescriptions = new List<TableDescription>() },
                        Message = "All tables already analyzed. Use 'Force Re-analyze' to refresh.",
                        ProcessingTime = DateTime.UtcNow,
                        Duration = DateTime.UtcNow - startTime
                    };
                }

                _logger.LogInformation("Starting table analysis for {Count} tables in database {DatabaseId}",
                    tablesToAnalyze.Count, databaseId);

                // Process tables in batches sequentially
                var batches = CreateBatches(tablesToAnalyze, MaxTablesPerBatch);

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batchResult = await AnalyzeTableBatchAsync(batch, database.Name);

                    if (batchResult.Success && batchResult.AnalysisData?.TableDescriptions != null)
                    {
                        allTableDescriptions.AddRange(batchResult.AnalysisData.TableDescriptions);
                    }
                    else
                    {
                        // Record batch failure
                        foreach (var table in batch)
                        {
                            errors.Add(new AnalysisError
                            {
                                TableName = table.DBName,
                                ErrorMessage = batchResult.ErrorMessage ?? "Batch analysis failed"
                            });
                        }
                    }

                    // Rate limiting delay between batches
                    if (batches.IndexOf(batch) < batches.Count - 1)
                    {
                        await Task.Delay(DelayBetweenLLMCallsMs, cancellationToken);
                    }
                }

                return new SchemaAnalysisResult
                {
                    Success = true,
                    AnalysisData = new SchemaAnalysisData { TableDescriptions = allTableDescriptions },
                    Errors = errors.Select(e => $"{e.TableName}: {e.ErrorMessage}").ToList(),
                    Message = BuildResultMessage(allTableDescriptions.Count, errors.Count, tablesToAnalyze.Count, "tables"),
                    ProcessingTime = DateTime.UtcNow,
                    Duration = DateTime.UtcNow - startTime
                };
            }
            catch (OperationCanceledException)
            {
                return new SchemaAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "Analysis cancelled by user",
                    AnalysisData = new SchemaAnalysisData { TableDescriptions = allTableDescriptions },
                    Message = $"Cancelled after analyzing {allTableDescriptions.Count} tables"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in table analysis for database {DatabaseId}", databaseId);
                return CreateErrorResult($"Error in table analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyzes a batch of tables
        /// </summary>
        private async Task<SchemaAnalysisResult> AnalyzeTableBatchAsync(List<TableSchema> tables, string databaseName)
        {
            try
            {
                var schemaString = BuildTableBatchSchemaString(tables);
                var prompt = BuildTableAnalysisPrompt(schemaString, databaseName);
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseTableAnalysisResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze table batch");
                return CreateErrorResult($"Batch analysis failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds schema string for a batch of tables
        /// </summary>
        private string BuildTableBatchSchemaString(List<TableSchema> tables)
        {
            var sb = new StringBuilder();
            foreach (var table in tables)
            {
                sb.AppendLine($"Table: {table.DBName}");
                if (table.Columns != null && table.Columns.Any())
                {
                    var columnInfo = table.Columns.Select(c =>
                        $"{c.DBName} ({c.DataType}){(c.IsPrimaryKey ? " PK" : "")}");
                    sb.AppendLine($"Columns: {string.Join(", ", columnInfo)}");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds prompt for table analysis (FIXED: now accepts 2 parameters)
        /// </summary>
        private string BuildTableAnalysisPrompt(string schema, string databaseName)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine($"You are an expert database analyst helping improve the usability of database '{databaseName}'.");
            prompt.AppendLine("\nYour task is to analyze the tables below and provide:");
            prompt.AppendLine("1. User-friendly names for each table (clear, business-oriented)");
            prompt.AppendLine("2. Brief descriptions of what each table represents (max 50 characters)");

            prompt.AppendLine("\nDatabase Schema:");
            prompt.AppendLine(schema);

            prompt.AppendLine("\nRespond with a JSON object having the following structure:");
            prompt.AppendLine("{");
            prompt.AppendLine("  \"tableDescriptions\": [");
            prompt.AppendLine("    {");
            prompt.AppendLine("      \"tableName\": \"exact_table_name_from_schema\",");
            prompt.AppendLine("      \"suggestedName\": \"User Friendly Table Name\",");
            prompt.AppendLine("      \"suggestedDescription\": \"Brief description\"");
            prompt.AppendLine("    }");
            prompt.AppendLine("  ]");
            prompt.AppendLine("}");

            prompt.AppendLine("\nGuidelines:");
            prompt.AppendLine("1. Provide business-oriented, non-technical friendly names");
            prompt.AppendLine("2. Keep descriptions concise and meaningful");
            prompt.AppendLine("3. Analyze ALL tables provided");
            prompt.AppendLine("4. Return pure JSON only, no additional text or markdown");

            return prompt.ToString();
        }

        /// <summary>
        /// Parses table analysis response from LLM
        /// </summary>
        private SchemaAnalysisResult ParseTableAnalysisResponse(string response)
        {
            try
            {
                var cleanedJson = CleanJsonResponse(response);

                var parsedResponse = JsonSerializer.Deserialize<TableDescriptionsResponse>(
                    cleanedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsedResponse?.TableDescriptions != null)
                {
                    return new SchemaAnalysisResult
                    {
                        Success = true,
                        AnalysisData = new SchemaAnalysisData
                        {
                            TableDescriptions = parsedResponse.TableDescriptions,
                            ColumnDescriptions = new List<ColumnDescription>(),
                            PotentialConflicts = new List<PotentialConflict>(),
                            SuggestedRelationships = new List<SuggestedRelationship>(),
                            UnclearElements = new List<UnclearElement>()
                        },
                        RawLLMResponse = response
                    };
                }

                return CreateErrorResult("Failed to parse table analysis response");
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "JSON parsing error in table analysis");
                return CreateErrorResult($"JSON parsing error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error parsing table analysis");
                return CreateErrorResult($"Parsing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a table needs analysis (missing FriendlyName OR Description)
        /// </summary>
        private bool NeedsTableAnalysis(TableSchema table)
        {
            return string.IsNullOrWhiteSpace(table.FriendlyName) ||
                   string.IsNullOrWhiteSpace(table.Description);
        }

        #endregion

        #region Helper Classes for Parsing

        private class TableDescriptionsResponse
        {
            public List<TableDescription> TableDescriptions { get; set; }
        }

        #endregion

        #region Column Analysis

        /// <summary>
        /// Analyzes columns sequentially with optional force re-analysis.
        /// Returns results for UI review - does NOT auto-save.
        /// </summary>
        /// <param name="databaseId">Database ID to analyze</param>
        /// <param name="forceReanalyze">If true, re-analyze all columns regardless of existing data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task<SchemaAnalysisResult> AnalyzeColumnsSmartAsync(
            int databaseId,
            bool forceReanalyze = false,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var allColumnDescriptions = new List<ColumnDescription>();
            var errors = new List<AnalysisError>();

            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                if (database == null)
                {
                    return CreateErrorResult($"Database with ID {databaseId} not found");
                }

                var schemaObj = await GetOrGenerateSchemaAsync(databaseId, database);
                if (schemaObj?.Tables == null || !schemaObj.Tables.Any())
                {
                    return CreateErrorResult("No tables found in database schema");
                }

                // Filter tables that have columns needing analysis
                var tablesToAnalyze = schemaObj.Tables
                    .Where(t => t.IsActive && t.Columns != null && t.Columns.Any())
                    .Where(t => forceReanalyze || HasColumnsNeedingAnalysis(t))
                    .ToList();

                if (!tablesToAnalyze.Any())
                {
                    return new SchemaAnalysisResult
                    {
                        Success = true,
                        AnalysisData = new SchemaAnalysisData { ColumnDescriptions = new List<ColumnDescription>() },
                        Message = "All columns already analyzed. Use 'Force Re-analyze' to refresh.",
                        ProcessingTime = DateTime.UtcNow,
                        Duration = DateTime.UtcNow - startTime
                    };
                }

                var totalColumnsToAnalyze = tablesToAnalyze.Sum(t =>
                    forceReanalyze
                        ? t.Columns.Count
                        : t.Columns.Count(c => NeedsColumnAnalysis(c)));

                _logger.LogInformation(
                    "Starting column analysis for {TableCount} tables ({ColumnCount} columns) in database {DatabaseId}",
                    tablesToAnalyze.Count, totalColumnsToAnalyze, databaseId);

                // Process each table sequentially
                foreach (var table in tablesToAnalyze)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var tableResult = await AnalyzeTableColumnsAsync(table, database.Name, forceReanalyze);

                    if (tableResult.Success && tableResult.AnalysisData?.ColumnDescriptions != null)
                    {
                        // Deduplicate before adding
                        foreach (var colDesc in tableResult.AnalysisData.ColumnDescriptions)
                        {
                            var exists = allColumnDescriptions.Any(c =>
                                c.TableName.Equals(colDesc.TableName, StringComparison.OrdinalIgnoreCase) &&
                                c.ColumnName.Equals(colDesc.ColumnName, StringComparison.OrdinalIgnoreCase));

                            if (!exists)
                            {
                                allColumnDescriptions.Add(colDesc);
                            }
                        }
                    }
                    else
                    {
                        errors.Add(new AnalysisError
                        {
                            TableName = table.DBName,
                            ErrorMessage = tableResult.ErrorMessage ?? "Column analysis failed"
                        });
                    }

                    // Rate limiting delay between tables
                    if (tablesToAnalyze.IndexOf(table) < tablesToAnalyze.Count - 1)
                    {
                        await Task.Delay(DelayBetweenLLMCallsMs, cancellationToken);
                    }
                }

                return new SchemaAnalysisResult
                {
                    Success = true,
                    AnalysisData = new SchemaAnalysisData { ColumnDescriptions = allColumnDescriptions },
                    Errors = errors.Select(e => $"{e.TableName}: {e.ErrorMessage}").ToList(),
                    Message = BuildResultMessage(allColumnDescriptions.Count, errors.Count, totalColumnsToAnalyze, "columns"),
                    ProcessingTime = DateTime.UtcNow,
                    Duration = DateTime.UtcNow - startTime
                };
            }
            catch (OperationCanceledException)
            {
                return new SchemaAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "Analysis cancelled by user",
                    AnalysisData = new SchemaAnalysisData { ColumnDescriptions = allColumnDescriptions },
                    Message = $"Cancelled after analyzing {allColumnDescriptions.Count} columns"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in column analysis for database {DatabaseId}", databaseId);
                return CreateErrorResult($"Error in column analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyzes columns for a single table
        /// </summary>
        private async Task<SchemaAnalysisResult> AnalyzeTableColumnsAsync(
            TableSchema table,
            string databaseName,
            bool forceReanalyze)
        {
            try
            {
                // Filter columns that need analysis (unless force re-analyze)
                var columnsToAnalyze = forceReanalyze
                    ? table.Columns.ToList()
                    : table.Columns.Where(c => NeedsColumnAnalysis(c)).ToList();

                if (!columnsToAnalyze.Any())
                {
                    return new SchemaAnalysisResult
                    {
                        Success = true,
                        AnalysisData = new SchemaAnalysisData { ColumnDescriptions = new List<ColumnDescription>() }
                    };
                }

                // For large tables, process columns in batches
                if (columnsToAnalyze.Count > MaxColumnsPerTableBatch)
                {
                    return await AnalyzeColumnsInBatchesAsync(table, columnsToAnalyze, databaseName);
                }

                var prompt = BuildColumnAnalysisPrompt(table, columnsToAnalyze, databaseName);
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseColumnAnalysisResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze columns for table {TableName}", table.DBName);
                return CreateErrorResult($"Column analysis failed for {table.DBName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyzes columns in batches for tables with many columns
        /// </summary>
        private async Task<SchemaAnalysisResult> AnalyzeColumnsInBatchesAsync(
            TableSchema table,
            List<ColumnSchema> columns,
            string databaseName)
        {
            var allDescriptions = new List<ColumnDescription>();
            var columnBatches = CreateColumnBatches(columns, MaxColumnsPerTableBatch);

            foreach (var batch in columnBatches)
            {
                var prompt = BuildColumnAnalysisPrompt(table, batch, databaseName);
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);
                var result = ParseColumnAnalysisResponse(response);

                if (result.Success && result.AnalysisData?.ColumnDescriptions != null)
                {
                    allDescriptions.AddRange(result.AnalysisData.ColumnDescriptions);
                }

                // Rate limiting between batches
                if (columnBatches.IndexOf(batch) < columnBatches.Count - 1)
                {
                    await Task.Delay(DelayBetweenLLMCallsMs);
                }
            }

            return new SchemaAnalysisResult
            {
                Success = true,
                AnalysisData = new SchemaAnalysisData { ColumnDescriptions = allDescriptions }
            };
        }

        /// <summary>
        /// Builds prompt for column analysis
        /// </summary>
        private string BuildColumnAnalysisPrompt(TableSchema table, List<ColumnSchema> columns, string databaseName)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"You are an expert database analyst helping improve the usability of database '{databaseName}'.");
            sb.AppendLine($"\nAnalyze the columns for table '{table.DBName}' and provide:");
            sb.AppendLine("1. User-friendly names for each column (clear, business-oriented)");
            sb.AppendLine("2. Brief descriptions (max 30 characters)");
            sb.AppendLine("3. Identify lookup/foreign key columns");

            sb.AppendLine("\nColumns to analyze:");
            foreach (var column in columns)
            {
                var attributes = new List<string>();
                if (column.IsPrimaryKey) attributes.Add("PK");
                if (!column.IsNullable) attributes.Add("Required");

                var attrStr = attributes.Any() ? $" [{string.Join(", ", attributes)}]" : "";
                sb.AppendLine($"- {column.DBName} ({column.DataType}){attrStr}");
            }

            sb.AppendLine("\nRespond with JSON only:");
            sb.AppendLine("{");
            sb.AppendLine("  \"columnDescriptions\": [");
            sb.AppendLine("    {");
            sb.AppendLine($"      \"tableName\": \"{table.DBName}\",");
            sb.AppendLine("      \"columnName\": \"exact_column_name\",");
            sb.AppendLine("      \"suggestedName\": \"Friendly Name\",");
            sb.AppendLine("      \"suggestedDescription\": \"Brief description\",");
            sb.AppendLine("      \"isLookupColumn\": true/false");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            sb.AppendLine("\nGuidelines:");
            sb.AppendLine("1. Analyze ALL columns provided");
            sb.AppendLine("2. Set isLookupColumn=true for foreign keys (columns ending in _id, _ID, Id, etc.)");
            sb.AppendLine("3. Return pure JSON only, no markdown or additional text");

            return sb.ToString();
        }

        /// <summary>
        /// Parses column analysis response from LLM
        /// </summary>
        private SchemaAnalysisResult ParseColumnAnalysisResponse(string response)
        {
            try
            {
                var cleanedJson = CleanJsonResponse(response);

                var parsedResponse = JsonSerializer.Deserialize<ColumnDescriptionsResponse>(
                    cleanedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsedResponse?.ColumnDescriptions != null)
                {
                    return new SchemaAnalysisResult
                    {
                        Success = true,
                        AnalysisData = new SchemaAnalysisData
                        {
                            ColumnDescriptions = parsedResponse.ColumnDescriptions,
                            TableDescriptions = new List<TableDescription>(),
                            PotentialConflicts = new List<PotentialConflict>(),
                            SuggestedRelationships = new List<SuggestedRelationship>(),
                            UnclearElements = new List<UnclearElement>()
                        },
                        RawLLMResponse = response
                    };
                }

                return CreateErrorResult("Failed to parse column analysis response");
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "JSON parsing error in column analysis");
                return CreateErrorResult($"JSON parsing error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error parsing column analysis");
                return CreateErrorResult($"Parsing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a table has any columns needing analysis
        /// </summary>
        private bool HasColumnsNeedingAnalysis(TableSchema table)
        {
            if (table.Columns == null || !table.Columns.Any())
                return false;

            return table.Columns.Any(c => NeedsColumnAnalysis(c));
        }

        /// <summary>
        /// Checks if a column needs analysis (missing FriendlyName OR Description)
        /// </summary>
        private bool NeedsColumnAnalysis(ColumnSchema column)
        {
            return string.IsNullOrWhiteSpace(column.FriendlyName) ||
                   string.IsNullOrWhiteSpace(column.Description);
        }

        /// <summary>
        /// Creates batches of columns for processing
        /// </summary>
        private List<List<ColumnSchema>> CreateColumnBatches(List<ColumnSchema> columns, int batchSize)
        {
            var batches = new List<List<ColumnSchema>>();
            for (int i = 0; i < columns.Count; i += batchSize)
            {
                batches.Add(columns.Skip(i).Take(batchSize).ToList());
            }
            return batches;
        }



        private class ColumnDescriptionsResponse
        {
            public List<ColumnDescription> ColumnDescriptions { get; set; }
        }

        #region Relationship Analysis

        /// <summary>
        /// Analyzes relationships using rule-based detection + LLM for complex cases.
        /// Returns results for UI review - does NOT auto-save.
        /// </summary>
        public async Task<SchemaAnalysisResult> AnalyzeRelationshipsSmartAsync(
            int databaseId,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                if (database == null)
                {
                    return CreateErrorResult($"Database with ID {databaseId} not found");
                }

                var schemaObj = await GetOrGenerateSchemaAsync(databaseId, database);
                if (schemaObj?.Tables == null || !schemaObj.Tables.Any())
                {
                    return CreateErrorResult("No tables found in database schema");
                }

                var relationships = new List<SuggestedRelationship>();

                // Phase 1: Rule-based detection (instant, no token cost)
                var ruleBasedRelationships = DetectRelationshipsByPattern(schemaObj);
                relationships.AddRange(ruleBasedRelationships);

                _logger.LogInformation("Rule-based detection found {Count} relationships", ruleBasedRelationships.Count);

                // Phase 2: LLM for tables with potential foreign keys not caught by rules
                var tablesWithPotentialFKs = schemaObj.Tables
                    .Where(t => t.IsActive && t.Columns != null)
                    .Where(t => t.Columns.Any(c =>
                        c.DBName.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ||
                        c.DBName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                        c.DBName.EndsWith("_ID", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (tablesWithPotentialFKs.Any())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var llmRelationships = await AnalyzeRelationshipsWithLLMAsync(
                            tablesWithPotentialFKs, database.Name);

                        if (llmRelationships.Any())
                        {
                            // Merge with rule-based, avoiding duplicates
                            relationships = MergeRelationships(relationships, llmRelationships);
                            _logger.LogInformation("LLM analysis added {Count} additional relationships",
                                llmRelationships.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "LLM relationship analysis failed, using rule-based only");
                        // Continue with rule-based results
                    }
                }

                return new SchemaAnalysisResult
                {
                    Success = true,
                    AnalysisData = new SchemaAnalysisData
                    {
                        SuggestedRelationships = relationships,
                        TableDescriptions = new List<TableDescription>(),
                        ColumnDescriptions = new List<ColumnDescription>(),
                        PotentialConflicts = new List<PotentialConflict>(),
                        UnclearElements = new List<UnclearElement>()
                    },
                    Message = $"Found {relationships.Count} relationships ({ruleBasedRelationships.Count} by pattern, {relationships.Count - ruleBasedRelationships.Count} by AI)",
                    ProcessingTime = DateTime.UtcNow,
                    Duration = DateTime.UtcNow - startTime
                };
            }
            catch (OperationCanceledException)
            {
                return CreateErrorResult("Analysis cancelled by user");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in relationship analysis for database {DatabaseId}", databaseId);
                return CreateErrorResult($"Error in relationship analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Detects relationships by naming patterns (rule-based, no LLM cost)
        /// </summary>
        private List<SuggestedRelationship> DetectRelationshipsByPattern(DatabaseSchema schema)
        {
            var relationships = new List<SuggestedRelationship>();
            var tables = schema.Tables.Where(t => t.IsActive).ToList();

            foreach (var sourceTable in tables)
            {
                if (sourceTable.Columns == null) continue;

                // Find the primary key column
                var pkColumn = sourceTable.Columns.FirstOrDefault(c => c.IsPrimaryKey) ??
                               sourceTable.Columns.FirstOrDefault(c =>
                                   c.DBName.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                                   c.DBName.Equals($"{sourceTable.DBName}Id", StringComparison.OrdinalIgnoreCase) ||
                                   c.DBName.Equals($"{sourceTable.DBName}_Id", StringComparison.OrdinalIgnoreCase));

                if (pkColumn == null) continue;

                // Look for foreign key references in other tables
                foreach (var targetTable in tables.Where(t => t.DBName != sourceTable.DBName))
                {
                    if (targetTable.Columns == null) continue;

                    foreach (var column in targetTable.Columns)
                    {
                        // Check for common FK patterns
                        bool isForeignKey =
                            column.DBName.Equals($"{sourceTable.DBName}_id", StringComparison.OrdinalIgnoreCase) ||
                            column.DBName.Equals($"{sourceTable.DBName}Id", StringComparison.OrdinalIgnoreCase) ||
                            column.DBName.Equals($"{sourceTable.DBName}_ID", StringComparison.OrdinalIgnoreCase) ||
                            column.DBName.Equals($"fk_{sourceTable.DBName}", StringComparison.OrdinalIgnoreCase);

                        if (isForeignKey)
                        {
                            relationships.Add(new SuggestedRelationship
                            {
                                SourceTable = new RelationshipDetails
                                {
                                    TableName = sourceTable.DBName,
                                    ColumnName = pkColumn.DBName
                                },
                                TargetTable = new RelationshipDetails
                                {
                                    TableName = targetTable.DBName,
                                    ColumnName = column.DBName
                                },
                                RelationshipType = "OneToMany",
                                Confidence = 0.9,
                                Reasoning = $"Detected by naming pattern: {column.DBName} references {sourceTable.DBName}"
                            });
                        }
                    }
                }
            }

            return relationships;
        }

        /// <summary>
        /// Analyzes relationships using LLM for complex cases
        /// </summary>
        private async Task<List<SuggestedRelationship>> AnalyzeRelationshipsWithLLMAsync(
            List<TableSchema> tables,
            string databaseName)
        {
            var schemaString = BuildRelationshipSchemaString(tables);
            var prompt = BuildRelationshipAnalysisPrompt(schemaString, databaseName);
            var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

            var result = ParseRelationshipAnalysisResponse(response);

            return result.Success && result.AnalysisData?.SuggestedRelationships != null
                ? result.AnalysisData.SuggestedRelationships
                : new List<SuggestedRelationship>();
        }

        /// <summary>
        /// Builds schema string focused on relationship-relevant columns
        /// </summary>
        private string BuildRelationshipSchemaString(List<TableSchema> tables)
        {
            var sb = new StringBuilder();
            foreach (var table in tables)
            {
                sb.AppendLine($"Table: {table.DBName}");

                var relevantColumns = table.Columns?
                    .Where(c => c.IsPrimaryKey ||
                                c.DBName.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ||
                                c.DBName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (relevantColumns != null && relevantColumns.Any())
                {
                    foreach (var col in relevantColumns)
                    {
                        sb.AppendLine($"  - {col.DBName} ({col.DataType}){(col.IsPrimaryKey ? " PK" : "")}");
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds prompt for relationship analysis
        /// </summary>
        private string BuildRelationshipAnalysisPrompt(string schema, string databaseName)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"You are an expert database analyst identifying relationships in '{databaseName}'.");
            sb.AppendLine("\nAnalyze the tables below and identify missing relationships:");

            sb.AppendLine("\nSchema:");
            sb.AppendLine(schema);

            sb.AppendLine("\nRespond with JSON only:");
            sb.AppendLine("{");
            sb.AppendLine("  \"suggestedRelationships\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"sourceTable\": \"source_table_name\",");
            sb.AppendLine("      \"sourceColumn\": \"source_column_name\",");
            sb.AppendLine("      \"targetTable\": \"target_table_name\",");
            sb.AppendLine("      \"targetColumn\": \"target_column_name\",");
            sb.AppendLine("      \"relationshipType\": \"OneToMany\",");
            sb.AppendLine("      \"confidence\": 0.85,");
            sb.AppendLine("      \"reasoning\": \"Brief explanation\"");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            sb.AppendLine("\nGuidelines:");
            sb.AppendLine("1. Only suggest relationships with confidence >= 0.6");
            sb.AppendLine("2. RelationshipType: OneToOne, OneToMany, ManyToOne, or ManyToMany");
            sb.AppendLine("3. Return pure JSON only");

            return sb.ToString();
        }

        /// <summary>
        /// Parses relationship analysis response from LLM
        /// </summary>
        private SchemaAnalysisResult ParseRelationshipAnalysisResponse(string response)
        {
            try
            {
                var cleanedJson = CleanJsonResponse(response);

                var parsedResponse = JsonSerializer.Deserialize<LlmRelationshipResponse>(
                    cleanedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsedResponse?.SuggestedRelationships != null)
                {
                    // Map flat structure to nested structure
                    var mappedRelationships = parsedResponse.SuggestedRelationships
                        .Select(r => new SuggestedRelationship
                        {
                            SourceTable = new RelationshipDetails
                            {
                                TableName = r.SourceTable,
                                ColumnName = r.SourceColumn
                            },
                            TargetTable = new RelationshipDetails
                            {
                                TableName = r.TargetTable,
                                ColumnName = r.TargetColumn
                            },
                            RelationshipType = r.RelationshipType ?? "ManyToOne",
                            Confidence = r.Confidence > 0 ? r.Confidence : 0.8,
                            Reasoning = r.Reasoning
                        })
                        .ToList();

                    return new SchemaAnalysisResult
                    {
                        Success = true,
                        AnalysisData = new SchemaAnalysisData
                        {
                            SuggestedRelationships = mappedRelationships
                        }
                    };
                }

                return CreateErrorResult("Failed to parse relationship analysis response");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing relationship analysis response");
                return CreateErrorResult($"Parsing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Merges rule-based and LLM relationships, avoiding duplicates
        /// </summary>
        private List<SuggestedRelationship> MergeRelationships(
            List<SuggestedRelationship> ruleBased,
            List<SuggestedRelationship> llmBased)
        {
            var merged = new List<SuggestedRelationship>(ruleBased);
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rel in ruleBased)
            {
                var key = $"{rel.SourceTable?.TableName}_{rel.SourceTable?.ColumnName}_TO_{rel.TargetTable?.TableName}_{rel.TargetTable?.ColumnName}";
                existingKeys.Add(key);
            }

            foreach (var rel in llmBased)
            {
                var key = $"{rel.SourceTable?.TableName}_{rel.SourceTable?.ColumnName}_TO_{rel.TargetTable?.TableName}_{rel.TargetTable?.ColumnName}";
                if (!existingKeys.Contains(key))
                {
                    merged.Add(rel);
                    existingKeys.Add(key);
                }
            }

            return merged;
        }

        private class LlmRelationshipResponse
        {
            public List<LlmSuggestedRelationship> SuggestedRelationships { get; set; }
        }

        #endregion

        #region Apply Results & Status

        /// <summary>
        /// Applies analysis results to update the database schema.
        /// Called by admin after reviewing suggestions.
        /// </summary>
        public async Task<bool> ApplySchemaAnalysisResultsAsync(int databaseId, SchemaAnalysisData analysisData)
        {
            try
            {
                if (analysisData == null)
                {
                    _logger.LogWarning("ApplySchemaAnalysisResultsAsync called with null analysisData");
                    return false;
                }

                var schema = await _schemaService.GetSchemaObject(databaseId);
                if (schema?.Tables == null)
                {
                    _logger.LogWarning("Schema or tables not found for database {DatabaseId}", databaseId);
                    return false;
                }

                int updatedTables = 0;
                int updatedColumns = 0;

                // Apply table descriptions
                if (analysisData.TableDescriptions != null && analysisData.TableDescriptions.Any())
                {
                    foreach (var tableDesc in analysisData.TableDescriptions)
                    {
                        var table = schema.Tables.FirstOrDefault(t =>
                            t.DBName.Equals(tableDesc.TableName, StringComparison.OrdinalIgnoreCase));

                        if (table != null)
                        {
                            table.FriendlyName = tableDesc.SuggestedName;
                            table.Description = tableDesc.SuggestedDescription;
                            updatedTables++;
                        }
                    }
                }

                // Apply column descriptions
                if (analysisData.ColumnDescriptions != null && analysisData.ColumnDescriptions.Any())
                {
                    foreach (var colDesc in analysisData.ColumnDescriptions)
                    {
                        var table = schema.Tables.FirstOrDefault(t =>
                            t.DBName.Equals(colDesc.TableName, StringComparison.OrdinalIgnoreCase));

                        if (table?.Columns == null) continue;

                        var column = table.Columns.FirstOrDefault(c =>
                            c.DBName.Equals(colDesc.ColumnName, StringComparison.OrdinalIgnoreCase));

                        if (column != null)
                        {
                            column.FriendlyName = colDesc.SuggestedName;
                            column.Description = colDesc.SuggestedDescription;
                            column.IsLookup = colDesc.IsLookupColumn;
                            updatedColumns++;
                        }
                    }
                }

                // Update schema timestamp and save
                schema.ModifiedAt = DateTime.UtcNow;
                schema.LastAnalyzed = DateTime.UtcNow;

                await _schemaService.UpdateSchemaAsync(schema);

                _logger.LogInformation(
                    "Applied analysis results: {Tables} tables, {Columns} columns updated for database {DatabaseId}",
                    updatedTables, updatedColumns, databaseId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying analysis results for database {DatabaseId}", databaseId);
                return false;
            }
        }

        /// <summary>
        /// Gets analysis status for UI display
        /// </summary>
        public async Task<AnalysisStatus> GetAnalysisStatusAsync(int databaseId)
        {
            var status = new AnalysisStatus();

            try
            {
                var schema = await _schemaService.GetSchemaObject(databaseId);
                if (schema?.Tables != null)
                {
                    status.TotalTables = schema.Tables.Count(t => t.IsActive);
                    status.TablesAnalyzed = schema.Tables.Count(t =>
                        t.IsActive &&
                        !string.IsNullOrWhiteSpace(t.FriendlyName) &&
                        !string.IsNullOrWhiteSpace(t.Description));

                    status.TotalColumns = schema.Tables
                        .Where(t => t.IsActive)
                        .Sum(t => t.Columns?.Count ?? 0);

                    status.ColumnsAnalyzed = schema.Tables
                        .Where(t => t.IsActive)
                        .Sum(t => t.Columns?.Count(c =>
                            !string.IsNullOrWhiteSpace(c.FriendlyName) &&
                            !string.IsNullOrWhiteSpace(c.Description)) ?? 0);

                    status.RelationshipsFound = schema.Relationships?.Count ?? 0;
                    status.LastAnalyzed = schema.LastAnalyzed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting analysis status for database {DatabaseId}", databaseId);
            }

            return status;
        }

        #endregion

        #region Term Mapping Analysis

        /// <summary>
        /// Suggests term mappings based on database schema
        /// </summary>
        public async Task<List<TermMapping>> SuggestTermMappingsAsync(int databaseId)
        {
            try
            {
                var schemaObj = await _schemaService.GetSchemaObject(databaseId);
                if (schemaObj == null)
                {
                    _logger.LogWarning("Schema not found for database {DatabaseId}", databaseId);
                    return new List<TermMapping>();
                }

                var schemaForLlm = _schemaService.BuildOptimizedSchemaString(schemaObj);
                var prompt = BuildTermSuggestionPrompt(schemaForLlm);

                var response = await _llmService.GenerateTermSuggestionsAsync(prompt);
                var termMappings = ParseTermSuggestionResponse(response, schemaObj);

                return termMappings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error suggesting term mappings for database {DatabaseId}", databaseId);
                return new List<TermMapping>();
            }
        }

        /// <summary>
        /// Generates term mappings dictionary
        /// </summary>
        public async Task<Dictionary<string, string>> GenerateTermMappingsAsync(int databaseId, string schemaString)
        {
            try
            {
                var prompt = BuildTermMappingPrompt(schemaString);
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseTermMappingResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating term mappings for database {DatabaseId}", databaseId);
                throw;
            }
        }

        private string BuildTermSuggestionPrompt(string schema)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an expert database analyst and business intelligence specialist.");
            sb.AppendLine("Based on the database schema below, suggest up to 20 business terms that users might search for.");

            sb.AppendLine("\nFor each term, provide:");
            sb.AppendLine("1. The business term (what users might ask for)");
            sb.AppendLine("2. A clear description");
            sb.AppendLine("3. Type: DirectColumn, CalculatedField, Aggregate, or FilterCondition");
            sb.AppendLine("4. Related table and column names");
            sb.AppendLine("5. Formula (for calculated fields)");
            sb.AppendLine("6. Synonyms users might use");

            sb.AppendLine("\nDatabase schema:");
            sb.AppendLine(schema);

            sb.AppendLine("\nRespond with JSON array:");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            sb.AppendLine("    \"businessTerm\": \"Gross Margin\",");
            sb.AppendLine("    \"description\": \"Revenue minus cost as percentage\",");
            sb.AppendLine("    \"type\": \"CalculatedField\",");
            sb.AppendLine("    \"dependencies\": [");
            sb.AppendLine("      { \"tableName\": \"Sales\", \"columnName\": \"Revenue\" }");
            sb.AppendLine("    ],");
            sb.AppendLine("    \"formula\": \"((Revenue - Cost) / Revenue) * 100\",");
            sb.AppendLine("    \"synonyms\": [\"profit margin\", \"margin\"]");
            sb.AppendLine("  }");
            sb.AppendLine("]");

            return sb.ToString();
        }

        private List<TermMapping> ParseTermSuggestionResponse(string response, DatabaseSchema schema)
        {
            var termMappings = new List<TermMapping>();

            try
            {
                var cleanedJson = CleanJsonResponse(response);

                // Handle array response
                if (!cleanedJson.StartsWith("["))
                {
                    var arrayStart = cleanedJson.IndexOf('[');
                    var arrayEnd = cleanedJson.LastIndexOf(']');
                    if (arrayStart >= 0 && arrayEnd > arrayStart)
                    {
                        cleanedJson = cleanedJson.Substring(arrayStart, arrayEnd - arrayStart + 1);
                    }
                }

                var suggestions = JsonSerializer.Deserialize<List<TermSuggestion>>(
                    cleanedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (suggestions == null) return termMappings;

                foreach (var suggestion in suggestions)
                {
                    var mapping = new TermMapping
                    {
                        ID = Guid.NewGuid().ToString(),
                        BusinessTerm = suggestion.BusinessTerm,
                        Description = suggestion.Description,
                        Type = ParseTermMappingType(suggestion.Type),
                        Synonyms = suggestion.Synonyms ?? new List<string>(),
                        Formula = suggestion.Formula,
                        FilterCondition = suggestion.FilterCondition,
                        IsLLMSuggested = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    // Map dependencies to schema IDs
                    if (suggestion.Dependencies != null)
                    {
                        mapping.Dependencies = new List<TermMappingDependency>();

                        foreach (var dep in suggestion.Dependencies)
                        {
                            var table = schema.Tables?.FirstOrDefault(t =>
                                t.DBName.Equals(dep.TableName, StringComparison.OrdinalIgnoreCase));

                            if (table == null) continue;

                            var column = table.Columns?.FirstOrDefault(c =>
                                c.DBName.Equals(dep.ColumnName, StringComparison.OrdinalIgnoreCase));

                            if (column != null)
                            {
                                mapping.Dependencies.Add(new TermMappingDependency
                                {
                                    TableId = table.ID,
                                    ColumnId = column.ID,
                                    TableName = table.DBName,
                                    ColumnName = column.DBName
                                });

                                // Set direct column mapping
                                if (mapping.Type == TermMappingType.DirectColumn &&
                                    string.IsNullOrEmpty(mapping.TableId))
                                {
                                    mapping.TableId = table.ID;
                                    mapping.ColumnId = column.ID;
                                }
                            }
                        }
                    }

                    termMappings.Add(mapping);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing term suggestion response");
            }

            return termMappings;
        }

        private TermMappingType ParseTermMappingType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return TermMappingType.DirectColumn;

            return type.ToLowerInvariant() switch
            {
                "directcolumn" => TermMappingType.DirectColumn,
                "calculatedfield" => TermMappingType.CalculatedField,
                "aggregate" => TermMappingType.Aggregate,
                "filtercondition" => TermMappingType.FilterCondition,
                _ => TermMappingType.DirectColumn
            };
        }

        private string BuildTermMappingPrompt(string schema)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an expert in translating technical database terms into business-friendly language.");
            sb.AppendLine("\nCreate mappings between technical terms and business terms:");

            sb.AppendLine("\nDatabase schema:");
            sb.AppendLine(schema);

            sb.AppendLine("\nRespond with JSON:");
            sb.AppendLine("{");
            sb.AppendLine("  \"termMappings\": {");
            sb.AppendLine("    \"technical_term\": \"business_term\"");
            sb.AppendLine("  }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private Dictionary<string, string> ParseTermMappingResponse(string response)
        {
            try
            {
                var cleanedJson = CleanJsonResponse(response);

                var parsedResponse = JsonSerializer.Deserialize<TermMappingDictionaryResponse>(
                    cleanedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return parsedResponse?.TermMappings ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing term mapping response");
                return new Dictionary<string, string>();
            }
        }

        private class TermMappingDictionaryResponse
        {
            public Dictionary<string, string> TermMappings { get; set; }
        }

        #endregion

        #region Legacy Methods (Backward Compatibility)

        /// <summary>
        /// Legacy method for analyzing tables (without smart features)
        /// </summary>
        public async Task<SchemaAnalysisResult> AnalyzeTablesAsync(int databaseId, string schemaString)
        {
            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                var prompt = BuildTableAnalysisPrompt(schemaString, database?.Name ?? "Database");
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseTableAnalysisResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in legacy table analysis");
                return CreateErrorResult($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Legacy method for analyzing columns (without smart features)
        /// </summary>
        public async Task<SchemaAnalysisResult> AnalyzeColumnsAsync(int databaseId, string schemaString)
        {
            try
            {
                var prompt = BuildLegacyColumnAnalysisPrompt(schemaString);
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseColumnAnalysisResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in legacy column analysis");
                return CreateErrorResult($"Error: {ex.Message}");
            }
        }

        private string BuildLegacyColumnAnalysisPrompt(string schema)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Analyze the columns in the schema below and provide:");
            sb.AppendLine("1. User-friendly names");
            sb.AppendLine("2. Brief descriptions");
            sb.AppendLine("3. Identify lookup columns");

            sb.AppendLine("\nSchema:");
            sb.AppendLine(schema);

            sb.AppendLine("\nRespond with JSON:");
            sb.AppendLine("{ \"columnDescriptions\": [...] }");

            return sb.ToString();
        }

        /// <summary>
        /// Legacy method for analyzing relationships
        /// </summary>
        public async Task<SchemaAnalysisResult> AnalyzeRelationshipsAsync(int databaseId, string schemaString)
        {
            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                var prompt = BuildRelationshipAnalysisPrompt(schemaString, database?.Name ?? "Database");
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseRelationshipAnalysisResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in legacy relationship analysis");
                return CreateErrorResult($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Legacy method for analyzing conflicts
        /// </summary>
        public async Task<SchemaAnalysisResult> AnalyzeConflictsAsync(int databaseId, string schemaString)
        {
            try
            {
                var prompt = BuildConflictAnalysisPrompt(schemaString);
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseConflictAnalysisResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in conflict analysis");
                return CreateErrorResult($"Error: {ex.Message}");
            }
        }

        private string BuildConflictAnalysisPrompt(string schema)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Identify naming conflicts and ambiguities in the schema:");
            sb.AppendLine(schema);

            sb.AppendLine("\nRespond with JSON:");
            sb.AppendLine("{");
            sb.AppendLine("  \"potentialConflicts\": [...],");
            sb.AppendLine("  \"unclearElements\": [...]");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private SchemaAnalysisResult ParseConflictAnalysisResponse(string response)
        {
            try
            {
                var cleanedJson = CleanJsonResponse(response);

                var parsedResponse = JsonSerializer.Deserialize<ConflictAnalysisResponse>(
                    cleanedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return new SchemaAnalysisResult
                {
                    Success = true,
                    AnalysisData = new SchemaAnalysisData
                    {
                        PotentialConflicts = parsedResponse?.PotentialConflicts ?? new List<PotentialConflict>(),
                        UnclearElements = parsedResponse?.UnclearElements ?? new List<UnclearElement>(),
                        TableDescriptions = new List<TableDescription>(),
                        ColumnDescriptions = new List<ColumnDescription>(),
                        SuggestedRelationships = new List<SuggestedRelationship>()
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing conflict analysis response");
                return CreateErrorResult($"Parsing error: {ex.Message}");
            }
        }

        private class ConflictAnalysisResponse
        {
            public List<PotentialConflict> PotentialConflicts { get; set; }
            public List<UnclearElement> UnclearElements { get; set; }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets or generates database schema
        /// </summary>
        private async Task<DatabaseSchema> GetOrGenerateSchemaAsync(int databaseId, Database database)
        {
            var schemaObj = await _schemaService.GetSchemaObject(databaseId);

            if (schemaObj == null || string.IsNullOrEmpty(schemaObj.SchemaData))
            {
                schemaObj = await _schemaService.GenerateAndGetDatabaseSchemaFromConnectedDBAsync(databaseId, database);
            }

            return schemaObj;
        }

        /// <summary>
        /// Analyzes schema with LLM (full analysis)
        /// </summary>
        private async Task<SchemaAnalysisResult> AnalyzeSchemaWithLLMAsync(string schema, string databaseName)
        {
            try
            {
                var prompt = BuildFullSchemaAnalysisPrompt(schema, databaseName);
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseSchemaAnalysisResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LLM analysis");
                return CreateErrorResult($"Error in LLM analysis: {ex.Message}");
            }
        }

        private string BuildFullSchemaAnalysisPrompt(string schema, string databaseName)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"You are an expert database analyst helping improve the usability of database '{databaseName}'.");
            sb.AppendLine("\nProvide:");
            sb.AppendLine("1. User-friendly names and descriptions for each table and column (max 5 words description)");
            sb.AppendLine("2. Analyze ALL tables and ALL columns");

            sb.AppendLine("\nSchema:");
            sb.AppendLine(schema);

            sb.AppendLine("\nRespond with JSON:");
            sb.AppendLine("{");
            sb.AppendLine("  \"tableDescriptions\": [...],");
            sb.AppendLine("  \"columnDescriptions\": [...]");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private SchemaAnalysisResult ParseSchemaAnalysisResponse(string response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(response))
                {
                    return CreateErrorResult("Empty response from LLM");
                }

                var cleanedJson = CleanJsonResponse(response);

                var llmResponse = JsonSerializer.Deserialize<LlmSchemaAnalysisResponse>(
                    cleanedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (llmResponse == null)
                {
                    return CreateErrorResult("Failed to parse LLM response");
                }

                var analysisData = new SchemaAnalysisData
                {
                    TableDescriptions = llmResponse.TableDescriptions ?? new List<TableDescription>(),
                    ColumnDescriptions = llmResponse.ColumnDescriptions ?? new List<ColumnDescription>(),
                    PotentialConflicts = llmResponse.PotentialConflicts ?? new List<PotentialConflict>(),
                    UnclearElements = llmResponse.UnclearElements ?? new List<UnclearElement>(),
                    SuggestedRelationships = MapLlmRelationships(llmResponse.SuggestedRelationships)
                };

                return new SchemaAnalysisResult
                {
                    Success = true,
                    AnalysisData = analysisData
                };
            }
            catch (JsonException ex)
            {
                return CreateErrorResult($"JSON parsing error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Unexpected error: {ex.Message}");
            }
        }

        /// <summary>
        /// Maps flat LLM relationship structure to nested model structure
        /// </summary>
        private List<SuggestedRelationship> MapLlmRelationships(List<LlmSuggestedRelationship> llmRelationships)
        {
            if (llmRelationships == null || !llmRelationships.Any())
                return new List<SuggestedRelationship>();

            return llmRelationships.Select(lr => new SuggestedRelationship
            {
                RelationshipType = lr.RelationshipType ?? "ManyToOne",
                Confidence = lr.Confidence > 0 ? lr.Confidence : 0.9,
                Reasoning = lr.Reasoning,
                SourceTable = new RelationshipDetails
                {
                    TableName = lr.SourceTable,
                    ColumnName = lr.SourceColumn
                },
                TargetTable = new RelationshipDetails
                {
                    TableName = lr.TargetTable,
                    ColumnName = lr.TargetColumn
                }
            }).ToList();
        }

        /// <summary>
        /// Cleans LLM response by removing markdown and extracting JSON
        /// </summary>
        private string CleanJsonResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "{}";

            var cleaned = response.Trim();

            // Remove markdown code blocks
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(7);
            else if (cleaned.StartsWith("```"))
                cleaned = cleaned.Substring(3);

            if (cleaned.EndsWith("```"))
                cleaned = cleaned.Substring(0, cleaned.Length - 3);

            cleaned = cleaned.Trim();

            // Find JSON object boundaries
            int start = cleaned.IndexOf('{');
            int end = cleaned.LastIndexOf('}');

            if (start >= 0 && end > start)
                return cleaned.Substring(start, end - start + 1);

            // Try array boundaries
            start = cleaned.IndexOf('[');
            end = cleaned.LastIndexOf(']');

            if (start >= 0 && end > start)
                return cleaned.Substring(start, end - start + 1);

            return cleaned;
        }

        /// <summary>
        /// Creates batches of tables for processing
        /// </summary>
        private List<List<TableSchema>> CreateBatches(List<TableSchema> tables, int batchSize)
        {
            var batches = new List<List<TableSchema>>();
            for (int i = 0; i < tables.Count; i += batchSize)
            {
                batches.Add(tables.Skip(i).Take(batchSize).ToList());
            }
            return batches;
        }

        /// <summary>
        /// Builds user-friendly result message
        /// </summary>
        private string BuildResultMessage(int successCount, int failureCount, int totalCount, string itemType)
        {
            if (failureCount == 0)
                return $"Successfully analyzed all {totalCount} {itemType}";

            if (successCount == 0)
                return $"Failed to analyze all {totalCount} {itemType}";

            return $"Analyzed {successCount} of {totalCount} {itemType} ({failureCount} failed - review errors below)";
        }

        /// <summary>
        /// Creates a standard error result
        /// </summary>
        private SchemaAnalysisResult CreateErrorResult(string message)
        {
            return new SchemaAnalysisResult
            {
                Success = false,
                ErrorMessage = message
            };
        }

        #endregion


        #region Supporting Classes

        /// <summary>
        /// Represents an analysis error for a specific table
        /// </summary>
        public class AnalysisError
        {
            public string TableName { get; set; }
            public string ErrorMessage { get; set; }
            public Exception Exception { get; set; }
        }

        #endregion

        #region Chunked Analysis Methods

        /// <summary>
        /// Analyzes tables in chunks for progressive loading.
        /// Returns only the requested chunk of results.
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        /// <param name="skip">Number of tables to skip</param>
        /// <param name="take">Number of tables to analyze in this chunk</param>
        /// <param name="forceReanalyze">If true, re-analyze even if already has data</param>
        public async Task<ChunkedAnalysisResult> AnalyzeTablesChunkedAsync(
            int databaseId,
            int skip,
            int take,
            bool forceReanalyze = false)
        {
            var startTime = DateTime.UtcNow;
            var result = new ChunkedAnalysisResult();

            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                if (database == null)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Database with ID {databaseId} not found";
                    return result;
                }

                var schemaObj = await GetOrGenerateSchemaAsync(databaseId, database);
                if (schemaObj?.Tables == null || !schemaObj.Tables.Any())
                {
                    result.Success = false;
                    result.ErrorMessage = "No tables found in database schema";
                    return result;
                }

                // Get tables that need analysis
                var allTables = schemaObj.Tables
                    .Where(t => t.IsActive)
                    .OrderBy(t => t.DBName)
                    .ToList();

                var tablesToAnalyze = forceReanalyze
                    ? allTables
                    : allTables.Where(t => NeedsTableAnalysis(t)).ToList();

                result.TotalCount = tablesToAnalyze.Count;
                result.SkippedCount = allTables.Count - tablesToAnalyze.Count;

                // If skip is beyond available items, return complete
                if (skip >= tablesToAnalyze.Count)
                {
                    result.Success = true;
                    result.ProcessedCount = tablesToAnalyze.Count;
                    return result;
                }

                // Get the chunk to process
                var chunk = tablesToAnalyze
                    .Skip(skip)
                    .Take(take)
                    .ToList();

                if (!chunk.Any())
                {
                    result.Success = true;
                    result.ProcessedCount = skip;
                    return result;
                }

                // Process this chunk
                var chunkResults = await AnalyzeTableBatchAsync(chunk, database.Name);

                if (chunkResults.Success && chunkResults.AnalysisData?.TableDescriptions != null)
                {
                    result.TableResults = chunkResults.AnalysisData.TableDescriptions;
                }

                if (chunkResults.Errors?.Any() == true)
                {
                    result.Errors.AddRange(chunkResults.Errors);
                }

                result.Success = true;
                result.ProcessedCount = Math.Min(skip + chunk.Count, tablesToAnalyze.Count);
                result.CurrentItemName = chunk.LastOrDefault()?.DBName ?? "";
                result.ChunkDuration = DateTime.UtcNow - startTime;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in chunked table analysis for database {DatabaseId}", databaseId);
                result.Success = false;
                result.ErrorMessage = $"Error: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Analyzes columns in chunks for progressive loading.
        /// </summary>
        public async Task<ChunkedAnalysisResult> AnalyzeColumnsChunkedAsync(
            int databaseId,
            int skip,
            int take,
            bool forceReanalyze = false)
        {
            var startTime = DateTime.UtcNow;
            var result = new ChunkedAnalysisResult();

            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                if (database == null)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Database with ID {databaseId} not found";
                    return result;
                }

                var schemaObj = await GetOrGenerateSchemaAsync(databaseId, database);
                if (schemaObj?.Tables == null || !schemaObj.Tables.Any())
                {
                    result.Success = false;
                    result.ErrorMessage = "No tables found in database schema";
                    return result;
                }

                // Flatten all columns that need analysis
                var allColumns = schemaObj.Tables
                    .Where(t => t.IsActive && t.Columns != null)
                    .SelectMany(t => t.Columns.Select(c => new { Table = t, Column = c }))
                    .OrderBy(x => x.Table.DBName)
                    .ThenBy(x => x.Column.DBName)
                    .ToList();

                var columnsToAnalyze = forceReanalyze
                    ? allColumns
                    : allColumns.Where(x => NeedsColumnAnalysis(x.Column)).ToList();

                result.TotalCount = columnsToAnalyze.Count;
                result.SkippedCount = allColumns.Count - columnsToAnalyze.Count;

                // If skip is beyond available items, return complete
                if (skip >= columnsToAnalyze.Count)
                {
                    result.Success = true;
                    result.ProcessedCount = columnsToAnalyze.Count;
                    return result;
                }

                // Get the chunk to process
                var chunk = columnsToAnalyze
                    .Skip(skip)
                    .Take(take)
                    .ToList();

                if (!chunk.Any())
                {
                    result.Success = true;
                    result.ProcessedCount = skip;
                    return result;
                }

                // Group by table for efficient processing
                var columnsByTable = chunk
                    .GroupBy(x => x.Table)
                    .ToList();

                var allColumnDescriptions = new List<ColumnDescription>();

                foreach (var tableGroup in columnsByTable)
                {
                    var table = tableGroup.Key;
                    var columns = tableGroup.Select(x => x.Column).ToList();

                    var tableResult = await AnalyzeTableColumnsAsync(table, database.Name, columns);

                    if (tableResult.Success && tableResult.AnalysisData?.ColumnDescriptions != null)
                    {
                        allColumnDescriptions.AddRange(tableResult.AnalysisData.ColumnDescriptions);
                    }
                    else if (!string.IsNullOrEmpty(tableResult.ErrorMessage))
                    {
                        result.Errors.Add($"{table.DBName}: {tableResult.ErrorMessage}");
                    }

                    // Small delay between tables to avoid rate limiting
                    if (columnsByTable.IndexOf(tableGroup) < columnsByTable.Count - 1)
                    {
                        await Task.Delay(DelayBetweenLLMCallsMs);
                    }
                }

                result.ColumnResults = allColumnDescriptions;
                result.Success = true;
                result.ProcessedCount = Math.Min(skip + chunk.Count, columnsToAnalyze.Count);
                result.CurrentItemName = chunk.LastOrDefault()?.Column.DBName ?? "";
                result.ChunkDuration = DateTime.UtcNow - startTime;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in chunked column analysis for database {DatabaseId}", databaseId);
                result.Success = false;
                result.ErrorMessage = $"Error: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Analyzes specific columns for a table (helper for chunked processing)
        /// </summary>
        private async Task<SchemaAnalysisResult> AnalyzeTableColumnsAsync(
            TableSchema table,
            string databaseName,
            List<ColumnSchema> columnsToAnalyze)
        {
            try
            {
                if (!columnsToAnalyze.Any())
                {
                    return new SchemaAnalysisResult
                    {
                        Success = true,
                        AnalysisData = new SchemaAnalysisData { ColumnDescriptions = new List<ColumnDescription>() }
                    };
                }

                var prompt = BuildColumnAnalysisPrompt(table, columnsToAnalyze, databaseName);
                var response = await _llmService.GenerateSchemaAnalysisAsync(prompt);

                return ParseColumnAnalysisResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze columns for table {TableName}", table.DBName);
                return CreateErrorResult($"Column analysis failed for {table.DBName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the total count of items needing analysis (for initial progress setup)
        /// </summary>
        public async Task<AnalysisCountResult> GetAnalysisCountsAsync(int databaseId, bool forceReanalyze = false)
        {
            var result = new AnalysisCountResult();

            try
            {
                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
                if (database == null)
                    return result;

                var schemaObj = await GetOrGenerateSchemaAsync(databaseId, database);
                if (schemaObj?.Tables == null)
                    return result;

                var activeTables = schemaObj.Tables.Where(t => t.IsActive).ToList();

                result.TotalTables = activeTables.Count;
                result.TablesToAnalyze = forceReanalyze
                    ? activeTables.Count
                    : activeTables.Count(t => NeedsTableAnalysis(t));

                var allColumns = activeTables
                    .Where(t => t.Columns != null)
                    .SelectMany(t => t.Columns)
                    .ToList();

                result.TotalColumns = allColumns.Count;
                result.ColumnsToAnalyze = forceReanalyze
                    ? allColumns.Count
                    : allColumns.Count(c => NeedsColumnAnalysis(c));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analysis counts for database {DatabaseId}", databaseId);
            }

            return result;
        }

        #endregion





        #endregion
    }
}