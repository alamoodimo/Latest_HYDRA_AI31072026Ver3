using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DynamicDashboardCommon.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DynamicDasboardWebAPI.Services.Auth
{
    /// <summary>
    /// JWT service implementation for token generation and validation.
    /// </summary>
    public class JwtService : IJwtService
    {
        private readonly JwtSettings _settings;
        private readonly SymmetricSecurityKey _signingKey;
        private readonly TokenValidationParameters _validationParameters;
        private readonly JwtSecurityTokenHandler _tokenHandler;

        // Custom claim types
        public const string ClaimTypeUserId = "uid";
        public const string ClaimTypeCompanyId = "cid";
        public const string ClaimTypeRole = "role";
        public const string ClaimTypeEmail = "email";
        public const string ClaimTypeFullName = "name";
        public const string ClaimTypePlanCode = "plan";
        public const string ClaimTypeTwoFactorPending = "2fa_pending";

        public JwtService(IOptions<JwtSettings> settings)
        {
            _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));

            if (string.IsNullOrEmpty(_settings.SecretKey) || _settings.SecretKey.Length < 32)
            {
                throw new InvalidOperationException("JWT SecretKey must be at least 32 characters");
            }

            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
            _tokenHandler = new JwtSecurityTokenHandler();

            _validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = _settings.ValidateIssuer,
                ValidateAudience = _settings.ValidateAudience,
                ValidateLifetime = _settings.ValidateLifetime,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _settings.Issuer,
                ValidAudience = _settings.Audience,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.FromMinutes(_settings.ClockSkewMinutes)
            };
        }

        #region Token Generation

        public string GenerateAccessToken(User user, Dictionary<string, string> additionalClaims = null)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserID.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(ClaimTypeUserId, user.UserID.ToString()),
                new Claim(ClaimTypeEmail, user.Email ?? ""),
                new Claim(ClaimTypeFullName, user.FullName ?? user.Username ?? ""),
                new Claim(ClaimTypeRole, GetRoleName(user.RoleID)),
                new Claim(ClaimTypes.Role, GetRoleName(user.RoleID)) // Standard role claim for [Authorize(Roles = "...")]
            };

            // Add company ID if user belongs to a company
            if (user.CompanyID.HasValue)
            {
                claims.Add(new Claim(ClaimTypeCompanyId, user.CompanyID.Value.ToString()));
            }

            // Add plan code if available
            if (user.Company?.Plan != null)
            {
                claims.Add(new Claim(ClaimTypePlanCode, user.Company.Plan.PlanCode));
            }

            // Add any additional claims
            if (additionalClaims != null)
            {
                foreach (var claim in additionalClaims)
                {
                    claims.Add(new Claim(claim.Key, claim.Value));
                }
            }

            var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
            var expiry = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiry,
                signingCredentials: credentials
            );

            return _tokenHandler.WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        public string GenerateTwoFactorToken(int userId)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypeUserId, userId.ToString()),
                new Claim(ClaimTypeTwoFactorPending, "true")
            };

            var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
            var expiry = DateTime.UtcNow.AddMinutes(10); // 2FA token valid for 10 minutes

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiry,
                signingCredentials: credentials
            );

            return _tokenHandler.WriteToken(token);
        }

        #endregion

        #region Token Validation

        public ClaimsPrincipal ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            try
            {
                var principal = _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);

                if (validatedToken is JwtSecurityToken jwtToken)
                {
                    // Verify algorithm to prevent algorithm confusion attacks
                    if (!jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return null;
                    }
                }

                return principal;
            }
            catch (SecurityTokenExpiredException)
            {
                // Token is expired
                Console.WriteLine("[JwtService] Token is expired");
                return null;
            }
            catch (SecurityTokenException ex)
            {
                // Token validation failed
                Console.WriteLine($"[JwtService] Token validation failed: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JwtService] Unexpected error validating token: {ex.Message}");
                return null;
            }
        }

        public ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            try
            {
                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuer = _settings.ValidateIssuer,
                    ValidateAudience = _settings.ValidateAudience,
                    ValidateLifetime = false, // Don't validate lifetime for expired tokens
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _settings.Issuer,
                    ValidAudience = _settings.Audience,
                    IssuerSigningKey = _signingKey
                };

                var principal = _tokenHandler.ValidateToken(token, validationParams, out var validatedToken);

                if (validatedToken is JwtSecurityToken jwtToken)
                {
                    if (!jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return null;
                    }
                }

                return principal;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JwtService] Error getting principal from expired token: {ex.Message}");
                return null;
            }
        }

        public int? ValidateTwoFactorToken(string token)
        {
            var principal = ValidateToken(token);
            if (principal == null)
                return null;

            // Check if this is a 2FA pending token
            var twoFactorClaim = principal.FindFirst(ClaimTypeTwoFactorPending);
            if (twoFactorClaim?.Value != "true")
                return null;

            return GetUserIdFromPrincipal(principal);
        }

        #endregion

        #region Token Information Extraction

        public int? GetUserIdFromToken(string token)
        {
            var principal = GetPrincipalFromExpiredToken(token);
            return GetUserIdFromPrincipal(principal);
        }

        public int? GetCompanyIdFromToken(string token)
        {
            var principal = GetPrincipalFromExpiredToken(token);
            if (principal == null)
                return null;

            var claim = principal.FindFirst(ClaimTypeCompanyId);
            if (claim != null && int.TryParse(claim.Value, out var companyId))
            {
                return companyId;
            }

            return null;
        }

        public string GetRoleFromToken(string token)
        {
            var principal = GetPrincipalFromExpiredToken(token);
            if (principal == null)
                return null;

            return principal.FindFirst(ClaimTypeRole)?.Value ??
                   principal.FindFirst(ClaimTypes.Role)?.Value;
        }

        public bool IsTokenExpired(string token)
        {
            var expiration = GetTokenExpiration(token);
            if (!expiration.HasValue)
                return true;

            return expiration.Value < DateTime.UtcNow;
        }

        public DateTime? GetTokenExpiration(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            try
            {
                var jwtToken = _tokenHandler.ReadJwtToken(token);
                return jwtToken.ValidTo;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Helper Methods

        private int? GetUserIdFromPrincipal(ClaimsPrincipal principal)
        {
            if (principal == null)
                return null;

            var userIdClaim = principal.FindFirst(ClaimTypeUserId) ??
                             principal.FindFirst(JwtRegisteredClaimNames.Sub) ??
                             principal.FindFirst(ClaimTypes.NameIdentifier);

            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
            {
                return userId;
            }

            return null;
        }

        private string GetRoleName(int roleId)
        {
            return roleId switch
            {
                1 => "Admin",
                2 => "BusinessUser",
                3 => "SuperAdmin",
                _ => "User"
            };
        }

        #endregion
    }
}