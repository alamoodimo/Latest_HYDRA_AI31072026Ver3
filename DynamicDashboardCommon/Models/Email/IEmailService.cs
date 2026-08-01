using System.Threading.Tasks;
using DynamicDashboardCommon.DTOs;
using DynamicDashboardCommon.Models;

namespace DynamicDasboardWebAPI.Services.Email
{
    /// <summary>
    /// Service interface for sending emails.
    /// </summary>
    public interface IEmailService
    {
        /// <summary>
        /// Sends a verification code email for signup
        /// </summary>
        /// <param name="email">Recipient email</param>
        /// <param name="fullName">Recipient name</param>
        /// <param name="code">4-digit verification code</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendVerificationCodeAsync(string email, string fullName, string code);

        /// <summary>
        /// Sends a welcome email after successful signup
        /// </summary>
        /// <param name="email">Recipient email</param>
        /// <param name="fullName">Recipient name</param>
        /// <param name="companyName">Company name</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendWelcomeEmailAsync(string email, string fullName, string companyName);

        /// <summary>
        /// Sends credentials to an invited business user
        /// </summary>
        /// <param name="email">Recipient email</param>
        /// <param name="fullName">Recipient name</param>
        /// <param name="password">Temporary password</param>
        /// <param name="companyName">Company name</param>
        /// <param name="invitedByName">Name of admin who invited</param>
        /// <param name="forcePasswordChange">Whether password change is required</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendBusinessUserInvitationAsync(
            string email,
            string fullName,
            string password,
            string companyName,
            string invitedByName,
            bool forcePasswordChange = false);

        /// <summary>
        /// Sends a password reset email with reset link
        /// </summary>
        /// <param name="email">Recipient email</param>
        /// <param name="fullName">Recipient name</param>
        /// <param name="resetToken">Password reset token</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendPasswordResetEmailAsync(string email, string fullName, string resetToken);

        /// <summary>
        /// Sends confirmation after password has been changed
        /// </summary>
        /// <param name="email">Recipient email</param>
        /// <param name="fullName">Recipient name</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendPasswordChangedNotificationAsync(string email, string fullName);

        /// <summary>
        /// Sends account locked notification
        /// </summary>
        /// <param name="email">Recipient email</param>
        /// <param name="fullName">Recipient name</param>
        /// <param name="lockedUntil">When the account will be unlocked</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendAccountLockedNotificationAsync(string email, string fullName, System.DateTime lockedUntil);

        /// <summary>
        /// Sends a 2FA code email
        /// </summary>
        /// <param name="email">Recipient email</param>
        /// <param name="fullName">Recipient name</param>
        /// <param name="code">4-digit 2FA code</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendTwoFactorCodeAsync(string email, string fullName, string code);

        /// <summary>
        /// Sends a generic email with custom content
        /// </summary>
        /// <param name="to">Recipient email</param>
        /// <param name="subject">Email subject</param>
        /// <param name="htmlBody">HTML email body</param>
        /// <param name="plainTextBody">Plain text fallback (optional)</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendEmailAsync(string to, string subject, string htmlBody, string plainTextBody = null);

        /// <summary>
        /// Validates email settings and connection
        /// </summary>
        /// <returns>True if settings are valid and SMTP is reachable</returns>
        Task<bool> ValidateSettingsAsync();
    }
}