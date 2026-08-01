using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace DynamicDashboardCommon.Models
{
    /// <summary>
    /// Represents a subscription plan with usage limits.
    /// </summary>
    [Table("Plans")]
    public class Plan
    {
        /// <summary>
        /// Unique identifier for the plan
        /// </summary>
        [Key]
        public int PlanID { get; set; }

        /// <summary>
        /// Display name of the plan
        /// </summary>
        [Required]
        [StringLength(50)]
        public string PlanName { get; set; }

        /// <summary>
        /// Unique code for the plan (FREE, STARTER, PRO, PROMAX)
        /// </summary>
        [Required]
        [StringLength(20)]
        public string PlanCode { get; set; }

        /// <summary>
        /// Maximum number of admin users allowed (-1 for unlimited)
        /// </summary>
        public int MaxAdmins { get; set; } = 1;

        /// <summary>
        /// Maximum number of business users allowed (-1 for unlimited)
        /// </summary>
        public int MaxBusinessUsers { get; set; } = 3;

        /// <summary>
        /// Maximum number of database connections allowed (-1 for unlimited)
        /// </summary>
        public int MaxDatabases { get; set; } = 1;

        /// <summary>
        /// Maximum number of dashboards allowed (-1 for unlimited)
        /// </summary>
        public int MaxDashboards { get; set; } = 1;

        /// <summary>
        /// Monthly subscription price
        /// </summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal MonthlyPrice { get; set; } = 0;

        /// <summary>
        /// Yearly subscription price (typically discounted)
        /// </summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal YearlyPrice { get; set; } = 0;

        /// <summary>
        /// Whether this plan is currently available for purchase
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// JSON array of feature flags for this plan
        /// </summary>
        public string Features { get; set; }

        /// <summary>
        /// When the plan was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ============================================
        // NAVIGATION PROPERTIES
        // ============================================

        /// <summary>
        /// Companies subscribed to this plan
        /// </summary>
        public virtual ICollection<Company> Companies { get; set; } = new List<Company>();

        // ============================================
        // HELPER METHODS
        // ============================================

        /// <summary>
        /// Checks if a limit is unlimited (-1)
        /// </summary>
        public bool IsUnlimited(int limit) => limit == -1;

        /// <summary>
        /// Checks if the plan is free
        /// </summary>
        public bool IsFree => PlanCode == PlanCodes.Free;

        /// <summary>
        /// Gets the feature list as an array
        /// </summary>
        public List<string> GetFeatures()
        {
            if (string.IsNullOrEmpty(Features))
                return new List<string>();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(Features) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Checks if the plan has a specific feature
        /// </summary>
        public bool HasFeature(string feature)
        {
            return GetFeatures().Contains(feature, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if adding one more of the specified resource would exceed the limit
        /// </summary>
        public bool WouldExceedLimit(int currentCount, int maxLimit)
        {
            if (maxLimit == -1) return false; // Unlimited
            return currentCount >= maxLimit;
        }
    }

    /// <summary>
    /// Plan code constants
    /// </summary>
    public static class PlanCodes
    {
        public const string Free = "FREE";
        public const string Starter = "STARTER";
        public const string Pro = "PRO";
        public const string ProMax = "PROMAX";
    }

    /// <summary>
    /// Feature flag constants
    /// </summary>
    public static class PlanFeatures
    {
        public const string BasicCharts = "basic_charts";
        public const string AdvancedCharts = "advanced_charts";
        public const string AiAssistant = "ai_assistant";
        public const string EmailSupport = "email_support";
        public const string PrioritySupport = "priority_support";
        public const string DedicatedSupport = "dedicated_support";
        public const string ExportPdf = "export_pdf";
        public const string ApiAccess = "api_access";
        public const string CustomBranding = "custom_branding";
        public const string Sso = "sso";
        public const string Unlimited = "unlimited";
    }
}