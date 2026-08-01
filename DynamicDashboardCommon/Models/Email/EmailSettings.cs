using System.ComponentModel.DataAnnotations;

namespace DynamicDashboardCommon.Models
{
    /// <summary>
    /// Email service configuration settings.
    /// Configure in appsettings.json under "EmailSettings" section.
    /// </summary>
    public class EmailSettings
    {
        /// <summary>
        /// Configuration section name
        /// </summary>
        public const string SectionName = "EmailSettings";

        /// <summary>
        /// SMTP server host (e.g., smtp.gmail.com, smtp.sendgrid.net)
        /// </summary>
        [Required]
        public string SmtpHost { get; set; }

        /// <summary>
        /// SMTP server port (typically 587 for TLS, 465 for SSL, 25 for unencrypted)
        /// </summary>
        [Range(1, 65535)]
        public int SmtpPort { get; set; } = 587;

        /// <summary>
        /// SMTP username for authentication
        /// </summary>
        public string SmtpUsername { get; set; }

        /// <summary>
        /// SMTP password for authentication
        /// </summary>
        public string SmtpPassword { get; set; }

        /// <summary>
        /// Whether to use SSL/TLS for SMTP connection
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// Whether to use STARTTLS
        /// </summary>
        public bool UseStartTls { get; set; } = true;

        /// <summary>
        /// Sender email address (From address)
        /// </summary>
        [Required]
        [EmailAddress]
        public string FromEmail { get; set; }

        /// <summary>
        /// Sender display name
        /// </summary>
        public string FromName { get; set; } = "Hydra AI";

        /// <summary>
        /// Reply-to email address (optional)
        /// </summary>
        [EmailAddress]
        public string ReplyToEmail { get; set; }

        /// <summary>
        /// Base URL for the application (used in email links)
        /// </summary>
        [Required]
        public string ApplicationUrl { get; set; }

        /// <summary>
        /// Whether email sending is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Verification code expiry in minutes
        /// </summary>
        public int VerificationCodeExpiryMinutes { get; set; } = 15;

        /// <summary>
        /// Password reset token expiry in hours
        /// </summary>
        public int PasswordResetTokenExpiryHours { get; set; } = 24;

        /// <summary>
        /// Maximum emails per hour (rate limiting)
        /// </summary>
        public int MaxEmailsPerHour { get; set; } = 100;

        /// <summary>
        /// Whether to log email content (for debugging, disable in production)
        /// </summary>
        public bool LogEmailContent { get; set; } = false;

        /// <summary>
        /// Test mode - if true, emails are logged but not sent
        /// </summary>
        public bool TestMode { get; set; } = false;

        /// <summary>
        /// Test email recipient - all emails go to this address in test mode
        /// </summary>
        [EmailAddress]
        public string TestModeRecipient { get; set; }

        /// <summary>
        /// Company support email for footer
        /// </summary>
        [EmailAddress]
        public string SupportEmail { get; set; } = "support@hydra-ai.com";

        /// <summary>
        /// Path to logo image for emails (relative to wwwroot or absolute URL)
        /// </summary>
        public string LogoUrl { get; set; }

        /// <summary>
        /// Primary brand color for email templates
        /// </summary>
        public string PrimaryColor { get; set; } = "#0ea5e9";

        /// <summary>
        /// Secondary brand color for email templates
        /// </summary>
        public string SecondaryColor { get; set; } = "#38bdf8";
    }
}