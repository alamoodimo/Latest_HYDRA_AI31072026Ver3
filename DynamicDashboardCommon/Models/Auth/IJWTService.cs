using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using DynamicDashboardCommon.Models;

namespace DynamicDasboardWebAPI.Services.Auth
{
    /// <summary>
    /// Service interface for JWT token operations.
    /// </summary>
    public interface IJwtService
    {
        /// <summary>
        /// Generates an access token for a user
        /// </summary>
        /// <param name="user">The user to generate token for</param>
        /// <param name="additionalClaims">Optional additional claims to include</param>
        /// <returns>JWT access token string</returns>
        string GenerateAccessToken(User user, Dictionary<string, string> additionalClaims = null);

        /// <summary>
        /// Generates a refresh token
        /// </summary>
        /// <returns>Secure random refresh token string</returns>
        string GenerateRefreshToken();

        /// <summary>
        /// Validates an access token and returns the claims principal
        /// </summary>
        /// <param name="token">JWT token to validate</param>
        /// <returns>ClaimsPrincipal if valid, null otherwise</returns>
        ClaimsPrincipal ValidateToken(string token);

        /// <summary>
        /// Extracts claims from a token without full validation (for expired tokens)
        /// </summary>
        /// <param name="token">JWT token</param>
        /// <returns>ClaimsPrincipal or null</returns>
        ClaimsPrincipal GetPrincipalFromExpiredToken(string token);

        /// <summary>
        /// Gets the user ID from a token
        /// </summary>
        /// <param name="token">JWT token</param>
        /// <returns>User ID or null</returns>
        int? GetUserIdFromToken(string token);

        /// <summary>
        /// Gets the company ID from a token
        /// </summary>
        /// <param name="token">JWT token</param>
        /// <returns>Company ID or null</returns>
        int? GetCompanyIdFromToken(string token);

        /// <summary>
        /// Gets the role from a token
        /// </summary>
        /// <param name="token">JWT token</param>
        /// <returns>Role name or null</returns>
        string GetRoleFromToken(string token);

        /// <summary>
        /// Checks if a token is expired
        /// </summary>
        /// <param name="token">JWT token</param>
        /// <returns>True if expired</returns>
        bool IsTokenExpired(string token);

        /// <summary>
        /// Gets token expiration time
        /// </summary>
        /// <param name="token">JWT token</param>
        /// <returns>Expiration DateTime or null</returns>
        System.DateTime? GetTokenExpiration(string token);

        /// <summary>
        /// Generates a temporary token for 2FA flow
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Temporary token valid for short period</returns>
        string GenerateTwoFactorToken(int userId);

        /// <summary>
        /// Validates a 2FA temporary token
        /// </summary>
        /// <param name="token">2FA token</param>
        /// <returns>User ID if valid, null otherwise</returns>
        int? ValidateTwoFactorToken(string token);
    }
}