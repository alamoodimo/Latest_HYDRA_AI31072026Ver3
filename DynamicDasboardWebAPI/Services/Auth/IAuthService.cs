using System.Threading.Tasks;
using DynamicDashboardCommon.DTOs;
using DynamicDashboardCommon.Models;

namespace DynamicDasboardWebAPI.Services.Auth
{
    /// <summary>
    /// Service interface for authentication operations.
    /// </summary>
    public interface IAuthService
    {
        // ============================================
        // SIGNUP
        // ============================================

        /// <summary>
        /// Validates email for signup (work email, not already registered)
        /// </summary>
        Task<EmailValidationResult> ValidateEmailForSignupAsync(string email);

        /// <summary>
        /// Initiates signup process - creates pending user and sends verification code
        /// </summary>
        Task<ApiResponse> InitiateSignupAsync(SignupInitRequest request, string ipAddress = null);

        /// <summary>
        /// Verifies email with code and activates account
        /// </summary>
        Task<SignupResponse> VerifyEmailAndCompleteSignupAsync(VerifyEmailRequest request);

        /// <summary>
        /// Resends verification code
        /// </summary>
        Task<ApiResponse> ResendVerificationCodeAsync(ResendCodeRequest request, string ipAddress = null);

        // ============================================
        // LOGIN
        // ============================================

        /// <summary>
        /// Authenticates user with email and password
        /// </summary>
        Task<LoginResponse> LoginAsync(LoginRequest request, string ipAddress = null, string deviceInfo = null);

        /// <summary>
        /// Completes 2FA verification
        /// </summary>
        Task<LoginResponse> VerifyTwoFactorAsync(TwoFactorRequest request, string ipAddress = null, string deviceInfo = null);

        /// <summary>
        /// Refreshes access token using refresh token
        /// </summary>
        Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request, string ipAddress = null);

        /// <summary>
        /// Logs out user and revokes refresh token
        /// </summary>
        Task<ApiResponse> LogoutAsync(int userId, string refreshToken = null);

        /// <summary>
        /// Logs out user from all devices
        /// </summary>
        Task<ApiResponse> LogoutAllDevicesAsync(int userId);

        // ============================================
        // PASSWORD MANAGEMENT
        // ============================================

        /// <summary>
        /// Initiates password reset - sends reset email
        /// </summary>
        Task<ApiResponse> ForgotPasswordAsync(ForgotPasswordRequest request, string ipAddress = null);

        /// <summary>
        /// Resets password using token
        /// </summary>
        Task<ApiResponse> ResetPasswordAsync(ResetPasswordRequest request);

        /// <summary>
        /// Changes password for logged-in user
        /// </summary>
        Task<ApiResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request);

        // ============================================
        // USER MANAGEMENT
        // ============================================

        /// <summary>
        /// Gets current user info
        /// </summary>
        Task<UserInfoDto> GetCurrentUserAsync(int userId);

        /// <summary>
        /// Invites a new business user (Admin function)
        /// </summary>
        Task<ApiResponse<UserInfoDto>> InviteUserAsync(int adminUserId, InviteUserRequest request);

        /// <summary>
        /// Gets users in company
        /// </summary>
        Task<ApiResponse<System.Collections.Generic.List<UserListItemDto>>> GetCompanyUsersAsync(int companyId);

        /// <summary>
        /// Deactivates a user
        /// </summary>
        Task<ApiResponse> DeactivateUserAsync(int adminUserId, int targetUserId);

        /// <summary>
        /// Reactivates a user
        /// </summary>
        Task<ApiResponse> ReactivateUserAsync(int adminUserId, int targetUserId);

        // ============================================
        // COMPANY & PLAN
        // ============================================

        /// <summary>
        /// Gets company usage statistics
        /// </summary>
        Task<CompanyUsageDto> GetCompanyUsageAsync(int companyId);

        /// <summary>
        /// Checks if company can add more of a resource type
        /// </summary>
        Task<bool> CanAddResourceAsync(int companyId, string resourceType);

        // ============================================
        // UTILITIES
        // ============================================

        /// <summary>
        /// Generates a 4-digit verification code
        /// </summary>
        string GenerateVerificationCode();

        /// <summary>
        /// Hashes a password using BCrypt
        /// </summary>
        string HashPassword(string password);

        /// <summary>
        /// Verifies a password against a hash
        /// </summary>
        bool VerifyPassword(string password, string hash);

        /// <summary>
        /// Validates password strength
        /// </summary>
        bool ValidatePasswordStrength(string password, out string errorMessage);
    }
}