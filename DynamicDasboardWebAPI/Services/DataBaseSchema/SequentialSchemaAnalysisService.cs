//using DynamicDashboardCommon.Models;
//using DynamicDashboardCommon.Models.SchemaAnalysis;
//using DynamicDasboardWebAPI.Services.LLM;
//using Microsoft.Extensions.Configuration;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Text.Json;
//using System.Threading.Tasks;

//namespace DynamicDasboardWebAPI.Services.SchemaAnalysis
//{
//    /// <summary>
//    /// Sequential schema analysis service - orchestrates multi-phase analysis
//    /// </summary>
//    public class SequentialSchemaAnalysisService
//    {
//        private readonly DatabaseSchemaService _schemaService;
//        private readonly DatabaseService _databaseService;
//        private readonly LLMServiceFactory _llmFactory;
//        private readonly IConfiguration _config;
//        private readonly SampleDataService _sampleDataService;

//        public SequentialSchemaAnalysisService(
//            DatabaseSchemaService schemaService,
//            DatabaseService databaseService,
//            LLMServiceFactory llmFactory,
//            SampleDataService sampleDataService,
//            IConfiguration config)
//        {
//            _schemaService = schemaService;
//            _databaseService = databaseService;
//            _llmFactory = llmFactory;
//            _sampleDataService = sampleDataService;
//            _config = config;
//        }

//        /// <summary>
//        /// Start sequential analysis for a database
//        /// </summary>
//        public async Task<SequentialAnalysisState> StartAnalysisAsync(int databaseId)
//        {
//            try
//            {
//                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
//                if (database == null)
//                {
//                    throw new Exception($"Database with ID {databaseId} not found");
//                }

//                // Initialize state
//                var state = new SequentialAnalysisState
//                {
//                    DatabaseId = databaseId,
//                    Status = AnalysisStatus.InProgress,
//                    CurrentPhase = AnalysisPhase.DomainGrouping,
//                    StartedAt = DateTime.UtcNow,
//                    LastUpdatedAt = DateTime.UtcNow
//                };

//                // Save initial state
//                await SaveAnalysisStateAsync(state);

//                // Execute Phase 1
//                await ExecutePhase1Async(state);

//                // If auto-proceed is enabled, continue to Phase 2
//                var autoProceed = _config.GetValue<bool>("SchemaAnalysis:Sequential:AutoProceed", true);
//                if (autoProceed && state.Phase1Result.Status == PhaseStatus.Complete)
//                {
//                    await ExecutePhase2Async(state);
                    
//                    if (state.Phase2Result.Status == PhaseStatus.Complete)
//                    {
//                        await ExecutePhase3Async(state);
                        
//                        if (state.Phase3Result.Status == PhaseStatus.Complete)
//                        {
//                            await ExecutePhase4Async(state);
//                        }
//                    }
//                }

//                // Mark as completed if all phases done
//                if (state.Phase4Result?.Status == PhaseStatus.Complete)
//                {
//                    state.Status = AnalysisStatus.Completed;
//                    state.CompletedAt = DateTime.UtcNow;
//                }

//                await SaveAnalysisStateAsync(state);
//                return state;
//            }
//            catch (Exception ex)
//            {
//                throw new Exception($"Error starting sequential analysis: {ex.Message}", ex);
//            }
//        }

//        /// <summary>
//        /// Get current analysis state
//        /// </summary>
//        public async Task<SequentialAnalysisState> GetAnalysisStateAsync(int databaseId)
//        {
//            try
//            {
//                var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
//                if (database == null)
//                {
//                    return null;
//                }

//                var state = new SequentialAnalysisState
//                {
//                    DatabaseId = databaseId,
//                    Status = ParseAnalysisStatus(database.AnalysisStatus),
//                    CurrentPhase = (AnalysisPhase)(database.AnalysisCurrentPhase ?? 0),
//                    StartedAt = database.AnalysisStartedAt,
//                    LastUpdatedAt = database.AnalysisLastUpdated,
//                    CompletedAt = database.AnalysisCompletedAt
//                };

//                // Load phase results
//                state.Phase1Result = LoadPhaseResult(database.AnalysisPhase1Data, 
//                    database.AnalysisPhase1Status, database.AnalysisPhase1Issues, 
//                    database.AnalysisPhase1RawResponse, AnalysisPhase.DomainGrouping);
                    
//                state.Phase2Result = LoadPhaseResult(database.AnalysisPhase2Data,
//                    database.AnalysisPhase2Status, database.AnalysisPhase2Issues,
//                    database.AnalysisPhase2RawResponse, AnalysisPhase.TableAnalysis);
                    
