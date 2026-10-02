using DynamicDashboardCommon.Models;

namespace DynamicDasboardWebAPI.Services
{
    /// <summary>
    /// Checks and repairs the SQL of a single dashboard component.
    /// Shared by the Dashboard Builder's "Fix with AI" and by dashboard generation.
    /// </summary>
    public interface IComponentSqlRepairService
    {
        /// <summary>
        /// Runs the SQL with a 1-row limit and a short timeout. Valid when it runs, is a single
        /// read-only SELECT, and returns at least one row.
        /// </summary>
        Task<ComponentSqlValidationResult> ValidateAsync(string sql, int databaseId);

        /// <summary>
        /// Returns working SQL for the component: the current SQL when it already works, otherwise
        /// SQL corrected by the AI (same purpose and type), checked after every attempt.
        /// </summary>
        Task<ComponentSqlRepairResult> RepairAsync(ComponentSqlRepairRequest request);
    }
}
