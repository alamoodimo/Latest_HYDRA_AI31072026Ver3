using System;
using System.Threading.Tasks;
using DynamicDashboardCommon.Enums;
using DynamicDashboardCommon.Models;
using DynamicDasboardWebAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace DynamicDasboardWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchemaAnalysisController : AppControllerBase
    {
        private readonly SchemaAnalysisService _analysisService;
        private readonly ILogsService _logsService;
        public SchemaAnalysisController(
            SchemaAnalysisService analysisService,
            ILogsService logsService)
            : base(logsService)
        {
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _logsService = logsService ?? throw new ArgumentNullException(nameof(analysisService));
        }

        #region Full Schema Analysis (Legacy)

        /// <summary>
        /// Analyzes database schema using LLM to generate descriptions and identify conflicts
        /// </summary>
        /// <param name="databaseId">The ID of the database to analyze</param>
        /// <returns>Schema analysis result</returns>
        [HttpGet("AnalyzeDatabaseSchema/{databaseId}")]
        public async Task<IActionResult> AnalyzeDatabaseSchema(int databaseId)
        {
            try
            {
           await     _logsService
                    .AddLogAsync(GetUserId(),
                    "Run Analysis",
                    $"The Analyse Schema has been started for Database Id: {databaseId}");
                var result = await _analysisService.AnalyzeDatabaseSchemaAsync(databaseId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        /// <summary>
        /// Applies schema analysis results to update database schema metadata.
        /// Called by admin after reviewing suggestions in UI.
        /// </summary>
        /// <param name="databaseId">The ID of the database</param>
        /// <param name="analysisData">The analysis data to apply</param>
        /// <returns>Success indicator</returns>
        [HttpPost("ApplySchemaAnalysisResults/{databaseId}")]
        public async Task<IActionResult> ApplySchemaAnalysisResults(int databaseId, [FromBody] SchemaAnalysisData analysisData)
        {
            try
            {
                var result = await _analysisService.ApplySchemaAnalysisResultsAsync(databaseId, analysisData);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        #endregion

        #region Term Mappings

        /// <summary>
        /// Suggests term mappings using LLM analysis
        /// </summary>
        [HttpGet("SuggestTerms/{databaseId}")]
        public async Task<IActionResult> SuggestTermMappings(int databaseId)
        {
            try
            {
                var suggestions = await _analysisService.SuggestTermMappingsAsync(databaseId);
                return Ok(suggestions);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        #endregion

        #region Phased Analysis with Schema String (Legacy)

        [HttpPost("analyze-tables")]
        public async Task<IActionResult> AnalyzeTablesOnly([FromBody] SchemaAnalysisRequest request)
        {
            try
            {
                var result = await _analysisService.AnalyzeTablesAsync(request.DatabaseId, request.SchemaString);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        [HttpPost("analyze-columns")]
        public async Task<IActionResult> AnalyzeColumnsOnly([FromBody] SchemaAnalysisRequest request)
        {
            try
            {
                var result = await _analysisService.AnalyzeColumnsAsync(request.DatabaseId, request.SchemaString);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        [HttpPost("analyze-relationships")]
        public async Task<IActionResult> AnalyzeRelationshipsOnly([FromBody] SchemaAnalysisRequest request)
        {
            try
            {
                var result = await _analysisService.AnalyzeRelationshipsAsync(request.DatabaseId, request.SchemaString);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        [HttpPost("analyze-conflicts")]
        public async Task<IActionResult> AnalyzeConflictsOnly([FromBody] SchemaAnalysisRequest request)
        {
            try
            {
                var result = await _analysisService.AnalyzeConflictsAsync(request.DatabaseId, request.SchemaString);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        [HttpPost("analyze-term-mappings")]
        public async Task<IActionResult> AnalyzeTermMappings([FromBody] SchemaAnalysisRequest request)
        {
            try
            {
                var result = await _analysisService.GenerateTermMappingsAsync(request.DatabaseId, request.SchemaString);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        #endregion

        #region Smart Phased Analysis (Sequential Processing)

        /// <summary>
        /// Phase 1: Analyze tables sequentially.
        /// Returns results for admin review - does NOT auto-save.
        /// Admin must call ApplySchemaAnalysisResults to save.
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        /// <param name="forceReanalyze">If true, re-analyze all tables even if already analyzed</param>
        [HttpPost("analyze-tables-parallel/{databaseId}")]
        public async Task<IActionResult> AnalyzeTablesParallel(int databaseId, [FromQuery] bool forceReanalyze = false)
        {
            try
            {
                var result = await _analysisService.AnalyzeTablesSmartAsync(databaseId, forceReanalyze);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        /// <summary>
        /// Phase 2: Analyze columns sequentially.
        /// Returns results for admin review - does NOT auto-save.
        /// Admin must call ApplySchemaAnalysisResults to save.
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        /// <param name="forceReanalyze">If true, re-analyze all columns even if already analyzed</param>
        [HttpPost("analyze-columns-parallel/{databaseId}")]
        public async Task<IActionResult> AnalyzeColumnsParallel(int databaseId, [FromQuery] bool forceReanalyze = false)
        {
            try
            {
                var result = await _analysisService.AnalyzeColumnsSmartAsync(databaseId, forceReanalyze);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        /// <summary>
        /// Phase 3: Analyze relationships using rule-based + LLM hybrid.
        /// Returns results for admin review - does NOT auto-save.
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        [HttpPost("analyze-relationships-parallel/{databaseId}")]
        public async Task<IActionResult> AnalyzeRelationshipsParallel(int databaseId)
        {
            try
            {
                var result = await _analysisService.AnalyzeRelationshipsSmartAsync(databaseId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        /// <summary>
        /// Get current analysis status for the database.
        /// Shows how many tables/columns have been analyzed.
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        [HttpGet("analysis-status/{databaseId}")]
        public async Task<IActionResult> GetAnalysisStatus(int databaseId)
        {
            try
            {
                var status = await _analysisService.GetAnalysisStatusAsync(databaseId);
                return Ok(status);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }



        #endregion

        #region Chunked Analysis Endpoints (Progressive Loading)

        /// <summary>
        /// Gets counts of items needing analysis (for progress bar setup)
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        /// <param name="forceReanalyze">If true, count all items regardless of existing data</param>
        [HttpGet("analysis-counts/{databaseId}")]
        public async Task<IActionResult> GetAnalysisCounts(int databaseId, [FromQuery] bool forceReanalyze = false)
        {
            try
            {
                var counts = await _analysisService.GetAnalysisCountsAsync(databaseId, forceReanalyze);
                return Ok(counts);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        /// <summary>
        /// Analyzes tables in chunks for progressive loading.
        /// Call repeatedly with increasing skip values until IsComplete = true.
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        /// <param name="skip">Number of tables to skip (start at 0)</param>
        /// <param name="take">Number of tables to analyze per chunk (default 5)</param>
        /// <param name="forceReanalyze">If true, re-analyze all tables</param>
        [HttpPost("analyze-tables-chunked/{databaseId}")]
        public async Task<IActionResult> AnalyzeTablesChunked(
            int databaseId,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 5,
            [FromQuery] bool forceReanalyze = false)
        {
            try
            {
                // Validate take parameter (prevent abuse)
                if (take < 1) take = 1;
                if (take > 20) take = 20;

                var result = await _analysisService.AnalyzeTablesChunkedAsync(
                    databaseId, skip, take, forceReanalyze);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        /// <summary>
        /// Analyzes columns in chunks for progressive loading.
        /// Call repeatedly with increasing skip values until IsComplete = true.
        /// </summary>
        /// <param name="databaseId">Database ID</param>
        /// <param name="skip">Number of columns to skip (start at 0)</param>
        /// <param name="take">Number of columns to analyze per chunk (default 10)</param>
        /// <param name="forceReanalyze">If true, re-analyze all columns</param>
        [HttpPost("analyze-columns-chunked/{databaseId}")]
        public async Task<IActionResult> AnalyzeColumnsChunked(
            int databaseId,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 10,
            [FromQuery] bool forceReanalyze = false)
        {
            try
            {
                // Validate take parameter
                if (take < 1) take = 1;
                if (take > 50) take = 50;

                var result = await _analysisService.AnalyzeColumnsChunkedAsync(
                    databaseId, skip, take, forceReanalyze);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        #endregion
    }
}