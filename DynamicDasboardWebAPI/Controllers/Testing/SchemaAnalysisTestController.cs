using Microsoft.AspNetCore.Mvc;
using DynamicDasboardWebAPI.Services;
using DynamicDasboardWebAPI.Services.LLM;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

namespace DynamicDasboardWebAPI.Controllers.Testing
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchemaAnalysisTestController : ControllerBase
    {
        private readonly LLMServiceFactory _llmFactory;
        private readonly DatabaseSchemaService _schemaService;
        private readonly DatabaseService _databaseService;

        public SchemaAnalysisTestController(
            LLMServiceFactory llmFactory,
            DatabaseSchemaService schemaService,
            DatabaseService databaseService)
        {
            _llmFactory = llmFactory;
            _schemaService = schemaService;
            _databaseService = databaseService;
        }

        /// <summary>
        /// Compare both approaches side-by-side
        /// </summary>
        [HttpPost("compare/{databaseId}")]
        public async Task<IActionResult> CompareApproaches(int databaseId)
        {
            var llm = _llmFactory.CreateLlmService();

            // Phase 1 (same for both)
            var phase1Prompt = await BuildPhase1Prompt(databaseId);
            var phase1Response = await llm.GenerateSchemaAnalysisAsync(phase1Prompt);

            // Test WITH context
            var phase2WithContext = await BuildPhase2WithContextPrompt(databaseId, phase1Response);
            var responseWithContext = await llm.GenerateSchemaAnalysisAsync(phase2WithContext);

            // Test WITHOUT context (self-contained)
            var phase2SelfContained = await BuildPhase2SelfContainedPrompt(databaseId, phase1Response);
            var responseSelfContained = await llm.GenerateSchemaAnalysisAsync(phase2SelfContained);

            return Ok(new
            {
                Phase1 = new
                {
                    Prompt = phase1Prompt,
                    Response = phase1Response,
                    PromptLength = phase1Prompt.Length
                },
                Phase2_WithContext = new
                {
                    Prompt = phase2WithContext,
                    Response = responseWithContext,
                    PromptLength = phase2WithContext.Length
                },
                Phase2_SelfContained = new
                {
                    Prompt = phase2SelfContained,
                    Response = responseSelfContained,
                    PromptLength = phase2SelfContained.Length
                },
                Summary = new
                {
                    PromptLengthDifference = phase2SelfContained.Length - phase2WithContext.Length,
                    PromptLengthIncrease = $"{((phase2SelfContained.Length - phase2WithContext.Length) * 100.0 / phase2WithContext.Length):F1}%",
                    Note = "Compare the quality, coherence, and accuracy of both responses"
                }
            });
        }

        #region Prompt Builders

        private async Task<string> BuildPhase1Prompt(int databaseId)
        {
            var database = await _databaseService.GetDatabaseByIdAsync(databaseId);
            var schema = await _schemaService.GetSchemaObject(databaseId);

            var prompt = new StringBuilder();
            prompt.AppendLine("You are a database architecture expert.");
            prompt.AppendLine();
            prompt.AppendLine($"DATABASE: {database.Name}");
            prompt.AppendLine($"TOTAL TABLES: {schema.Tables.Count}");
            prompt.AppendLine();
            prompt.AppendLine("TABLE STRUCTURES:");
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            foreach (var table in schema.Tables)
            {
                prompt.AppendLine();
                prompt.AppendLine($"📋 {table.DBName} ({table.Columns.Count} columns)");

                var pk = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
                if (pk != null)
                {
                    prompt.AppendLine($"   Primary Key: {pk.DBName}");
                }

                var fks = table.Columns.Where(c => c.IsLookup).ToList();
                if (fks.Any())
                {
                    prompt.AppendLine("   Foreign Keys:");
                    foreach (var fk in fks)
                    {
                        prompt.AppendLine($"   - {fk.DBName}");
                    }
                }

                prompt.AppendLine($"   Columns: {string.Join(", ", table.Columns.Select(c => $"{c.DBName} ({c.DataType})"))}");
            }

            prompt.AppendLine();
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine();
            prompt.AppendLine("CONFIGURATION:");
            prompt.AppendLine("- Maximum tables per domain: 20");
            prompt.AppendLine();
            prompt.AppendLine("YOUR TASK:");
            prompt.AppendLine("Group these tables into main business domains based on their relationships and purpose.");
            prompt.AppendLine();
            prompt.AppendLine("OUTPUT (pure JSON, no markdown):");
            prompt.AppendLine("{");
            prompt.AppendLine("  \"domains\": [");
            prompt.AppendLine("    {");
            prompt.AppendLine("      \"name\": \"Sales\",");
            prompt.AppendLine("      \"description\": \"Sales order processing\",");
            prompt.AppendLine("      \"tableNames\": [\"Orders\", \"Customers\"],");
            prompt.AppendLine("      \"sharedTablesReferenced\": [\"Statuses\"]");
            prompt.AppendLine("    }");
            prompt.AppendLine("  ],");
            prompt.AppendLine("  \"sharedTables\": [");
            prompt.AppendLine("    {");
            prompt.AppendLine("      \"tableName\": \"Statuses\",");
            prompt.AppendLine("      \"referencedByDomains\": [\"Sales\"]");
            prompt.AppendLine("    }");
            prompt.AppendLine("  ]");
            prompt.AppendLine("}");

            return prompt.ToString();
        }

        private async Task<string> BuildPhase2WithContextPrompt(int databaseId, string phase1Response)
        {
            var schema = await _schemaService.GetSchemaObject(databaseId);

            var prompt = new StringBuilder();
            prompt.AppendLine("ANALYZING TABLES IN DOMAIN");
            prompt.AppendLine();
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine("CONTEXT FROM PHASE 1:");
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine("In Phase 1, you analyzed the database and identified business domains.");
            prompt.AppendLine("Your Phase 1 output was:");
            prompt.AppendLine(phase1Response);
            prompt.AppendLine();
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine("NOW ANALYZE THE FIRST DOMAIN:");
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine();

            // Add first 3 tables for analysis
            var tablesToAnalyze = schema.Tables.Take(3).ToList();
            foreach (var table in tablesToAnalyze)
            {
                prompt.AppendLine($"📋 {table.DBName}");
                prompt.AppendLine($"   Columns: {string.Join(", ", table.Columns.Select(c => $"{c.DBName} ({c.DataType})"))}");
                prompt.AppendLine();
            }

            prompt.AppendLine("YOUR TASK:");
            prompt.AppendLine("Based on your Phase 1 domain grouping, analyze these tables.");
            prompt.AppendLine("Provide friendly names, descriptions, and categories (FACT or DIMENSION).");
            prompt.AppendLine();
            prompt.AppendLine("OUTPUT (pure JSON, no markdown):");
            prompt.AppendLine("{");
            prompt.AppendLine("  \"tables\": [");
            prompt.AppendLine("    {");
            prompt.AppendLine("      \"tableName\": \"TableName\",");
            prompt.AppendLine("      \"friendlyName\": \"Friendly Name\",");
            prompt.AppendLine("      \"description\": \"Business description\",");
            prompt.AppendLine("      \"category\": \"FACT\"");
            prompt.AppendLine("    }");
            prompt.AppendLine("  ]");
            prompt.AppendLine("}");

            return prompt.ToString();
        }

        private async Task<string> BuildPhase2SelfContainedPrompt(int databaseId, string phase1Response)
        {
            var schema = await _schemaService.GetSchemaObject(databaseId);

            // Parse Phase 1 response to extract domain info
            var firstDomain = ParseFirstDomain(phase1Response);

            var prompt = new StringBuilder();
            prompt.AppendLine("ANALYZING TABLES IN DOMAIN");
            prompt.AppendLine();
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine("DOMAIN INFORMATION (from database):");
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            if (firstDomain != null)
            {
                prompt.AppendLine($"Domain Name: {firstDomain.Name}");
                prompt.AppendLine($"Description: {firstDomain.Description}");
                if (firstDomain.TableNames?.Any() == true)
                {
                    prompt.AppendLine($"Tables in Domain: {string.Join(", ", firstDomain.TableNames)}");
                }
                if (firstDomain.SharedTablesReferenced?.Any() == true)
                {
                    prompt.AppendLine($"Shared Tables Used: {string.Join(", ", firstDomain.SharedTablesReferenced)}");
                }
            }

            prompt.AppendLine();
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine("TABLE STRUCTURES:");
            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine();

            // Add detailed table structures (first 3 tables)
            var tablesToAnalyze = schema.Tables.Take(3).ToList();
            foreach (var table in tablesToAnalyze)
            {
                prompt.AppendLine($"📋 {table.DBName}");

                var pk = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
                if (pk != null)
                {
                    prompt.AppendLine($"   Primary Key: {pk.DBName}");
                }

                var fks = table.Columns.Where(c => c.IsLookup).ToList();
                if (fks.Any())
                {
                    prompt.AppendLine("   Foreign Keys:");
                    foreach (var fk in fks)
                    {
                        prompt.AppendLine($"   - {fk.DBName}");
                    }
                }

                prompt.AppendLine("   Columns:");
                foreach (var col in table.Columns)
                {
                    var nullable = col.IsNullable ? "NULLABLE" : "NOT NULL";
                    var keyInfo = col.IsPrimaryKey ? ", PK" : (col.IsLookup ? ", FK" : "");
                    prompt.AppendLine($"   - {col.DBName} ({col.DataType}, {nullable}{keyInfo})");
                }
                prompt.AppendLine();
            }

            prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            prompt.AppendLine();
            prompt.AppendLine("YOUR TASK:");
            prompt.AppendLine("Analyze each table and provide:");
            prompt.AppendLine("1. Friendly name");
            prompt.AppendLine("2. Business description (max 150 chars)");
            prompt.AppendLine("3. Category (FACT or DIMENSION)");
            prompt.AppendLine("4. Business importance (1-10)");
            prompt.AppendLine();
            prompt.AppendLine("OUTPUT (pure JSON, no markdown):");
            prompt.AppendLine("{");
            prompt.AppendLine("  \"tables\": [");
            prompt.AppendLine("    {");
            prompt.AppendLine("      \"tableName\": \"TableName\",");
            prompt.AppendLine("      \"friendlyName\": \"Friendly Name\",");
            prompt.AppendLine("      \"description\": \"Business description\",");
            prompt.AppendLine("      \"category\": \"FACT\",");
            prompt.AppendLine("      \"businessImportance\": 10");
            prompt.AppendLine("    }");
            prompt.AppendLine("  ]");
            prompt.AppendLine("}");

            return prompt.ToString();
        }

        private DomainInfo ParseFirstDomain(string response)
        {
            try
            {
                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var parsed = JsonSerializer.Deserialize<DomainResponse>(json, options);

                    return parsed?.Domains?.FirstOrDefault();
                }
            }
            catch
            {
                // If parsing fails, return null
            }

            return null;
        }

        #endregion
    }

    #region Helper Classes

    public class DomainInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> TableNames { get; set; }
        public List<string> SharedTablesReferenced { get; set; }
    }

    public class DomainResponse
    {
        public List<DomainInfo> Domains { get; set; }
    }

    #endregion
}
