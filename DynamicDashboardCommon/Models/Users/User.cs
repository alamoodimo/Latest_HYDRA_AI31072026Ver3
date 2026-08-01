using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DynamicDashboardCommon.Models
{
    /// <summary>
    /// Represents a user in the system.
    /// Users belong to a company and have role-based access.
    /// </summary>
    [Table("Users")]
    public class User
    {
        /// <summary>
        /// Unique identifier for the user
        /// </summary>
        [Key]
        public int UserID { get; set; }

        /// <summary>
        /// Username (typically the email)
        /// </summary>
        [Required]
        [StringLength(100)]
        public string Username { get; set; }

        /// <summary>
        /// User's email address (work email)
        /// </summary>
        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; }

        /// <summary>
        /// User's full name
        /// </summary>
        [StringLength(200)]
        public string FullName { get; set; }

        /// <summary>
        /// Hashed password (BCrypt)
        /// </summary>
        [Required]
        [StringLength(255)]
        public string PasswordHash { get; set; }

        /// <summary>
        /// Role ID (1 = Admin, 2 = BusinessUser, 3 = SuperAdmin)
        /// </summary>
        [Required]
        public int RoleID { get; set; }

        /// <summary>
        /// Company the user belongs to
        /// </summary>
        public int? CompanyID { get; set; }

        /// <summary>
        /// Whether the user's email has been verified
        /// </summary>
        public bool EmailVerified { get; set; } = false;

        /// <summary>
        /// Whether the user account is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Number of consecutive failed login attempts
        /// </summary>
        public int FailedLoginAttempts { get; set; } = 0;

        /// <summary>
        /// Account locked until this time (null if not locked)
        /// </summary>
        public DateTime? LockedUntil { get; set; }

        /// <summary>
        /// Whether user must change password on next login
        /// </summary>
        public bool ForcePasswordChange { get; set; } = false;

        /// <summary>
        /// When the password was last changed
        /// </summary>
        public DateTime? PasswordChangedAt { get; set; }

        /// <summary>
        /// User ID of the admin who invited this user (for business users)
        /// </summary>
        public int? InvitedBy { get; set; }

        /// <summary>
        /// Whether two-factor authentication is enabled
        /// </summary>
        public bool TwoFactorEnabled { get; set; } = false;

        /// <summary>
        /// Secret key for TOTP-based 2FA (encrypted)
        /// </summary>
        [StringLength(100)]
        public string TwoFactorSecret { get; set; }

        /// <summary>
        /// Last successful login timestamp
        /// </summary>
        public DateTime? LastLogin { get; set; }

        /// <summary>
        /// When the user account was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Legacy field - comma-separated database IDs (deprecated, use UserDatabaseAccess)
        /// </summary>
        [Obsolete("Use UserDatabaseAccess table instead")]
        public string AllowedDatabases { get; set; }

        // ============================================
        // NAVIGATION PROPERTIES
        // ============================================

        /// <summary>
        /// The user's role
        /// </summary>
        [ForeignKey("RoleID")]
        public virtual UserRole Role { get; set; }

        /// <summary>
        /// The company this user belongs to
        /// </summary>
        [ForeignKey("CompanyID")]
        public virtual Company Company { get; set; }

        /// <summary>
        /// The user who invited this user (for business users)
        /// </summary>
        [ForeignKey("InvitedBy")]
        public virtual User InvitedByUser { get; set; }

        /// <summary>
        /// Dashboards this user has explicit access to
        /// </summary>
        public virtual ICollection<UserDashboardAccess> DashboardAccess { get; set; } = new List<UserDashboardAccess>();

        /// <summary>
        /// Active sessions for this user
        /// </summary>
        public virtual ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();

        // ============================================
        // COMPUTED PROPERTIES
        // ============================================

        /// <summary>
        /// Whether the account is currently locked
        /// </summary>
        [NotMapped]
        public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow;

        /// <summary>
        /// Whether this user is an admin
        /// </summary>
        [NotMapped]
        public bool IsAdmin => RoleID == (int)UserRoleEnum.Admin || RoleID == (int)UserRoleEnum.SuperAdmin;

        /// <summary>
        /// Whether this user is a business user
        /// </summary>
        [NotMapped]
        public bool IsBusinessUser => RoleID == (int)UserRoleEnum.BusinessUser;

        /// <summary>
        /// Whether this user is a super admin
        /// </summary>
        [NotMapped]
        public bool IsSuperAdmin => RoleID == (int)UserRoleEnum.SuperAdmin;

        /// <summary>
        /// Display name (full name or username)
        /// </summary>
        [NotMapped]
        public string DisplayName => !string.IsNullOrWhiteSpace(FullName) ? FullName : Username;

        // ============================================
        // HELPER METHODS
        // ============================================

        /// <summary>
        /// Increments failed login attempts and locks account if threshold reached
        /// </summary>
        public void RecordFailedLogin(int maxAttempts = 5, int lockoutHours = 24)
        {
            FailedLoginAttempts++;
            if (FailedLoginAttempts >= maxAttempts)
            {
                LockedUntil = DateTime.UtcNow.AddHours(lockoutHours);
            }
        }

        /// <summary>
        /// Resets failed login attempts on successful login
        /// </summary>
        public void RecordSuccessfulLogin()
        {
            FailedLoginAttempts = 0;
            LockedUntil = null;
            LastLogin = DateTime.UtcNow;
        }

        /// <summary>
        /// Checks if the account can attempt login
        /// </summary>
        public bool CanAttemptLogin()
        {
            if (!IsActive) return false;
            if (IsLocked) return false;
            return true;
        }
    }

    /// <summary>
    /// User role enum
    /// </summary>
    public enum UserRoleEnum
    {
        Admin = 1,
        BusinessUser = 2,
        SuperAdmin = 3
    }

    /// <summary>
    /// User role entity
    /// </summary>
    [Table("UserRoles")]
    public class UserRole
    {
        [Key]
        public int RoleID { get; set; }

        [Required]
        [StringLength(50)]
        public string RoleName { get; set; }

        // Navigation
        public virtual ICollection<User> Users { get; set; } = new List<User>();
    }

    /// <summary>
    /// User session for token management
    /// </summary>
    [Table("UserSessions")]
    public class UserSession
    {
        [Key]
        public int SessionID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        [StringLength(500)]
        public string RefreshToken { get; set; }

        [StringLength(500)]
        public string DeviceInfo { get; set; }

        [StringLength(50)]
        public string IPAddress { get; set; }

        public DateTime ExpiresAt { get; set; }

        public bool IsRevoked { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        [ForeignKey("UserID")]
        public virtual User User { get; set; }
    }

    /// <summary>
    /// User access to specific dashboards
    /// </summary>
    [Table("UserDashboardAccess")]
    public class UserDashboardAccess
    {
        [Key]
        public int AccessID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        public int DashboardID { get; set; }

        [Required]
        [StringLength(20)]
        public string AccessLevel { get; set; } = "VIEW"; // VIEW, EDIT, ADMIN

        public int GrantedBy { get; set; }

        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        [ForeignKey("UserID")]
        public virtual User User { get; set; }

        [ForeignKey("DashboardID")]
        public virtual DashboardModel Dashboard { get; set; }

        [ForeignKey("GrantedBy")]
        public virtual User GrantedByUser { get; set; }
    }

    /// <summary>
    /// Access level constants
    /// </summary>
    public static class AccessLevels
    {
        public const string View = "VIEW";
        public const string Edit = "EDIT";
        public const string Admin = "ADMIN";
    }
}