//                state.Phase3Result = LoadPhaseResult(database.AnalysisPhase3Data,
//                    database.AnalysisPhase3Status, database.AnalysisPhase3Issues,
//                    database.AnalysisPhase3RawResponse, AnalysisPhase.ColumnAnalysis);
                    
//                state.Phase4Result = LoadPhaseResult(database.AnalysisPhase4Data,
//                    database.AnalysisPhase4Status, database.AnalysisPhase4Issues,
//                    database.AnalysisPhase4RawResponse, AnalysisPhase.RelationshipAnalysis);

//                return state;
//            }
//            catch (Exception ex)
//            {
//                throw new Exception($"Error loading analysis state: {ex.Message}", ex);
//            }
//        }

//        /// <summary>
//        /// Continue analysis to next phase
//        /// </summary>
//        public async Task<SequentialAnalysisState> ContinueAnalysisAsync(int databaseId)
//        {
//            try
//            {
//                var state = await GetAnalysisStateAsync(databaseId);
//                if (state == null)
//                {
//                    throw new Exception("No analysis state found");
//                }

//                // Execute next phase based on current phase
//                switch (state.CurrentPhase)
//                {
//                    case AnalysisPhase.DomainGrouping:
//                        await ExecutePhase2Async(state);
//                        break;
//                    case AnalysisPhase.TableAnalysis:
//                        await ExecutePhase3Async(state);
//                        break;
//                    case AnalysisPhase.ColumnAnalysis:
//                        await ExecutePhase4Async(state);
//                        break;
//                    default:
//                        throw new Exception("Analysis already completed or invalid state");
//                }

//                await SaveAnalysisStateAsync(state);
//                return state;
//            }
//            catch (Exception ex)
//            {
//                throw new Exception($"Error continuing analysis: {ex.Message}", ex);
//            }
//        }

//        #region Phase Execution Methods

//        /// <summary>
//        /// PHASE 1: Domain Grouping
//        /// </summary>
//        private async Task ExecutePhase1Async(SequentialAnalysisState state)
//        {
//            var startTime = DateTime.UtcNow;
//            var result = new PhaseResult
//            {
//                Phase = AnalysisPhase.DomainGrouping,
//                Status = PhaseStatus.InProgress
//            };

//            try
//            {
//                var schema = await _schemaService.GetSchemaObject(state.DatabaseId);
//                var database = await _databaseService.GetDatabaseByIdAsync(state.DatabaseId);

//                // Build prompt
//                var prompt = BuildPhase1Prompt(database, schema);

//                // Call LLM
//                var llm = CreateLlmServiceForAnalysis();
//                var response = await llm.GenerateSchemaAnalysisAsync(prompt);

//                result.RawResponse = response;

//                // Parse response
//                var parsed = ParseDomainGroupingResponse(response);
//                result.DataJson = JsonSerializer.Serialize(parsed);
//                result.Status = PhaseStatus.Complete;

//                state.CurrentPhase = AnalysisPhase.TableAnalysis;
//            }
//            catch (Exception ex)
//            {
//                result.Status = PhaseStatus.Failed;
//                result.ErrorMessage = ex.Message;
//                result.Issues.Add($"Phase 1 failed: {ex.Message}");
//            }
//            finally
//            {
//                result.CompletedAt = DateTime.UtcNow;
//                result.Duration = DateTime.UtcNow - startTime;
//                state.Phase1Result = result;
//                state.LastUpdatedAt = DateTime.UtcNow;
//            }
//        }

//        /// <summary>
//        /// PHASE 2: Table Analysis (Self-Contained)
//        /// </summary>
//        private async Task ExecutePhase2Async(SequentialAnalysisState state)
//        {
//            var startTime = DateTime.UtcNow;
//            var result = new PhaseResult
//            {
//                Phase = AnalysisPhase.TableAnalysis,
//                Status = PhaseStatus.InProgress
//            };

//            try
//            {
//                var schema = await _schemaService.GetSchemaObject(state.DatabaseId);
                
//                // Parse Phase 1 results
//                var phase1Data = JsonSerializer.Deserialize<DomainGroupingResult>(state.Phase1Result.DataJson);

//                // Analyze each domain
//                var allTableAnalyses = new List<TableAnalysisInfo>();

//                foreach (var domain in phase1Data.Domains)
//                {
//                    // Build self-contained prompt with Phase 1 info embedded
//                    var prompt = BuildPhase2Prompt(domain, phase1Data.SharedTables, schema);

//                    // Call LLM
//                    var llm = CreateLlmServiceForAnalysis();
//                    var response = await llm.GenerateSchemaAnalysisAsync(prompt);

//                    // Parse domain-specific results
//                    var parsed = ParseTableAnalysisResponse(response);
//                    allTableAnalyses.AddRange(parsed.Tables);
//                }

