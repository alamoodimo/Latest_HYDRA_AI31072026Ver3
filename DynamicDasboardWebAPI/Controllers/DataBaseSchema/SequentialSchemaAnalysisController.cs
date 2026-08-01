//using DynamicDashboardCommon.Enums;
//using DynamicDasboardWebAPI.Services.SchemaAnalysis;
//using Microsoft.AspNetCore.Mvc;
//using System;
//using System.Threading.Tasks;

//namespace DynamicDasboardWebAPI.Controllers.DataBaseSchema
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class SequentialSchemaAnalysisController : ControllerBase
//    {
//        private readonly SequentialSchemaAnalysisService _analysisService;

//        public SequentialSchemaAnalysisController(
//    SequentialSchemaAnalysisService analysisService)  // ✓ Only this
//        {
//            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
//        }

//        /// <summary>
//        /// Start sequential schema analysis
//        /// </summary>
//        [HttpPost("start/{databaseId}")]
//        public async Task<IActionResult> StartAnalysis(int databaseId)
//        {
//            try
//            {
//                var result = await _analysisService.StartAnalysisAsync(databaseId);
//                return Ok(result);
//            }
//            catch (Exception ex)
//            {
//                throw;
//            }
//        }

//        /// <summary>
//        /// Get analysis status
//        /// </summary>
//        [HttpGet("status/{databaseId}")]
//        public async Task<IActionResult> GetStatus(int databaseId)
//        {
//            try
//            {
//                var result = await _analysisService.GetAnalysisStateAsync(databaseId);
//                return Ok(result);
//            }
//            catch (Exception ex)
//            {
//                throw;
//            }
//        }

//        /// <summary>
//        /// Continue to next phase
//        /// </summary>
//        [HttpPost("continue/{databaseId}")]
//        public async Task<IActionResult> ContinueAnalysis(int databaseId)
//        {
//            try
//            {
//                var result = await _analysisService.ContinueAnalysisAsync(databaseId);
//                return Ok(result);
//            }
//            catch (Exception ex)
//            {
//                throw;
//            }
//        }
//    }
//}
