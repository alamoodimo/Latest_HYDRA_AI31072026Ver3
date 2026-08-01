using System;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using DynamicDashboardCommon.Models;
using Microsoft.Extensions.Options;

namespace DynamicDasboardWebAPI.Services.Email
{
    /// <summary>
    /// Email service implementation using SMTP.
    /// Includes beautiful HTML email templates with Hydra AI branding.
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        }

        #region Public Methods

        public async Task<bool> SendVerificationCodeAsync(string email, string fullName, string code)
        {
            var subject = $"Your Hydra AI Verification Code: {code}";
            var htmlBody = BuildVerificationCodeEmail(fullName, code);
            var plainText = $"Hi {fullName},\n\nYour verification code is: {code}\n\nThis code expires in {_settings.VerificationCodeExpiryMinutes} minutes.\n\nIf you didn't request this code, please ignore this email.\n\n- The Hydra AI Team";

            return await SendEmailAsync(email, subject, htmlBody, plainText);
        }

        public async Task<bool> SendWelcomeEmailAsync(string email, string fullName, string companyName)
        {
            var subject = $"Welcome to Hydra AI, {fullName}! 🚀";
            var htmlBody = BuildWelcomeEmail(fullName, companyName);
            var plainText = $"Hi {fullName},\n\nWelcome to Hydra AI! Your account for {companyName} has been created.\n\nGet started by connecting your first data source and creating your first dashboard.\n\nLogin at: {_settings.ApplicationUrl}\n\n- The Hydra AI Team";

            return await SendEmailAsync(email, subject, htmlBody, plainText);
        }

        public async Task<bool> SendBusinessUserInvitationAsync(
            string email,
            string fullName,
            string password,
            string companyName,
            string invitedByName,
            bool forcePasswordChange = false)
        {
            var subject = $"You've been invited to join {companyName} on Hydra AI";
            var htmlBody = BuildBusinessUserInvitationEmail(fullName, password, companyName, invitedByName, forcePasswordChange);
            var plainText = $"Hi {fullName},\n\n{invitedByName} has invited you to join {companyName} on Hydra AI.\n\nYour login credentials:\nEmail: {email}\nPassword: {password}\n\nLogin at: {_settings.ApplicationUrl}\n\n{(forcePasswordChange ? "You will be asked to change your password on first login.\n\n" : "")}- The Hydra AI Team";

            return await SendEmailAsync(email, subject, htmlBody, plainText);
        }

        public async Task<bool> SendPasswordResetEmailAsync(string email, string fullName, string resetToken)
        {
            var resetUrl = $"{_settings.ApplicationUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(email)}";
            var subject = "Reset Your Hydra AI Password";
            var htmlBody = BuildPasswordResetEmail(fullName, resetUrl);
            var plainText = $"Hi {fullName},\n\nWe received a request to reset your password. Click the link below to create a new password:\n\n{resetUrl}\n\nThis link expires in {_settings.PasswordResetTokenExpiryHours} hours.\n\nIf you didn't request this, please ignore this email.\n\n- The Hydra AI Team";

            return await SendEmailAsync(email, subject, htmlBody, plainText);
        }

        public async Task<bool> SendPasswordChangedNotificationAsync(string email, string fullName)
        {
            var subject = "Your Hydra AI Password Has Been Changed";
            var htmlBody = BuildPasswordChangedEmail(fullName);
            var plainText = $"Hi {fullName},\n\nYour Hydra AI password has been successfully changed.\n\nIf you didn't make this change, please contact us immediately at {_settings.SupportEmail}.\n\n- The Hydra AI Team";

            return await SendEmailAsync(email, subject, htmlBody, plainText);
        }

        public async Task<bool> SendAccountLockedNotificationAsync(string email, string fullName, DateTime lockedUntil)
        {
            var subject = "Hydra AI Account Temporarily Locked";
            var htmlBody = BuildAccountLockedEmail(fullName, lockedUntil);
            var plainText = $"Hi {fullName},\n\nYour Hydra AI account has been temporarily locked due to multiple failed login attempts.\n\nYour account will be automatically unlocked on {lockedUntil:MMMM dd, yyyy 'at' hh:mm tt} UTC.\n\nIf this wasn't you, please reset your password immediately.\n\n- The Hydra AI Team";

            return await SendEmailAsync(email, subject, htmlBody, plainText);
        }

        public async Task<bool> SendTwoFactorCodeAsync(string email, string fullName, string code)
        {
            var subject = $"Your Hydra AI Security Code: {code}";
            var htmlBody = BuildTwoFactorCodeEmail(fullName, code);
            var plainText = $"Hi {fullName},\n\nYour security code is: {code}\n\nThis code expires in 10 minutes.\n\nIf you didn't request this code, please secure your account immediately.\n\n- The Hydra AI Team";

            return await SendEmailAsync(email, subject, htmlBody, plainText);
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string htmlBody, string plainTextBody = null)
        {
            if (!_settings.IsEnabled)
            {
                Console.WriteLine($"[EmailService] Email disabled. Would send to: {to}, Subject: {subject}");
                return true;
            }

            // Test mode - redirect all emails
            if (_settings.TestMode && !string.IsNullOrEmpty(_settings.TestModeRecipient))
            {
                subject = $"[TEST - Original: {to}] {subject}";
                to = _settings.TestModeRecipient;
            }

            try
            {
                using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                {
                    EnableSsl = _settings.UseSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000 // 30 seconds
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(_settings.FromEmail, _settings.FromName),
                    Subject = subject,
                    IsBodyHtml = true,
                    Body = htmlBody,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };

                message.To.Add(to);

                // Add reply-to if configured
                if (!string.IsNullOrEmpty(_settings.ReplyToEmail))
                {
                    message.ReplyToList.Add(_settings.ReplyToEmail);
                }

                // Add plain text alternative
                if (!string.IsNullOrEmpty(plainTextBody))
                {
                    var plainView = AlternateView.CreateAlternateViewFromString(plainTextBody, Encoding.UTF8, "text/plain");
                    message.AlternateViews.Add(plainView);
                }

                if (_settings.LogEmailContent)
                {
                    Console.WriteLine($"[EmailService] Sending email to: {to}");
                    Console.WriteLine($"[EmailService] Subject: {subject}");
                }

                await client.SendMailAsync(message);

                Console.WriteLine($"[EmailService] ✅ Email sent successfully to: {to}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] ❌ Failed to send email to {to}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ValidateSettingsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.SmtpHost) ||
                    string.IsNullOrEmpty(_settings.FromEmail))
                {
                    return false;
                }

                using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                {
                    EnableSsl = _settings.UseSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword),
                    Timeout = 10000
                };

                // Just create the client to validate - actual connection test would require sending
                await Task.CompletedTask;
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Email Templates

        private string BuildBaseTemplate(string title, string content, string preheader = "")
        {
            var logoUrl = !string.IsNullOrEmpty(_settings.LogoUrl)
                ? _settings.LogoUrl
                : $"{_settings.ApplicationUrl}/images/logo3.jpg";

            return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <title>{title}</title>
    <!--[if mso]>
    <noscript>
        <xml>
            <o:OfficeDocumentSettings>
                <o:PixelsPerInch>96</o:PixelsPerInch>
            </o:OfficeDocumentSettings>
        </xml>
    </noscript>
    <![endif]-->
    <style>
        /* Reset */
        body, table, td, a {{ -webkit-text-size-adjust: 100%; -ms-text-size-adjust: 100%; }}
        table, td {{ mso-table-lspace: 0pt; mso-table-rspace: 0pt; }}
        img {{ -ms-interpolation-mode: bicubic; border: 0; height: auto; line-height: 100%; outline: none; text-decoration: none; }}
        body {{ margin: 0 !important; padding: 0 !important; width: 100% !important; }}
        
        /* Responsive */
        @media screen and (max-width: 600px) {{
            .container {{ width: 100% !important; padding: 10px !important; }}
            .content {{ padding: 20px !important; }}
            .code-box {{ font-size: 28px !important; letter-spacing: 6px !important; }}
        }}
    </style>
</head>
<body style=""margin: 0; padding: 0; background-color: #f4f7fa; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;"">
    <!-- Preheader text -->
    <div style=""display: none; max-height: 0; overflow: hidden; font-size: 1px; line-height: 1px; color: #f4f7fa;"">{preheader}</div>
    
    <!-- Main container -->
    <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color: #f4f7fa;"">
        <tr>
            <td align=""center"" style=""padding: 40px 20px;"">
                <!-- Email container -->
                <table role=""presentation"" class=""container"" width=""600"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color: #ffffff; border-radius: 16px; box-shadow: 0 4px 24px rgba(0, 0, 0, 0.08); overflow: hidden;"">
                    
                    <!-- Header with gradient -->
                    <tr>
                        <td style=""background: linear-gradient(135deg, {_settings.SecondaryColor} 0%, {_settings.PrimaryColor} 100%); padding: 32px 40px; text-align: center;"">
                            <img src=""{logoUrl}"" alt=""Hydra AI"" width=""120"" style=""display: block; margin: 0 auto; max-width: 120px; height: auto;"">
                        </td>
                    </tr>
                    
                    <!-- Content -->
                    <tr>
                        <td class=""content"" style=""padding: 40px;"">
                            {content}
                        </td>
                    </tr>
                    
                    <!-- Footer -->
                    <tr>
                        <td style=""background-color: #f8fafc; padding: 24px 40px; border-top: 1px solid #e2e8f0;"">
                            <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"">
                                <tr>
                                    <td style=""text-align: center;"">
                                        <p style=""margin: 0 0 8px 0; font-size: 14px; color: #64748b;"">
                                            <strong style=""color: {_settings.PrimaryColor};"">Hydra AI</strong> - AI-Powered Business Intelligence
                                        </p>
                                        <p style=""margin: 0; font-size: 12px; color: #94a3b8;"">
                                            Need help? Contact us at <a href=""mailto:{_settings.SupportEmail}"" style=""color: {_settings.PrimaryColor}; text-decoration: none;"">{_settings.SupportEmail}</a>
                                        </p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    
                </table>
                
                <!-- Legal footer -->
                <table role=""presentation"" width=""600"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""margin-top: 24px;"">
                    <tr>
                        <td style=""text-align: center; font-size: 11px; color: #94a3b8; line-height: 1.6;"">
                            This email was sent by Hydra AI.<br>
                            © {DateTime.UtcNow.Year} Hydra AI. All rights reserved.
                        </td>
                    </tr>
                </table>
                
            </td>
        </tr>
    </table>
</body>
</html>";
        }

        private string BuildVerificationCodeEmail(string fullName, string code)
        {
            var content = $@"
                <h1 style=""margin: 0 0 24px 0; font-size: 24px; font-weight: 600; color: #1e293b; text-align: center;"">
                    Verify Your Email
                </h1>
                
                <p style=""margin: 0 0 24px 0; font-size: 16px; color: #475569; line-height: 1.6; text-align: center;"">
                    Hi {fullName},<br><br>
                    Thank you for signing up for Hydra AI! Please enter the verification code below to complete your registration.
                </p>
                
                <!-- Code box -->
                <div style=""text-align: center; margin: 32px 0;"">
                    <div class=""code-box"" style=""display: inline-block; background: linear-gradient(135deg, #f0f9ff 0%, #e0f2fe 100%); border: 2px solid {_settings.PrimaryColor}; border-radius: 12px; padding: 20px 40px; font-size: 36px; font-weight: 700; letter-spacing: 10px; color: {_settings.PrimaryColor}; font-family: 'Monaco', 'Consolas', monospace;"">
                        {code}
                    </div>
                </div>
                
                <p style=""margin: 24px 0 0 0; font-size: 14px; color: #64748b; text-align: center; line-height: 1.6;"">
                    This code expires in <strong>{_settings.VerificationCodeExpiryMinutes} minutes</strong>.<br>
                    If you didn't request this code, please ignore this email.
                </p>";

            return BuildBaseTemplate("Verify Your Email - Hydra AI", content, $"Your verification code is {code}");
        }

        private string BuildWelcomeEmail(string fullName, string companyName)
        {
            var loginUrl = $"{_settings.ApplicationUrl}/login";

            var content = $@"
                <h1 style=""margin: 0 0 24px 0; font-size: 24px; font-weight: 600; color: #1e293b; text-align: center;"">
                    Welcome to Hydra AI! 🚀
                </h1>
                
                <p style=""margin: 0 0 24px 0; font-size: 16px; color: #475569; line-height: 1.6; text-align: center;"">
                    Hi {fullName},<br><br>
                    Your account for <strong>{companyName}</strong> has been created successfully. You're all set to start building powerful, AI-driven dashboards!
                </p>
                
                <!-- Getting started steps -->
                <div style=""background: #f8fafc; border-radius: 12px; padding: 24px; margin: 24px 0;"">
                    <h3 style=""margin: 0 0 16px 0; font-size: 16px; font-weight: 600; color: #1e293b;"">
                        🎯 Get Started in 3 Simple Steps:
                    </h3>
                    <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"">
                        <tr>
                            <td style=""padding: 8px 0;"">
                                <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0"">
                                    <tr>
                                        <td style=""width: 32px; height: 32px; background: {_settings.PrimaryColor}; border-radius: 50%; text-align: center; vertical-align: middle; color: white; font-weight: 600; font-size: 14px;"">1</td>
                                        <td style=""padding-left: 12px; font-size: 14px; color: #475569;"">Connect your data source (SQL Server, MySQL, PostgreSQL, etc.)</td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                        <tr>
                            <td style=""padding: 8px 0;"">
                                <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0"">
                                    <tr>
                                        <td style=""width: 32px; height: 32px; background: {_settings.PrimaryColor}; border-radius: 50%; text-align: center; vertical-align: middle; color: white; font-weight: 600; font-size: 14px;"">2</td>
                                        <td style=""padding-left: 12px; font-size: 14px; color: #475569;"">Let our AI analyze your schema and suggest insights</td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                        <tr>
                            <td style=""padding: 8px 0;"">
                                <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0"">
                                    <tr>
                                        <td style=""width: 32px; height: 32px; background: {_settings.PrimaryColor}; border-radius: 50%; text-align: center; vertical-align: middle; color: white; font-weight: 600; font-size: 14px;"">3</td>
                                        <td style=""padding-left: 12px; font-size: 14px; color: #475569;"">Create beautiful dashboards with drag-and-drop simplicity</td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </div>
                
                <!-- CTA Button -->
                <div style=""text-align: center; margin: 32px 0;"">
                    <a href=""{loginUrl}"" style=""display: inline-block; background: linear-gradient(135deg, {_settings.SecondaryColor} 0%, {_settings.PrimaryColor} 100%); color: white; text-decoration: none; padding: 14px 32px; border-radius: 8px; font-weight: 600; font-size: 16px; box-shadow: 0 4px 12px rgba(14, 165, 233, 0.3);"">
                        Go to Dashboard →
                    </a>
                </div>
                
                <p style=""margin: 24px 0 0 0; font-size: 14px; color: #64748b; text-align: center;"">
                    Questions? We're here to help at <a href=""mailto:{_settings.SupportEmail}"" style=""color: {_settings.PrimaryColor};"">{_settings.SupportEmail}</a>
                </p>";

            return BuildBaseTemplate("Welcome to Hydra AI!", content, $"Welcome {fullName}! Your Hydra AI account is ready.");
        }

        private string BuildBusinessUserInvitationEmail(string fullName, string password, string companyName, string invitedByName, bool forcePasswordChange)
        {
            var loginUrl = $"{_settings.ApplicationUrl}/login";

            var content = $@"
                <h1 style=""margin: 0 0 24px 0; font-size: 24px; font-weight: 600; color: #1e293b; text-align: center;"">
                    You're Invited! 🎉
                </h1>
                
                <p style=""margin: 0 0 24px 0; font-size: 16px; color: #475569; line-height: 1.6; text-align: center;"">
                    Hi {fullName},<br><br>
                    <strong>{invitedByName}</strong> has invited you to join <strong>{companyName}</strong> on Hydra AI - an AI-powered business intelligence platform.
                </p>
                
                <!-- Credentials box -->
                <div style=""background: linear-gradient(135deg, #f0f9ff 0%, #e0f2fe 100%); border: 2px solid {_settings.PrimaryColor}; border-radius: 12px; padding: 24px; margin: 24px 0;"">
                    <h3 style=""margin: 0 0 16px 0; font-size: 16px; font-weight: 600; color: #1e293b; text-align: center;"">
                        🔐 Your Login Credentials
                    </h3>
                    <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"">
                        <tr>
                            <td style=""padding: 8px 0; border-bottom: 1px solid #e0f2fe;"">
                                <span style=""font-size: 13px; color: #64748b;"">Email:</span><br>
                                <span style=""font-size: 15px; font-weight: 600; color: #1e293b;"">{fullName}</span>
                            </td>
                        </tr>
                        <tr>
                            <td style=""padding: 12px 0 8px 0;"">
                                <span style=""font-size: 13px; color: #64748b;"">Password:</span><br>
                                <span style=""font-size: 18px; font-weight: 700; color: {_settings.PrimaryColor}; font-family: 'Monaco', 'Consolas', monospace; letter-spacing: 1px;"">{password}</span>
                            </td>
                        </tr>
                    </table>
                </div>
                
                {(forcePasswordChange ? @"
                <div style=""background: #fef3c7; border-radius: 8px; padding: 12px 16px; margin: 16px 0; text-align: center;"">
                    <span style=""color: #92400e; font-size: 14px;"">⚠️ You will be asked to change your password on first login.</span>
                </div>" : "")}
                
                <!-- CTA Button -->
                <div style=""text-align: center; margin: 32px 0;"">
                    <a href=""{loginUrl}"" style=""display: inline-block; background: linear-gradient(135deg, {_settings.SecondaryColor} 0%, {_settings.PrimaryColor} 100%); color: white; text-decoration: none; padding: 14px 32px; border-radius: 8px; font-weight: 600; font-size: 16px; box-shadow: 0 4px 12px rgba(14, 165, 233, 0.3);"">
                        Login to Hydra AI →
                    </a>
                </div>
                
                <p style=""margin: 24px 0 0 0; font-size: 13px; color: #94a3b8; text-align: center; line-height: 1.6;"">
                    For security, please keep your credentials safe and don't share them with anyone.<br>
                    If you didn't expect this invitation, please contact your administrator.
                </p>";

            return BuildBaseTemplate($"You're Invited to {companyName} on Hydra AI", content, $"{invitedByName} invited you to join {companyName} on Hydra AI");
        }

        private string BuildPasswordResetEmail(string fullName, string resetUrl)
        {
            var content = $@"
                <h1 style=""margin: 0 0 24px 0; font-size: 24px; font-weight: 600; color: #1e293b; text-align: center;"">
                    Reset Your Password
                </h1>
                
                <p style=""margin: 0 0 24px 0; font-size: 16px; color: #475569; line-height: 1.6; text-align: center;"">
                    Hi {fullName},<br><br>
                    We received a request to reset your password. Click the button below to create a new password.
                </p>
                
                <!-- CTA Button -->
                <div style=""text-align: center; margin: 32px 0;"">
                    <a href=""{resetUrl}"" style=""display: inline-block; background: linear-gradient(135deg, {_settings.SecondaryColor} 0%, {_settings.PrimaryColor} 100%); color: white; text-decoration: none; padding: 14px 32px; border-radius: 8px; font-weight: 600; font-size: 16px; box-shadow: 0 4px 12px rgba(14, 165, 233, 0.3);"">
                        Reset Password →
                    </a>
                </div>
                
                <div style=""background: #f8fafc; border-radius: 8px; padding: 16px; margin: 24px 0; text-align: center;"">
                    <p style=""margin: 0; font-size: 13px; color: #64748b; line-height: 1.6;"">
                        This link expires in <strong>{_settings.PasswordResetTokenExpiryHours} hours</strong>.<br>
                        If you can't click the button, copy and paste this URL into your browser:<br>
                        <span style=""color: {_settings.PrimaryColor}; word-break: break-all; font-size: 12px;"">{resetUrl}</span>
                    </p>
                </div>
                
                <p style=""margin: 24px 0 0 0; font-size: 14px; color: #94a3b8; text-align: center;"">
                    If you didn't request this password reset, please ignore this email.<br>
                    Your password will remain unchanged.
                </p>";

            return BuildBaseTemplate("Reset Your Password - Hydra AI", content, "Reset your Hydra AI password");
        }

        private string BuildPasswordChangedEmail(string fullName)
        {
            var content = $@"
                <h1 style=""margin: 0 0 24px 0; font-size: 24px; font-weight: 600; color: #1e293b; text-align: center;"">
                    Password Changed ✓
                </h1>
                
                <p style=""margin: 0 0 24px 0; font-size: 16px; color: #475569; line-height: 1.6; text-align: center;"">
                    Hi {fullName},<br><br>
                    Your Hydra AI password has been successfully changed.
                </p>
                
                <div style=""background: #f0fdf4; border: 1px solid #86efac; border-radius: 8px; padding: 16px; margin: 24px 0; text-align: center;"">
                    <span style=""color: #166534; font-size: 14px;"">✅ Your account is secure with the new password.</span>
                </div>
                
                <p style=""margin: 24px 0 0 0; font-size: 14px; color: #ef4444; text-align: center; line-height: 1.6;"">
                    <strong>⚠️ Didn't make this change?</strong><br>
                    If you didn't change your password, please contact us immediately at<br>
                    <a href=""mailto:{_settings.SupportEmail}"" style=""color: {_settings.PrimaryColor};"">{_settings.SupportEmail}</a>
                </p>";

            return BuildBaseTemplate("Password Changed - Hydra AI", content, "Your Hydra AI password has been changed");
        }

        private string BuildAccountLockedEmail(string fullName, DateTime lockedUntil)
        {
            var resetUrl = $"{_settings.ApplicationUrl}/forgot-password";

            var content = $@"
                <h1 style=""margin: 0 0 24px 0; font-size: 24px; font-weight: 600; color: #1e293b; text-align: center;"">
                    Account Temporarily Locked
                </h1>
                
                <p style=""margin: 0 0 24px 0; font-size: 16px; color: #475569; line-height: 1.6; text-align: center;"">
                    Hi {fullName},<br><br>
                    Your Hydra AI account has been temporarily locked due to multiple failed login attempts.
                </p>
                
                <div style=""background: #fef2f2; border: 1px solid #fecaca; border-radius: 8px; padding: 16px; margin: 24px 0; text-align: center;"">
                    <span style=""color: #991b1b; font-size: 14px;"">
                        🔒 Your account will be automatically unlocked on<br>
                        <strong>{lockedUntil:MMMM dd, yyyy 'at' hh:mm tt} UTC</strong>
                    </span>
                </div>
                
                <p style=""margin: 0 0 24px 0; font-size: 14px; color: #475569; text-align: center; line-height: 1.6;"">
                    If this wasn't you, we recommend resetting your password immediately to secure your account.
                </p>
                
                <!-- CTA Button -->
                <div style=""text-align: center; margin: 32px 0;"">
                    <a href=""{resetUrl}"" style=""display: inline-block; background: linear-gradient(135deg, #f87171 0%, #ef4444 100%); color: white; text-decoration: none; padding: 14px 32px; border-radius: 8px; font-weight: 600; font-size: 16px;"">
                        Reset Password Now →
                    </a>
                </div>";

            return BuildBaseTemplate("Account Locked - Hydra AI", content, "Your Hydra AI account has been temporarily locked");
        }

        private string BuildTwoFactorCodeEmail(string fullName, string code)
        {
            var content = $@"
                <h1 style=""margin: 0 0 24px 0; font-size: 24px; font-weight: 600; color: #1e293b; text-align: center;"">
                    Security Verification
                </h1>
                
                <p style=""margin: 0 0 24px 0; font-size: 16px; color: #475569; line-height: 1.6; text-align: center;"">
                    Hi {fullName},<br><br>
                    Please enter the security code below to complete your login.
                </p>
                
                <!-- Code box -->
                <div style=""text-align: center; margin: 32px 0;"">
                    <div class=""code-box"" style=""display: inline-block; background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%); border: 2px solid #f59e0b; border-radius: 12px; padding: 20px 40px; font-size: 36px; font-weight: 700; letter-spacing: 10px; color: #92400e; font-family: 'Monaco', 'Consolas', monospace;"">
                        {code}
                    </div>
                </div>
                
                <p style=""margin: 24px 0 0 0; font-size: 14px; color: #64748b; text-align: center; line-height: 1.6;"">
                    This code expires in <strong>10 minutes</strong>.<br>
                    If you didn't request this code, please secure your account immediately.
                </p>";

            return BuildBaseTemplate("Security Code - Hydra AI", content, $"Your security code is {code}");
        }

        #endregion
    }
}