//                var finalResult = new TableAnalysisResult { Tables = allTableAnalyses };
//                result.DataJson = JsonSerializer.Serialize(finalResult);
//                result.RawResponse = "Multiple domain calls - see individual tables";
//                result.Status = PhaseStatus.Complete;

//                state.CurrentPhase = AnalysisPhase.ColumnAnalysis;
//            }
//            catch (Exception ex)
//            {
//                result.Status = PhaseStatus.Failed;
//                result.ErrorMessage = ex.Message;
//                result.Issues.Add($"Phase 2 failed: {ex.Message}");
//            }
//            finally
//            {
//                result.CompletedAt = DateTime.UtcNow;
//                result.Duration = DateTime.UtcNow - startTime;
//                state.Phase2Result = result;
//                state.LastUpdatedAt = DateTime.UtcNow;
//            }
//        }

//        /// <summary>
//        /// PHASE 3: Column Analysis (Self-Contained with Sample Data)
//        /// </summary>
//        private async Task ExecutePhase3Async(SequentialAnalysisState state)
//        {
//            var startTime = DateTime.UtcNow;
//            var result = new PhaseResult
//            {
//                Phase = AnalysisPhase.ColumnAnalysis,
//                Status = PhaseStatus.InProgress
//            };

//            try
//            {
//                var schema = await _schemaService.GetSchemaObject(state.DatabaseId);
                
//                // Parse Phase 1 and Phase 2 results
//                var phase1Data = JsonSerializer.Deserialize<DomainGroupingResult>(state.Phase1Result.DataJson);
//                var phase2Data = JsonSerializer.Deserialize<TableAnalysisResult>(state.Phase2Result.DataJson);

//                // Get sample data for requested columns
//                var sampleDataRequests = phase2Data.Tables
//                    .SelectMany(t => t.ColumnsNeedingSampleData.Select(c => new
//                    {
//                        TableName = t.TableName,
//                        ColumnName = c.ColumnName,
//                        Reason = c.Reason
//                    }))
//                    .ToList();

//                var sampleDataResults = await _sampleDataService.FetchSampleDataAsync(
//                    state.DatabaseId, sampleDataRequests);

//                // Analyze columns for each domain
//                var allColumnAnalyses = new List<ColumnAnalysisInfo>();

//                foreach (var domain in phase1Data.Domains)
//                {
//                    // Build self-contained prompt with all previous phase info
//                    var prompt = BuildPhase3Prompt(domain, phase2Data, sampleDataResults, schema);

//                    // Call LLM
//                    var llm = CreateLlmServiceForAnalysis();
//                    var response = await llm.GenerateSchemaAnalysisAsync(prompt);

//                    // Parse results
//                    var parsed = ParseColumnAnalysisResponse(response);
//                    allColumnAnalyses.AddRange(parsed.Columns);
//                }

//                var finalResult = new ColumnAnalysisResult { Columns = allColumnAnalyses };
//                result.DataJson = JsonSerializer.Serialize(finalResult);
//                result.Status = PhaseStatus.Complete;

//                state.CurrentPhase = AnalysisPhase.RelationshipAnalysis;
//            }
//            catch (Exception ex)
//            {
//                result.Status = PhaseStatus.Failed;
//                result.ErrorMessage = ex.Message;
//                result.Issues.Add($"Phase 3 failed: {ex.Message}");
//            }
//            finally
//            {
//                result.CompletedAt = DateTime.UtcNow;
//                result.Duration = DateTime.UtcNow - startTime;
//                state.Phase3Result = result;
//                state.LastUpdatedAt = DateTime.UtcNow;
//            }
//        }

//        /// <summary>
//        /// PHASE 4: Relationship Analysis (Self-Contained)
//        /// </summary>
//        private async Task ExecutePhase4Async(SequentialAnalysisState state)
//        {
//            var startTime = DateTime.UtcNow;
//            var result = new PhaseResult
//            {
//                Phase = AnalysisPhase.RelationshipAnalysis,
//                Status = PhaseStatus.InProgress
//            };

//            try
//            {
//                var schema = await _schemaService.GetSchemaObject(state.DatabaseId);
                
//                // Parse all previous phase results
//                var phase1Data = JsonSerializer.Deserialize<DomainGroupingResult>(state.Phase1Result.DataJson);
//                var phase2Data = JsonSerializer.Deserialize<TableAnalysisResult>(state.Phase2Result.DataJson);
//                var phase3Data = JsonSerializer.Deserialize<ColumnAnalysisResult>(state.Phase3Result.DataJson);

//                // Analyze relationships for each domain
//                var allRelationships = new List<SuggestedRelationshipInfo>();
//                var allCrossDomainRelationships = new List<CrossDomainRelationshipInfo>();

