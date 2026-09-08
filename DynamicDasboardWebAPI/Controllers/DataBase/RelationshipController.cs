using DynamicDasboardWebAPI.Services;
using DynamicDashboardCommon.Enums;
using DynamicDashboardCommon.Models;
using Microsoft.AspNetCore.Mvc;
using MySqlX.XDevAPI.Relational;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DynamicDasboardWebAPI.Controllers
{
    /// <summary>
    /// API Controller for managing relationships between tables.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class RelationshipsController : AppControllerBase
    {
        private readonly RelationshipService _service;
        private readonly ILogsService _logsService;

        /// <summary>
        /// Initializes a new instance of the <see cref="RelationshipsController"/> class.
        /// </summary>
        /// <param name="service">The relationship service.</param>
        public RelationshipsController(RelationshipService service,
        ILogsService logsService)
        : base(logsService)
        {
            _service = service;
            logsService=_logsService;
        }

        /// <summary>
        /// Gets the relationships for a specific table.
        /// </summary>
        /// <param name="tableId">The ID of the table.</param>
        /// <returns>A list of relationships for the specified table.</returns>
        [HttpGet("table/{tableId}")]
        public async Task<ActionResult<IEnumerable<Relationship>>> GetRelationshipsByTableId(int tableId)
        {
            try
            {
                await _logsService
         .AddLogAsync(GetUserId(),
         "Run GetRelationshipsByTableId",
         $"The GetRelationshipsByTableId has been started for Table Id: {tableId}");
                var relationships = await _service.GetRelationshipsByTableIdAsync(tableId);
                return Ok(relationships);
            }
            catch (Exception ex)

            {
                return await HandleExceptionAsync(ex, EnumLoggingType.Error.ToString());
            }
        }

        /// <summary>
        /// Adds a new relationship.
        /// </summary>
        /// <param name="relationship">The relationship to add.</param>
        /// <returns>The ID of the newly created relationship.</returns>
        [HttpPost]
        public async Task<ActionResult<int>> AddRelationship([FromBody] Relationship relationship)
        {
            await _logsService
.AddLogAsync(GetUserId(),
"Run AddRelationship",
$"The AddRelationship has been started for Table Id: {relationship.TableID} " +
$"and Relationship Id :{relationship.RelationshipID}");
            var result = await _service.AddRelationshipAsync(relationship);
            return Ok(result);
        }

        /// <summary>
        /// Updates an existing relationship.
        /// </summary>
        /// <param name="relationshipId">The ID of the relationship to update.</param>
        /// <param name="relationship">The updated relationship data.</param>
        /// <returns>The ID of the updated relationship.</returns>
        [HttpPut("{relationshipId}")]
        public async Task<ActionResult<int>> UpdateRelationship(int relationshipId, [FromBody] Relationship relationship)
        {

            await _logsService
.AddLogAsync(GetUserId(),
"Run UpdateRelationship",
$"The UpdateRelationship has been started for Table Id: {relationship.TableID} " +
$"and Relationship Id :{relationship.RelationshipID}");
            if (relationshipId != relationship.RelationshipID)
                return BadRequest("Relationship ID mismatch.");

            var result = await _service.UpdateRelationshipAsync(relationship);
            return Ok(result);
        }

        /// <summary>
        /// Deletes a relationship.
        /// </summary>
        /// <param name="relationshipId">The ID of the relationship to delete.</param>
        /// <returns>The ID of the deleted relationship.</returns>
        [HttpDelete("{relationshipId}")]
        public async Task<ActionResult<int>> DeleteRelationship(int relationshipId)
        {
            await _logsService
.AddLogAsync(GetUserId(),
"Run DeleteRelationship",
$"The DeleteRelationship has been started for  Relationship Id :{relationshipId}");
            var result = await _service.DeleteRelationshipAsync(relationshipId);
            return Ok(result);
        }
    }
}