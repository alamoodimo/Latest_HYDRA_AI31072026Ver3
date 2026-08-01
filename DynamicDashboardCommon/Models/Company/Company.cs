using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Numerics;

namespace DynamicDashboardCommon.Models
{
    /// <summary>
    /// Represents a company/tenant in the multi-tenant system.
    /// Each company has isolated data (dashboards, databases, users).
    /// </summary>
    [Table("Companies")]
    public class Company
    {
        /// <summary>
        /// Unique identifier for the company
        /// </summary>
        [Key]
        public int CompanyID { get; set; }

        /// <summary>
        /// Company name (required during signup)
        /// </summary>
        [Required(ErrorMessage = "Company name is required")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Company name must be between 2 and 200 characters")]
        public string CompanyName { get; set; }

        /// <summary>
        /// Email domain extracted from admin's work email (e.g., 'acme.com')
        /// Used to suggest company for users with same domain
        /// </summary>
        [StringLength(100)]
        public string Domain { get; set; }

        /// <summary>
        /// Company size category
        /// </summary>
        [StringLength(50)]
        public string Size { get; set; }

        /// <summary>
        /// Industry/sector of the company
        /// </summary>
        [StringLength(100)]
        public string Industry { get; set; }

        /// <summary>
        /// Current subscription plan ID
        /// </summary>
        public int PlanID { get; set; } = 1; // Default to Free plan

        /// <summary>
        /// When the current plan started
        /// </summary>
        public DateTime? PlanStartDate { get; set; }

        /// <summary>
        /// When the current plan expires (null for Free plan)
        /// </summary>
        public DateTime? PlanEndDate { get; set; }

        /// <summary>
        /// Whether the company account is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When the company was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        // ============================================
        // NAVIGATION PROPERTIES
        // ============================================

        /// <summary>
        /// The subscription plan for this company
        /// </summary>
        [ForeignKey("PlanID")]
        public virtual Plan Plan { get; set; }

        /// <summary>
        /// All users belonging to this company
        /// </summary>
        public virtual ICollection<User> Users { get; set; } = new List<User>();

        /// <summary>
        /// All databases connected by this company
        /// </summary>
        public virtual ICollection<Database> Databases { get; set; } = new List<Database>();

        /// <summary>
        /// All dashboards created by this company
        /// </summary>
        public virtual ICollection<DashboardModel> Dashboards { get; set; } = new List<DashboardModel>();

        // ============================================
        // HELPER METHODS
        // ============================================

        /// <summary>
        /// Extracts domain from an email address
        /// </summary>
        public static string ExtractDomainFromEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
                return null;

            return email.Substring(email.IndexOf('@') + 1).ToLowerInvariant();
        }

        /// <summary>
        /// Checks if the company's plan has expired
        /// </summary>
        public bool IsPlanExpired()
        {
            if (!PlanEndDate.HasValue)
                return false; // Free plan never expires

            return PlanEndDate.Value < DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Company size options for dropdown
    /// </summary>
    public static class CompanySizeOptions
    {
        public const string Size1To10 = "1-10";
        public const string Size11To50 = "11-50";
        public const string Size51To200 = "51-200";
        public const string Size201To500 = "201-500";
        public const string Size500Plus = "500+";

        public static List<SelectOption> GetOptions()
        {
            return new List<SelectOption>
            {
                new SelectOption { Value = Size1To10, Label = "1-10 employees" },
                new SelectOption { Value = Size11To50, Label = "11-50 employees" },
                new SelectOption { Value = Size51To200, Label = "51-200 employees" },
                new SelectOption { Value = Size201To500, Label = "201-500 employees" },
                new SelectOption { Value = Size500Plus, Label = "500+ employees" }
            };
        }
    }

    /// <summary>
    /// Generic select option for dropdowns
    /// </summary>
    public class SelectOption
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }
}