//                foreach (var domain in phase1Data.Domains)
//                {
//                    // Build self-contained prompt with full context
//                    var prompt = BuildPhase4Prompt(domain, phase1Data, phase2Data, phase3Data, schema);

//                    // Call LLM
//                    var llm = CreateLlmServiceForAnalysis();
//                    var response = await llm.GenerateSchemaAnalysisAsync(prompt);

//                    // Parse results
//                    var parsed = ParseRelationshipAnalysisResponse(response);
//                    allRelationships.AddRange(parsed.MissingRelationships);
//                    allCrossDomainRelationships.AddRange(parsed.CrossDomainRelationships);
//                }

//                var finalResult = new RelationshipAnalysisResult
//                {
//                    MissingRelationships = allRelationships,
//                    CrossDomainRelationships = allCrossDomainRelationships
//                };
                
//                result.DataJson = JsonSerializer.Serialize(finalResult);
//                result.Status = PhaseStatus.Complete;
//            }
//            catch (Exception ex)
//            {
//                result.Status = PhaseStatus.Failed;
//                result.ErrorMessage = ex.Message;
//                result.Issues.Add($"Phase 4 failed: {ex.Message}");
//            }
//            finally
//            {
//                result.CompletedAt = DateTime.UtcNow;
//                result.Duration = DateTime.UtcNow - startTime;
//                state.Phase4Result = result;
//                state.LastUpdatedAt = DateTime.UtcNow;
//            }
//        }

//        #endregion

//       // CONTINUATION OF SequentialSchemaAnalysisService
//// Add these methods to the class from Part 1

//#region Prompt Builders

//private string BuildPhase1Prompt(Database database, DatabaseSchema schema)
//{
//    var maxTablesPerDomain = _config.GetValue<int>("SchemaAnalysis:DomainGrouping:MaxTablesPerDomain", 20);
    
//    var prompt = new StringBuilder();
//    prompt.AppendLine("You are a database architecture expert.");
//    prompt.AppendLine();
//    prompt.AppendLine($"DATABASE: {database.Name}");
//    prompt.AppendLine($"TOTAL TABLES: {schema.Tables.Count}");
//    prompt.AppendLine();
//    prompt.AppendLine("TABLE STRUCTURES:");
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    
//    foreach (var table in schema.Tables)
//    {
//        prompt.AppendLine();
//        prompt.AppendLine($"📋 {table.DBName} ({table.Columns.Count} columns)");
        
//        var pk = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
//        if (pk != null)
//        {
//            prompt.AppendLine($"   Primary Key: {pk.DBName}");
//        }
        
//        var fks = table.Columns.Where(c => c.IsLookup).ToList();
//        if (fks.Any())
//        {
//            prompt.AppendLine("   Foreign Keys:");
//            foreach (var fk in fks)
//            {
//                prompt.AppendLine($"   - {fk.DBName}");
//            }
//        }
        
//        var referencedBy = schema.Relationships
//            .Where(r => r.Target?.TableName == table.DBName)
//            .Select(r => r.Source?.TableName)
//            .Distinct()
//            .ToList();
            
//        if (referencedBy.Any())
//        {
//            prompt.AppendLine($"   Referenced By: {string.Join(", ", referencedBy)}");
//        }
        
//        prompt.AppendLine($"   Columns: {string.Join(", ", table.Columns.Select(c => $"{c.DBName} ({c.DataType})"))}");
//    }
    
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine();
//    prompt.AppendLine("CONFIGURATION:");
//    prompt.AppendLine($"- Maximum tables per domain: {maxTablesPerDomain}");
//    prompt.AppendLine();
//    prompt.AppendLine("YOUR TASK:");
//    prompt.AppendLine("Group these tables into main business domains based on their relationships and purpose.");
//    prompt.AppendLine();
//    prompt.AppendLine("SHARED TABLE DETECTION:");
//    prompt.AppendLine("A table is shared if:");
//    prompt.AppendLine("1. Referenced by tables in multiple domains (has multiple incoming FKs)");
//    prompt.AppendLine("2. Has generic name (Statuses, Types, Categories) OR referenced widely");
//    prompt.AppendLine();
//    prompt.AppendLine("OUTPUT (pure JSON, no markdown):");
//    prompt.AppendLine("{");
//    prompt.AppendLine("  \"domains\": [");
//    prompt.AppendLine("    {");
//    prompt.AppendLine("      \"name\": \"Sales\",");
//    prompt.AppendLine("      \"description\": \"Sales order processing\",");
//    prompt.AppendLine("      \"priority\": 1,");
//    prompt.AppendLine("      \"tableNames\": [\"Orders\", \"Customers\"],");
//    prompt.AppendLine("      \"sharedTablesReferenced\": [\"Statuses\"],");
//    prompt.AppendLine("      \"estimatedComplexity\": \"Medium\"");
//    prompt.AppendLine("    }");
//    prompt.AppendLine("  ],");
//    prompt.AppendLine("  \"sharedTables\": [");
//    prompt.AppendLine("    {");
//    prompt.AppendLine("      \"tableName\": \"Statuses\",");
//    prompt.AppendLine("      \"referencedByDomains\": [\"Sales\", \"Support\"],");
//    prompt.AppendLine("      \"referencedByTables\": [\"Orders\", \"Tickets\"],");
//    prompt.AppendLine("      \"reason\": \"Generic status lookup\",");
//    prompt.AppendLine("      \"treatAsPrimaryDomain\": \"Reference Data\"");
//    prompt.AppendLine("    }");
//    prompt.AppendLine("  ]");
//    prompt.AppendLine("}");
    
