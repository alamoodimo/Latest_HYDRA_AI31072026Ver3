using System.ComponentModel.DataAnnotations;

namespace DynamicDashboardCommon.Models
{
    /// <summary>
    /// JWT authentication configuration settings.
    /// Configure in appsettings.json under "JwtSettings" section.
    /// </summary>
    public class JwtSettings
    {
        /// <summary>
        /// Configuration section name
        /// </summary>
        public const string SectionName = "JwtSettings";

        /// <summary>
        /// Secret key for signing JWT tokens (minimum 32 characters)
        /// </summary>
        [Required]
        [MinLength(32, ErrorMessage = "Secret key must be at least 32 characters")]
        public string SecretKey { get; set; }

        /// <summary>
        /// Token issuer (typically your application URL or name)
        /// </summary>
        [Required]
        public string Issuer { get; set; } = "HydraAI";

        /// <summary>
        /// Token audience (typically your application URL)
        /// </summary>
        [Required]
        public string Audience { get; set; } = "HydraAI";

        /// <summary>
        /// Access token expiry in minutes (default: 60 minutes)
        /// </summary>
        [Range(5, 1440)]
        public int AccessTokenExpiryMinutes { get; set; } = 60;

        /// <summary>
        /// Refresh token expiry in days (default: 7 days)
        /// </summary>
        [Range(1, 90)]
        public int RefreshTokenExpiryDays { get; set; } = 7;

        /// <summary>
        /// Extended session duration in days when "Remember Me" is checked
        /// </summary>
        [Range(1, 90)]
        public int RememberMeExpiryDays { get; set; } = 30;

        /// <summary>
        /// Whether to validate token lifetime
        /// </summary>
        public bool ValidateLifetime { get; set; } = true;

        /// <summary>
        /// Whether to validate the issuer
        /// </summary>
        public bool ValidateIssuer { get; set; } = true;

        /// <summary>
        /// Whether to validate the audience
        /// </summary>
        public bool ValidateAudience { get; set; } = true;

        /// <summary>
        /// Clock skew tolerance in minutes (for time differences between servers)
        /// </summary>
        [Range(0, 10)]
        public int ClockSkewMinutes { get; set; } = 2;

        /// <summary>
        /// Whether to require HTTPS for token transmission
        /// </summary>
        public bool RequireHttps { get; set; } = true;
    }
}