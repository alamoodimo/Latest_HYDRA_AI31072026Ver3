using DynamicDashboardCommon.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DynamicDashboardCommon.DTOs
{
    // ============================================
    // SIGNUP DTOs
    // ============================================

    /// <summary>
    /// Step 1: Initial signup request - sends verification code
    /// </summary>
    public class SignupInitRequest
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(255)]
        public string Email { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 200 characters")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Company name is required")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Company name must be between 2 and 200 characters")]
        public string CompanyName { get; set; }

        [StringLength(50)]
        public string CompanySize { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
        [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
            ErrorMessage = "Password must contain at least one letter and one number")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Please confirm your password")]
        [Compare("Password", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; }
    }

    /// <summary>
    /// Step 2: Verify email with code
    /// </summary>
    public class VerifyEmailRequest
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Verification code is required")]
        [StringLength(4, MinimumLength = 4, ErrorMessage = "Code must be 4 digits")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "Code must be 4 digits")]
        public string Code { get; set; }
    }

    /// <summary>
    /// Optional: Add business user during signup
    /// </summary>
    public class AddBusinessUserRequest
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(255)]
        public string Email { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        [StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
        [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
            ErrorMessage = "Password must contain at least one letter and one number")]
        public string Password { get; set; }

        /// <summary>
        /// Whether to force password change on first login
        /// </summary>
        public bool ForcePasswordChange { get; set; } = false;
    }

    /// <summary>
    /// Response after successful signup
    /// </summary>
    public class SignupResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int? UserId { get; set; }
        public int? CompanyId { get; set; }
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public UserInfoDto User { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    // ============================================
    // LOGIN DTOs
    // ============================================

    /// <summary>
    /// Login request
    /// </summary>
    public class LoginRequest
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; }

        /// <summary>
        /// Remember me option for extended session
        /// </summary>
        public bool RememberMe { get; set; } = false;
    }

    /// <summary>
    /// Login response
    /// </summary>
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public UserInfoDto User { get; set; }
        public bool RequiresTwoFactor { get; set; } = false;
        public bool RequiresPasswordChange { get; set; } = false;
        public string TwoFactorToken { get; set; } // Temporary token for 2FA flow
    }

    /// <summary>
    /// Two-factor authentication request
    /// </summary>
    public class TwoFactorRequest
    {
        [Required]
        public string TwoFactorToken { get; set; }

        [Required]
        [StringLength(4, MinimumLength = 4)]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "Code must be 4 digits")]
        public string Code { get; set; }
    }

    /// <summary>
    /// Refresh token request
    /// </summary>
    public class RefreshTokenRequest
    {
        [Required]
        public string RefreshToken { get; set; }
    }

    // ============================================
    // PASSWORD DTOs
    // ============================================

    /// <summary>
    /// Forgot password request - sends reset email
    /// </summary>
    public class ForgotPasswordRequest
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; }
    }

    /// <summary>
    /// Reset password with token
    /// </summary>
    public class ResetPasswordRequest
    {
        [Required]
        public string Token { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "New password is required")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
        [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
            ErrorMessage = "Password must contain at least one letter and one number")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm your password")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; }
    }

    /// <summary>
    /// Change password (for logged-in users)
    /// </summary>
    public class ChangePasswordRequest
    {
        [Required(ErrorMessage = "Current password is required")]
        public string CurrentPassword { get; set; }

        [Required(ErrorMessage = "New password is required")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
        [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
            ErrorMessage = "Password must contain at least one letter and one number")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm your password")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; }
    }

    // ============================================
    // USER INFO DTOs
    // ============================================

    /// <summary>
    /// User information returned after authentication
    /// </summary>
    public class UserInfoDto
    {
        public int UserId { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public int RoleId { get; set; }
        public string RoleName { get; set; }
        public int? CompanyId { get; set; }
        public string CompanyName { get; set; }
        public string CompanyDomain { get; set; }
        public bool EmailVerified { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public DateTime? LastLogin { get; set; }
        public PlanInfoDto Plan { get; set; }
        public CompanyUsageDto Usage { get; set; }
    }

    /// <summary>
    /// Plan information
    /// </summary>
    public class PlanInfoDto
    {
        public int PlanId { get; set; }
        public string PlanName { get; set; }
        public string PlanCode { get; set; }
        public int MaxAdmins { get; set; }
        public int MaxBusinessUsers { get; set; }
        public int MaxDatabases { get; set; }
        public int MaxDashboards { get; set; }
        public List<string> Features { get; set; } = new List<string>();
    }

    /// <summary>
    /// Company usage statistics
    /// </summary>
    public class CompanyUsageDto
    {
        public int CurrentAdmins { get; set; }
        public int CurrentBusinessUsers { get; set; }
        public int CurrentDatabases { get; set; }
        public int CurrentDashboards { get; set; }

        // Calculated properties
        public bool CanAddAdmin { get; set; }
        public bool CanAddBusinessUser { get; set; }
        public bool CanAddDatabase { get; set; }
        public bool CanAddDashboard { get; set; }
    }

    // ============================================
    // EMAIL VERIFICATION DTOs
    // ============================================

    /// <summary>
    /// Resend verification code request
    /// </summary>
    public class ResendCodeRequest
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress]
        public string Email { get; set; }

        /// <summary>
        /// Purpose of the code: SIGNUP, PASSWORD_RESET, 2FA
        /// </summary>
        public string Purpose { get; set; } = "SIGNUP";
    }

    // ============================================
    // VALIDATION DTOs
    // ============================================

    /// <summary>
    /// Email validation result
    /// </summary>
    public class EmailValidationResult
    {
        public bool IsValid { get; set; }
        public bool IsWorkEmail { get; set; }
        public bool IsAlreadyRegistered { get; set; }
        public string Domain { get; set; }
        public string Message { get; set; }
        public Company ExistingCompany { get; set; }
    }

    /// <summary>
    /// Generic API response
    /// </summary>
    public class ApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public static ApiResponse Ok(string message = "Success")
        {
            return new ApiResponse { Success = true, Message = message };
        }

        public static ApiResponse Fail(string message, List<string> errors = null)
        {
            return new ApiResponse { Success = false, Message = message, Errors = errors ?? new List<string>() };
        }
    }

    /// <summary>
    /// Generic API response with data
    /// </summary>
    public class ApiResponse<T> : ApiResponse
    {
        public T Data { get; set; }

        public static ApiResponse<T> Ok(T data, string message = "Success")
        {
            return new ApiResponse<T> { Success = true, Message = message, Data = data };
        }

        public new static ApiResponse<T> Fail(string message, List<string> errors = null)
        {
            return new ApiResponse<T> { Success = false, Message = message, Errors = errors ?? new List<string>() };
        }
    }

    // ============================================
    // USER MANAGEMENT DTOs (Admin)
    // ============================================

    /// <summary>
    /// Invite new user request (Admin function)
    /// </summary>
    public class InviteUserRequest
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        [StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, MinimumLength = 6)]
        [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
            ErrorMessage = "Password must contain at least one letter and one number")]
        public string Password { get; set; }

        /// <summary>
        /// Role for the invited user (default: BusinessUser)
        /// </summary>
        public int RoleId { get; set; } = 2; // BusinessUser

        /// <summary>
        /// Whether user must change password on first login
        /// </summary>
        public bool ForcePasswordChange { get; set; } = false;

        /// <summary>
        /// Dashboard IDs to grant access to
        /// </summary>
        public List<int> DashboardIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// User list item for admin view
    /// </summary>
    public class UserListItemDto
    {
        public int UserId { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public string RoleName { get; set; }
        public bool IsActive { get; set; }
        public bool EmailVerified { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime CreatedAt { get; set; }
        public int DashboardCount { get; set; }
    }
}