//    return prompt.ToString();
//}

//private string BuildPhase2Prompt(DomainInfo domain, List<SharedTableInfo> sharedTables, DatabaseSchema schema)
//{
//    var maxDescLength = _config.GetValue<int>("SchemaAnalysis:Descriptions:MaxTableDescriptionLength", 150);
    
//    var prompt = new StringBuilder();
//    prompt.AppendLine($"ANALYZING DOMAIN: {domain.Name}");
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine("DOMAIN INFORMATION:");
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine($"Name: {domain.Name}");
//    prompt.AppendLine($"Description: {domain.Description}");
//    prompt.AppendLine($"Tables: {string.Join(", ", domain.TableNames)}");
    
//    if (domain.SharedTablesReferenced?.Any() == true)
//    {
//        prompt.AppendLine($"Shared Tables Used: {string.Join(", ", domain.SharedTablesReferenced)}");
//    }
    
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine("TABLE STRUCTURES:");
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    
//    // Get main tables in domain
//    var mainTables = schema.Tables.Where(t => domain.TableNames.Contains(t.DBName)).ToList();
    
//    // Get shared tables referenced by this domain
//    var sharedTableObjs = schema.Tables.Where(t => domain.SharedTablesReferenced?.Contains(t.DBName) == true).ToList();
    
//    var allTables = mainTables.Concat(sharedTableObjs).ToList();
    
//    foreach (var table in allTables)
//    {
//        var isShared = domain.SharedTablesReferenced?.Contains(table.DBName) == true;
        
//        prompt.AppendLine();
//        prompt.AppendLine($"📋 {table.DBName}{(isShared ? " (SHARED)" : "")}");
        
//        var pk = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
//        if (pk != null)
//        {
//            prompt.AppendLine($"   Primary Key: {pk.DBName}");
//        }
        
//        var fks = table.Columns.Where(c => c.IsLookup).ToList();
//        if (fks.Any())
//        {
//            prompt.AppendLine("   Foreign Keys:");
//            foreach (var fk in fks)
//            {
//                prompt.AppendLine($"   - {fk.DBName}");
//            }
//        }
        
//        if (isShared)
//        {
//            var sharedInfo = sharedTables.FirstOrDefault(st => st.TableName == table.DBName);
//            if (sharedInfo != null)
//            {
//                prompt.AppendLine($"   Used By Domains: {string.Join(", ", sharedInfo.ReferencedByDomains)}");
//            }
//        }
        
//        prompt.AppendLine("   Columns:");
//        foreach (var col in table.Columns)
//        {
//            var keyInfo = col.IsPrimaryKey ? ", PK" : (col.IsLookup ? ", FK" : "");
//            prompt.AppendLine($"   - {col.DBName} ({col.DataType}{keyInfo})");
//        }
//    }
    
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine();
//    prompt.AppendLine($"CONFIGURATION: Max description length = {maxDescLength} characters");
//    prompt.AppendLine();
//    prompt.AppendLine("YOUR TASK:");
//    prompt.AppendLine("Analyze EACH table and provide:");
//    prompt.AppendLine("1. Friendly name");
//    prompt.AppendLine($"2. Description (MAX {maxDescLength} chars)");
//    prompt.AppendLine("3. Category (FACT or DIMENSION)");
//    prompt.AppendLine("4. Business importance (1-10)");
//    prompt.AppendLine("5. Columns needing sample data (status/type/category/code columns, ambiguous names)");
//    prompt.AppendLine();
//    prompt.AppendLine("For SHARED tables, explain domain-specific usage.");
//    prompt.AppendLine();
//    prompt.AppendLine("OUTPUT (pure JSON, no markdown):");
//    prompt.AppendLine("{");
//    prompt.AppendLine("  \"tables\": [");
//    prompt.AppendLine("    {");
//    prompt.AppendLine("      \"tableName\": \"Orders\",");
//    prompt.AppendLine("      \"friendlyName\": \"Customer Orders\",");
//    prompt.AppendLine("      \"description\": \"Records customer orders\",");
//    prompt.AppendLine("      \"category\": \"FACT\",");
//    prompt.AppendLine("      \"businessImportance\": 10,");
//    prompt.AppendLine("      \"needsReview\": false,");
//    prompt.AppendLine("      \"isSharedTable\": false,");
//    prompt.AppendLine("      \"columnsNeedingSampleData\": [");
//    prompt.AppendLine("        {\"columnName\": \"StatusID\", \"reason\": \"Need status values\"}");
//    prompt.AppendLine("      ]");
//    prompt.AppendLine("    }");
//    prompt.AppendLine("  ]");
//    prompt.AppendLine("}");
    
//    return prompt.ToString();
//}

//private string BuildPhase3Prompt(DomainInfo domain, TableAnalysisResult phase2Data, 
//    List<SampleDataResult> sampleData, DatabaseSchema schema)
//{
//    var maxDescLength = _config.GetValue<int>("SchemaAnalysis:Descriptions:MaxColumnDescriptionLength", 100);
    
//    var prompt = new StringBuilder();
//    prompt.AppendLine($"ANALYZING COLUMNS FOR DOMAIN: {domain.Name}");
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine("TABLE ANALYSIS RESULTS:");
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    
//    var domainTables = phase2Data.Tables.Where(t => domain.TableNames.Contains(t.TableName)).ToList();
    
//    foreach (var table in domainTables)
//    {
//        prompt.AppendLine($"{table.TableName}: {table.FriendlyName} ({table.Category})");
//        prompt.AppendLine($"  {table.Description}");
//    }
    
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine("COLUMN DETAILS WITH SAMPLE DATA:");
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    
//    foreach (var tableName in domain.TableNames)
//    {
//        var table = schema.Tables.FirstOrDefault(t => t.DBName == tableName);
//        if (table == null) continue;
        
//        prompt.AppendLine();
//        prompt.AppendLine($"📋 {table.DBName}");
        
//        foreach (var col in table.Columns)
//        {
//            var nullable = col.IsNullable ? "NULLABLE" : "NOT NULL";
//            var keyInfo = col.IsPrimaryKey ? ", PK" : (col.IsLookup ? ", FK" : "");
//            prompt.AppendLine($"   {col.DBName} ({col.DataType}, {nullable}{keyInfo})");
            
//            // Add sample data if available
//            var sample = sampleData.FirstOrDefault(s => s.TableName == table.DBName && s.ColumnName == col.DBName);
//            if (sample != null)
//            {
//                if (sample.DataType == "ValueSet")
//                {
//                    prompt.AppendLine($"      VALUES: {string.Join(", ", sample.DistinctValues.Take(10))}");
//                    if (sample.ResolvedValues?.Any() == true)
//                    {
//                        prompt.AppendLine($"      DISPLAY: {string.Join(", ", sample.ResolvedValues.Values.Take(10))}");
//                    }
//                }
//                else
//                {
//                    prompt.AppendLine($"      SAMPLES: {string.Join(", ", sample.SampleValues.Take(3))}");
//                }
//            }
//        }
//    }
    
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine();
//    prompt.AppendLine($"CONFIGURATION: Max description = {maxDescLength} chars");
//    prompt.AppendLine();
//    prompt.AppendLine("YOUR TASK:");
//    prompt.AppendLine("For EACH column provide:");
//    prompt.AppendLine("1. Friendly name");
//    prompt.AppendLine($"2. Description (MAX {maxDescLength} chars)");
//    prompt.AppendLine("3. Data category (ONLY if certain)");
//    prompt.AppendLine("   Options: Identifier, Measure, Attribute, Date, Flag, Description, Code");
//    prompt.AppendLine("4. Business importance (1-10)");
//    prompt.AppendLine();
//    prompt.AppendLine("DATA CATEGORY RULES:");
//    prompt.AppendLine("- Set ONLY if 100% certain");
//    prompt.AppendLine("- Use sample data to decide");
//    prompt.AppendLine("- OMIT if uncertain");
//    prompt.AppendLine();
//    prompt.AppendLine("OUTPUT (pure JSON, no markdown):");
//    prompt.AppendLine("{");
//    prompt.AppendLine("  \"columns\": [");
//    prompt.AppendLine("    {");
//    prompt.AppendLine("      \"tableName\": \"Orders\",");
//    prompt.AppendLine("      \"columnName\": \"OrderID\",");
//    prompt.AppendLine("      \"friendlyName\": \"Order Number\",");
//    prompt.AppendLine("      \"description\": \"Unique order identifier\",");
//    prompt.AppendLine("      \"dataCategory\": \"Identifier\",");
//    prompt.AppendLine("      \"businessImportance\": 10,");
//    prompt.AppendLine("      \"isLookupColumn\": false,");
//    prompt.AppendLine("      \"isValueSet\": false,");
//    prompt.AppendLine("      \"needsReview\": false");
//    prompt.AppendLine("    }");
//    prompt.AppendLine("  ]");
//    prompt.AppendLine("}");
    
//    return prompt.ToString();
//}

//private string BuildPhase4Prompt(DomainInfo domain, DomainGroupingResult phase1Data, 
//    TableAnalysisResult phase2Data, ColumnAnalysisResult phase3Data, DatabaseSchema schema)
//{
//    var maxReasonLength = _config.GetValue<int>("SchemaAnalysis:Descriptions:MaxRelationshipReasonLength", 100);
    
//    var prompt = new StringBuilder();
//    prompt.AppendLine($"ANALYZING RELATIONSHIPS FOR DOMAIN: {domain.Name}");
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine("DOMAIN TABLES:");
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    
//    foreach (var tableName in domain.TableNames)
//    {
//        var tableInfo = phase2Data.Tables.FirstOrDefault(t => t.TableName == tableName);
//        if (tableInfo == null) continue;
        
//        prompt.AppendLine($"{tableName} ({tableInfo.Category}): {tableInfo.Description}");
        
//        var keyColumns = phase3Data.Columns
//            .Where(c => c.TableName == tableName && (c.DataCategory == "Identifier" || c.IsLookupColumn))
//            .ToList();
            
//        if (keyColumns.Any())
//        {
//            prompt.AppendLine($"  Key Columns: {string.Join(", ", keyColumns.Select(c => c.ColumnName))}");
//        }
//    }
    
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine("EXISTING RELATIONSHIPS:");
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    
//    foreach (var rel in schema.Relationships)
//    {
//        prompt.AppendLine($"✓ {rel.Source.TableName}.{rel.Source.ColumnName} → {rel.Target.TableName}.{rel.Target.ColumnName}");
//    }
    
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine("OTHER DOMAINS (for cross-domain suggestions):");
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    
//    foreach (var otherDomain in phase1Data.Domains.Where(d => d.Name != domain.Name))
//    {
//        prompt.AppendLine($"{otherDomain.Name}: {string.Join(", ", otherDomain.TableNames)}");
//    }
    
//    prompt.AppendLine();
//    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
//    prompt.AppendLine();
//    prompt.AppendLine($"CONFIGURATION: Max reason length = {maxReasonLength} chars");
//    prompt.AppendLine();
//    prompt.AppendLine("YOUR TASK:");
//    prompt.AppendLine("Identify MISSING relationships:");
//    prompt.AppendLine("1. Within this domain");
//    prompt.AppendLine("2. To shared tables");
//    prompt.AppendLine("3. Cross-domain connections");
//    prompt.AppendLine();
//    prompt.AppendLine("OUTPUT (pure JSON, no markdown):");
//    prompt.AppendLine("{");
//    prompt.AppendLine("  \"missingRelationships\": [");
//    prompt.AppendLine("    {");
//    prompt.AppendLine("      \"sourceTable\": \"Orders\",");
//    prompt.AppendLine("      \"sourceColumn\": \"CustomerID\",");
//    prompt.AppendLine("      \"targetTable\": \"Customers\",");
//    prompt.AppendLine("      \"targetColumn\": \"CustomerID\",");
//    prompt.AppendLine("      \"relationshipType\": \"ManyToOne\",");
//    prompt.AppendLine("      \"businessReason\": \"Link orders to customers\",");
//    prompt.AppendLine("      \"importance\": 10,");
//    prompt.AppendLine("      \"relationType\": \"WithinDomain\"");
//    prompt.AppendLine("    }");
//    prompt.AppendLine("  ],");
//    prompt.AppendLine("  \"crossDomainRelationships\": []");
//    prompt.AppendLine("}");
    
//    return prompt.ToString();
//}

//#endregion

//#region Response Parsers

//private DomainGroupingResult ParseDomainGroupingResponse(string response)
//{
//    var cleaned = CleanJsonResponse(response);
//    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
//    return JsonSerializer.Deserialize<DomainGroupingResult>(cleaned, options);
//}

//private TableAnalysisResult ParseTableAnalysisResponse(string response)
//{
//    var cleaned = CleanJsonResponse(response);
//    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
//    return JsonSerializer.Deserialize<TableAnalysisResult>(cleaned, options);
//}

//private ColumnAnalysisResult ParseColumnAnalysisResponse(string response)
//{
//    var cleaned = CleanJsonResponse(response);
//    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
//    return JsonSerializer.Deserialize<ColumnAnalysisResult>(cleaned, options);
//}

//private RelationshipAnalysisResult ParseRelationshipAnalysisResponse(string response)
//{
//    var cleaned = CleanJsonResponse(response);
//    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
//    return JsonSerializer.Deserialize<RelationshipAnalysisResult>(cleaned, options);
//}

//private string CleanJsonResponse(string response)
//{
//    if (string.IsNullOrWhiteSpace(response))
//        return "{}";

//    var cleaned = response.Trim();

//    // Remove markdown code blocks
//    if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
//        cleaned = cleaned.Substring(7);
//    else if (cleaned.StartsWith("```"))
//        cleaned = cleaned.Substring(3);

//    if (cleaned.EndsWith("```"))
//        cleaned = cleaned.Substring(0, cleaned.Length - 3);

//    cleaned = cleaned.Trim();

//    // Find JSON boundaries
//    int start = cleaned.IndexOf('{');
//    int end = cleaned.LastIndexOf('}');

//    if (start >= 0 && end > start)
//        return cleaned.Substring(start, end - start + 1);

//    return cleaned;
//}

//#endregion

//#region Helper Methods

//private ILLMService CreateLlmServiceForAnalysis()
//{
//    // Use configured LLM for schema analysis
//    return _llmFactory.CreateLlmService();
//}

//private async Task SaveAnalysisStateAsync(SequentialAnalysisState state)
//{
//    var database = await _databaseService.GetDatabaseByIdAsync(state.DatabaseId);
    
//    database.AnalysisStatus = state.Status.ToString();
//    database.AnalysisCurrentPhase = (int)state.CurrentPhase;
//    database.AnalysisLastUpdated = state.LastUpdatedAt;
//    database.AnalysisStartedAt = state.StartedAt;
//    database.AnalysisCompletedAt = state.CompletedAt;
    
//    // Save phase results
//    if (state.Phase1Result != null)
//    {
//        database.AnalysisPhase1Data = state.Phase1Result.DataJson;
//        database.AnalysisPhase1Status = state.Phase1Result.Status.ToString();
//        database.AnalysisPhase1Issues = JsonSerializer.Serialize(state.Phase1Result.Issues);
//        database.AnalysisPhase1RawResponse = state.Phase1Result.RawResponse;
//    }
    
//    if (state.Phase2Result != null)
//    {
//        database.AnalysisPhase2Data = state.Phase2Result.DataJson;
//        database.AnalysisPhase2Status = state.Phase2Result.Status.ToString();
//        database.AnalysisPhase2Issues = JsonSerializer.Serialize(state.Phase2Result.Issues);
//        database.AnalysisPhase2RawResponse = state.Phase2Result.RawResponse;
//    }
    
//    if (state.Phase3Result != null)
//    {
//        database.AnalysisPhase3Data = state.Phase3Result.DataJson;
//        database.AnalysisPhase3Status = state.Phase3Result.Status.ToString();
//        database.AnalysisPhase3Issues = JsonSerializer.Serialize(state.Phase3Result.Issues);
//        database.AnalysisPhase3RawResponse = state.Phase3Result.RawResponse;
//    }
    
//    if (state.Phase4Result != null)
//    {
//        database.AnalysisPhase4Data = state.Phase4Result.DataJson;
//        database.AnalysisPhase4Status = state.Phase4Result.Status.ToString();
//        database.AnalysisPhase4Issues = JsonSerializer.Serialize(state.Phase4Result.Issues);
//        database.AnalysisPhase4RawResponse = state.Phase4Result.RawResponse;
//    }
    
//    await _databaseService.UpdateDatabaseAsync(database);
//}

//private PhaseResult LoadPhaseResult(string dataJson, string status, string issues, string rawResponse, AnalysisPhase phase)
//{
//    if (string.IsNullOrEmpty(dataJson))
//        return null;
        
//    return new PhaseResult
//    {
//        Phase = phase,
//        Status = Enum.TryParse<PhaseStatus>(status, out var ps) ? ps : PhaseStatus.NotStarted,
//        DataJson = dataJson,
//        Issues = string.IsNullOrEmpty(issues) ? new List<string>() : 
//            JsonSerializer.Deserialize<List<string>>(issues),
//        RawResponse = rawResponse
//    };
//}

//private AnalysisStatus ParseAnalysisStatus(string status)
//{
//    return Enum.TryParse<AnalysisStatus>(status, out var result) ? result : AnalysisStatus.NotStarted;
//}

//#endregion

//    }
//}
