using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DynamicDashboardCommon.DTOs;
using DynamicDashboardCommon.Models;
using DynamicDasboardWebAPI.Repositories;
using DynamicDasboardWebAPI.Services.Email;
using Microsoft.Extensions.Options;

namespace DynamicDasboardWebAPI.Services.Auth
{
    /// <summary>
    /// Authentication service implementation.
    /// Handles signup, login, password management, and user management.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly AuthRepository _repository;
        private readonly IJwtService _jwtService;
        private readonly IEmailService _emailService;
        private readonly EmailSettings _emailSettings;
        private readonly JwtSettings _jwtSettings;

        // Constants
        private const int MaxLoginAttempts = 5;
        private const int LockoutHours = 24;
        private const int MaxVerificationAttempts = 5;

        public AuthService(
            AuthRepository repository,
            IJwtService jwtService,
            IEmailService emailService,
            IOptions<EmailSettings> emailSettings,
            IOptions<JwtSettings> jwtSettings)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _emailSettings = emailSettings.Value;
            _jwtSettings = jwtSettings.Value;
        }

        #region Signup

        public async Task<EmailValidationResult> ValidateEmailForSignupAsync(string email)
        {
            var result = new EmailValidationResult
            {
                IsValid = false,
                IsWorkEmail = false,
                IsAlreadyRegistered = false
            };

            // Basic email format validation
            if (string.IsNullOrWhiteSpace(email) || !IsValidEmailFormat(email))
            {
                result.Message = "Invalid email format";
                return result;
            }

            email = email.Trim().ToLowerInvariant();
            result.Domain = Company.ExtractDomainFromEmail(email);

            // Check if email domain is blocked (personal email)
            var isBlocked = await _repository.IsEmailDomainBlockedAsync(result.Domain);
            if (isBlocked)
            {
                result.Message = "Please use a work email address. Personal email domains are not allowed.";
                return result;
            }

            result.IsWorkEmail = true;

            // Check if email is already registered
            var existingUser = await _repository.GetUserByEmailAsync(email);
            if (existingUser != null)
            {
                result.IsAlreadyRegistered = true;
                result.Message = "This email is already registered. Please login or use a different email.";
                return result;
            }

            // Check if there's an existing company with this domain
            var existingCompany = await _repository.GetCompanyByDomainAsync(result.Domain);
            if (existingCompany != null)
            {
                result.ExistingCompany = existingCompany;
            }

            result.IsValid = true;
            result.Message = "Email is valid for signup";
            return result;
        }

        public async Task<ApiResponse> InitiateSignupAsync(SignupInitRequest request, string ipAddress = null)
        {
            try
            {
                // Validate email first
                var emailValidation = await ValidateEmailForSignupAsync(request.Email);
                if (!emailValidation.IsValid)
                {
                    return ApiResponse.Fail(emailValidation.Message);
                }

                // Validate password strength
                if (!ValidatePasswordStrength(request.Password, out string passwordError))
                {
                    return ApiResponse.Fail(passwordError);
                }

                var email = request.Email.Trim().ToLowerInvariant();

                // Check for existing pending verification (rate limiting)
                var hasRecentCode = await _repository.HasRecentVerificationCodeAsync(email, "SIGNUP", 2);
                if (hasRecentCode)
                {
                    return ApiResponse.Fail("A verification code was recently sent. Please wait 2 minutes before requesting another.");
                }

                // Generate verification code
                var code = GenerateVerificationCode();
                var expiresAt = DateTime.UtcNow.AddMinutes(_emailSettings.VerificationCodeExpiryMinutes);

                // Store verification code
                await _repository.CreateVerificationCodeAsync(email, code, "SIGNUP", expiresAt, ipAddress);

                // Send verification email
                var emailSent = await _emailService.SendVerificationCodeAsync(email, request.FullName, code);
                if (!emailSent)
                {
                    return ApiResponse.Fail("Failed to send verification email. Please try again.");
                }

                Console.WriteLine($"[AuthService] Signup initiated for {email}, code: {code}");

                return ApiResponse.Ok("Verification code sent to your email. Please check your inbox.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] InitiateSignup error: {ex.Message}");
                return ApiResponse.Fail("An error occurred during signup. Please try again.");
            }
        }

        public async Task<SignupResponse> VerifyEmailAndCompleteSignupAsync(VerifyEmailRequest request)
        {
            var response = new SignupResponse { Success = false };

            try
            {
                var email = request.Email.Trim().ToLowerInvariant();

                // Get the verification record
                var verification = await _repository.GetVerificationCodeAsync(email, "SIGNUP");

                if (verification == null)
                {
                    response.Message = "No pending verification found. Please start the signup process again.";
                    return response;
                }

                // Check expiry
                if (DateTime.UtcNow > (DateTime)verification.ExpiresAt)
                {
                    response.Message = "Verification code has expired. Please request a new one.";
                    return response;
                }

                // Check attempts
                if ((int)verification.Attempts >= MaxVerificationAttempts)
                {
                    response.Message = "Too many failed attempts. Please request a new verification code.";
                    return response;
                }

                // Verify code
                if (verification.Code != request.Code)
                {
                    await _repository.IncrementVerificationAttemptsAsync((int)verification.VerificationID);
                    response.Message = "Invalid verification code. Please try again.";
                    return response;
                }

                // Code is valid - mark as used
                await _repository.MarkVerificationCodeUsedAsync((int)verification.VerificationID);

                response.Success = true;
                response.Message = "Email verified successfully! Please complete your registration.";
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] VerifyEmail error: {ex.Message}");
                response.Message = "An error occurred during verification. Please try again.";
                return response;
            }
        }

        public async Task<SignupResponse> CompleteSignupAsync(SignupInitRequest request, string ipAddress = null)
        {
            var response = new SignupResponse { Success = false };

            try
            {
                var email = request.Email.Trim().ToLowerInvariant();
                var domain = Company.ExtractDomainFromEmail(email);

                // Double-check email is not already registered
                var existingUser = await _repository.GetUserByEmailAsync(email);
                if (existingUser != null)
                {
                    response.Message = "This email is already registered.";
                    return response;
                }

                // Check if verification was completed
                var verificationComplete = await _repository.IsEmailVerifiedRecentlyAsync(email, "SIGNUP", 1);
                if (!verificationComplete)
                {
                    response.Message = "Email not verified. Please verify your email first.";
                    return response;
                }

                // Create company and user in transaction
                var passwordHash = HashPassword(request.Password);
                var result = await _repository.CreateCompanyAndAdminAsync(
                    companyName: request.CompanyName.Trim(),
                    domain: domain,
                    size: request.CompanySize,
                    email: email,
                    fullName: request.FullName.Trim(),
                    passwordHash: passwordHash
                );

                if (!result.Success)
                {
                    response.Message = result.ErrorMessage ?? "Failed to create account.";
                    return response;
                }

                // Log audit
                await _repository.LogAuditAsync(
                    result.UserId, result.CompanyId, "SIGNUP", "User", result.UserId,
                    JsonSerializer.Serialize(new { CompanyName = request.CompanyName }), ipAddress);

                // Get full user info
                var user = await _repository.GetUserByIdAsync(result.UserId);

                // Generate tokens
                var accessToken = _jwtService.GenerateAccessToken(user);
                var refreshToken = _jwtService.GenerateRefreshToken();

                // Store refresh token
                var refreshExpiry = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays);
                await _repository.CreateSessionAsync(result.UserId, refreshToken, null, ipAddress, refreshExpiry);

                // Send welcome email (fire and forget)
                _ = _emailService.SendWelcomeEmailAsync(email, request.FullName, request.CompanyName);

                response.Success = true;
                response.Message = "Account created successfully!";
                response.UserId = result.UserId;
                response.CompanyId = result.CompanyId;
                response.Token = accessToken;
                response.RefreshToken = refreshToken;
                response.User = MapToUserInfoDto(user);

                Console.WriteLine($"[AuthService] ✅ Signup completed for {email}, Company: {request.CompanyName}");
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] CompleteSignup error: {ex.Message}");
                response.Message = "An error occurred during signup. Please try again.";
                return response;
            }
        }

        public async Task<ApiResponse> ResendVerificationCodeAsync(ResendCodeRequest request, string ipAddress = null)
        {
            try
            {
                var email = request.Email.Trim().ToLowerInvariant();

                // Rate limiting - check recent codes
                var recentCodeCount = await _repository.GetRecentVerificationCodeCountAsync(email, request.Purpose, 1);
                if (recentCodeCount >= 5)
                {
                    return ApiResponse.Fail("Too many verification codes requested. Please try again later.");
                }

                // Get user name if exists
                var user = await _repository.GetUserByEmailAsync(email);
                var userName = user?.FullName ?? "User";

                // Generate new code
                var code = GenerateVerificationCode();
                var expiresAt = DateTime.UtcNow.AddMinutes(_emailSettings.VerificationCodeExpiryMinutes);

                // Store verification code (marks old ones as used automatically)
                await _repository.CreateVerificationCodeAsync(email, code, request.Purpose, expiresAt, ipAddress);

                // Send email based on purpose
                bool emailSent = request.Purpose switch
                {
                    "2FA" => await _emailService.SendTwoFactorCodeAsync(email, userName, code),
                    _ => await _emailService.SendVerificationCodeAsync(email, userName, code)
                };

                if (!emailSent)
                {
                    return ApiResponse.Fail("Failed to send verification code. Please try again.");
                }

                return ApiResponse.Ok("Verification code sent successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] ResendCode error: {ex.Message}");
                return ApiResponse.Fail("An error occurred. Please try again.");
            }
        }

        #endregion

        #region Login

        public async Task<LoginResponse> LoginAsync(LoginRequest request, string ipAddress = null, string deviceInfo = null)
        {
            var response = new LoginResponse { Success = false };

            try
            {
                var email = request.Email.Trim().ToLowerInvariant();

                // Get user with company and plan info
                var user = await _repository.GetUserByEmailAsync(email);

                if (user == null)
                {
                    response.Message = "Invalid email or password.";
                    return response;
                }

                // Check if account is active
                if (!user.IsActive)
                {
                    response.Message = "Your account has been deactivated. Please contact your administrator.";
                    return response;
                }

                // Check if account is locked
                if (user.IsLocked)
                {
                    response.Message = $"Your account is locked due to multiple failed login attempts. Please try again after {user.LockedUntil:MMM dd, yyyy HH:mm} UTC.";
                    return response;
                }

                // Verify password
                if (!VerifyPassword(request.Password, user.PasswordHash))
                {
                    // Record failed attempt
                    user.RecordFailedLogin(MaxLoginAttempts, LockoutHours);
                    await _repository.UpdateUserLoginFailedAsync(user.UserID, user.FailedLoginAttempts, user.LockedUntil);

                    // Send locked notification if just locked
                    if (user.IsLocked)
                    {
                        _ = _emailService.SendAccountLockedNotificationAsync(user.Email, user.FullName, user.LockedUntil.Value);
                        response.Message = "Your account has been locked due to multiple failed login attempts. Check your email for details.";
                    }
                    else
                    {
                        var attemptsLeft = MaxLoginAttempts - user.FailedLoginAttempts;
                        response.Message = $"Invalid email or password. {attemptsLeft} attempt(s) remaining.";
                    }

                    // Log failed attempt
                    await _repository.LogAuditAsync(user.UserID, user.CompanyID, "LOGIN_FAILED", "User", user.UserID, null, ipAddress);

                    return response;
                }

                // Check if email is verified
                if (!user.EmailVerified)
                {
                    response.Message = "Please verify your email address before logging in.";
                    return response;
                }

                // Check if 2FA is enabled
                if (user.TwoFactorEnabled)
                {
                    // Generate and send 2FA code
                    var code = GenerateVerificationCode();
                    var expiresAt = DateTime.UtcNow.AddMinutes(10);

                    await _repository.CreateVerificationCodeAsync(user.Email, code, "2FA", expiresAt, ipAddress);
                    await _emailService.SendTwoFactorCodeAsync(user.Email, user.FullName, code);

                    response.RequiresTwoFactor = true;
                    response.TwoFactorToken = _jwtService.GenerateTwoFactorToken(user.UserID);
                    response.Message = "Please enter the verification code sent to your email.";
                    return response;
                }

                // Check if password change is required
                if (user.ForcePasswordChange)
                {
                    response.RequiresPasswordChange = true;
                    response.TwoFactorToken = _jwtService.GenerateTwoFactorToken(user.UserID);
                    response.Message = "You must change your password before continuing.";
                    return response;
                }

                // Successful login
                return await CompleteLoginAsync(user, request.RememberMe, ipAddress, deviceInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Login error: {ex.Message}");
                response.Message = "An error occurred during login. Please try again.";
                return response;
            }
        }

        public async Task<LoginResponse> VerifyTwoFactorAsync(TwoFactorRequest request, string ipAddress = null, string deviceInfo = null)
        {
            var response = new LoginResponse { Success = false };

            try
            {
                // Validate 2FA token
                var userId = _jwtService.ValidateTwoFactorToken(request.TwoFactorToken);
                if (!userId.HasValue)
                {
                    response.Message = "Invalid or expired session. Please login again.";
                    return response;
                }

                // Get user
                var user = await _repository.GetUserByIdAsync(userId.Value);
                if (user == null)
                {
                    response.Message = "User not found.";
                    return response;
                }

                // Verify the 2FA code
                var verification = await _repository.GetVerificationCodeAsync(user.Email, "2FA");

                if (verification == null)
                {
                    response.Message = "No verification code found. Please login again.";
                    return response;
                }

                if (DateTime.UtcNow > (DateTime)verification.ExpiresAt)
                {
                    response.Message = "Verification code has expired. Please login again.";
                    return response;
                }

                if ((int)verification.Attempts >= MaxVerificationAttempts)
                {
                    response.Message = "Too many failed attempts. Please login again.";
                    return response;
                }

                if (verification.Code != request.Code)
                {
                    await _repository.IncrementVerificationAttemptsAsync((int)verification.VerificationID);
                    response.Message = "Invalid verification code.";
                    return response;
                }

                // Mark code as used
                await _repository.MarkVerificationCodeUsedAsync((int)verification.VerificationID);

                // Complete login
                return await CompleteLoginAsync(user, false, ipAddress, deviceInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] VerifyTwoFactor error: {ex.Message}");
                response.Message = "An error occurred. Please try again.";
                return response;
            }
        }

        private async Task<LoginResponse> CompleteLoginAsync(User user, bool rememberMe, string ipAddress, string deviceInfo)
        {
            // Reset failed attempts and update last login
            user.RecordSuccessfulLogin();
            await _repository.UpdateUserLoginSuccessAsync(user.UserID);

            // Generate tokens
            var accessToken = _jwtService.GenerateAccessToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Calculate refresh token expiry based on "remember me"
            var refreshExpiry = rememberMe
                ? DateTime.UtcNow.AddDays(_jwtSettings.RememberMeExpiryDays)
                : DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays);

            // Store refresh token
            await _repository.CreateSessionAsync(user.UserID, refreshToken, deviceInfo, ipAddress, refreshExpiry);

            // Log successful login
            await _repository.LogAuditAsync(user.UserID, user.CompanyID, "LOGIN", "User", user.UserID, null, ipAddress);

            Console.WriteLine($"[AuthService] ✅ Login successful for {user.Email}");

            return new LoginResponse
            {
                Success = true,
                Message = "Login successful",
                Token = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes),
                User = MapToUserInfoDto(user)
            };
        }

        public async Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request, string ipAddress = null)
        {
            var response = new LoginResponse { Success = false };

            try
            {
                // Find the session
                var session = await _repository.GetSessionByRefreshTokenAsync(request.RefreshToken);

                if (session == null)
                {
                    response.Message = "Invalid refresh token.";
                    return response;
                }

                if (DateTime.UtcNow > (DateTime)session.ExpiresAt)
                {
                    response.Message = "Refresh token has expired. Please login again.";
                    return response;
                }

                if (!(bool)session.IsActive)
                {
                    response.Message = "Your account has been deactivated.";
                    return response;
                }

                // Get full user info
                var user = await _repository.GetUserByIdAsync((int)session.UserID);
                if (user == null)
                {
                    response.Message = "User not found.";
                    return response;
                }

                // Generate new tokens
                var newAccessToken = _jwtService.GenerateAccessToken(user);
                var newRefreshToken = _jwtService.GenerateRefreshToken();

                // Revoke old session and create new one (token rotation)
                await _repository.RevokeSessionAsync((int)session.SessionID);
                await _repository.CreateSessionAsync(
                    user.UserID, newRefreshToken, null, ipAddress,
                    DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays));

                return new LoginResponse
                {
                    Success = true,
                    Message = "Token refreshed successfully",
                    Token = newAccessToken,
                    RefreshToken = newRefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes),
                    User = MapToUserInfoDto(user)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] RefreshToken error: {ex.Message}");
                response.Message = "An error occurred. Please login again.";
                return response;
            }
        }

        public async Task<ApiResponse> LogoutAsync(int userId, string refreshToken = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    await _repository.RevokeSessionByTokenAsync(userId, refreshToken);
                }
                else
                {
                    await _repository.RevokeLatestSessionAsync(userId);
                }

                await _repository.LogAuditAsync(userId, null, "LOGOUT", "User", userId, null, null);

                return ApiResponse.Ok("Logged out successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Logout error: {ex.Message}");
                return ApiResponse.Fail("An error occurred during logout.");
            }
        }

        public async Task<ApiResponse> LogoutAllDevicesAsync(int userId)
        {
            try
            {
                var revokedCount = await _repository.RevokeAllUserSessionsAsync(userId);

                await _repository.LogAuditAsync(userId, null, "LOGOUT_ALL", "User", userId,
                    JsonSerializer.Serialize(new { RevokedSessions = revokedCount }), null);

                return ApiResponse.Ok($"Logged out from {revokedCount} device(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] LogoutAll error: {ex.Message}");
                return ApiResponse.Fail("An error occurred.");
            }
        }

        #endregion

        #region Password Management

        public async Task<ApiResponse> ForgotPasswordAsync(ForgotPasswordRequest request, string ipAddress = null)
        {
            try
            {
                var email = request.Email.Trim().ToLowerInvariant();

                // Get user
                var user = await _repository.GetUserByEmailAsync(email);

                // Always return success to prevent email enumeration
                if (user == null || !user.IsActive)
                {
                    Console.WriteLine($"[AuthService] Password reset requested for non-existent/inactive email: {email}");
                    return ApiResponse.Ok("If an account exists with this email, you will receive a password reset link.");
                }

                // Rate limiting
                var recentTokens = await _repository.GetRecentPasswordResetCountAsync(user.UserID, 1);
                if (recentTokens >= 3)
                {
                    return ApiResponse.Ok("If an account exists with this email, you will receive a password reset link.");
                }

                // Generate reset token
                var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                var expiresAt = DateTime.UtcNow.AddHours(_emailSettings.PasswordResetTokenExpiryHours);

                // Store reset token
                await _repository.CreatePasswordResetTokenAsync(user.UserID, token, expiresAt, ipAddress);

                // Send email
                await _emailService.SendPasswordResetEmailAsync(email, user.FullName ?? "User", token);

                await _repository.LogAuditAsync(user.UserID, null, "PASSWORD_RESET_REQUESTED", "User", user.UserID, null, ipAddress);

                return ApiResponse.Ok("If an account exists with this email, you will receive a password reset link.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] ForgotPassword error: {ex.Message}");
                return ApiResponse.Fail("An error occurred. Please try again.");
            }
        }

        public async Task<ApiResponse> ResetPasswordAsync(ResetPasswordRequest request)
        {
            try
            {
                var email = request.Email.Trim().ToLowerInvariant();

                if (!ValidatePasswordStrength(request.NewPassword, out string passwordError))
                {
                    return ApiResponse.Fail(passwordError);
                }

                // Find valid token
                var resetRecord = await _repository.GetPasswordResetTokenAsync(request.Token, email);

                if (resetRecord == null)
                {
                    return ApiResponse.Fail("Invalid or expired reset link. Please request a new one.");
                }

                if (DateTime.UtcNow > (DateTime)resetRecord.ExpiresAt)
                {
                    return ApiResponse.Fail("This reset link has expired. Please request a new one.");
                }

                // Update password
                var passwordHash = HashPassword(request.NewPassword);
                await _repository.UpdateUserPasswordAsync((int)resetRecord.UserID, passwordHash);

                // Mark token as used
                await _repository.MarkPasswordResetTokenUsedAsync((int)resetRecord.TokenID);

                // Revoke all sessions (force re-login)
                await _repository.RevokeAllUserSessionsAsync((int)resetRecord.UserID);

                // Send confirmation email
                await _emailService.SendPasswordChangedNotificationAsync((string)resetRecord.Email, (string)resetRecord.FullName ?? "User");

                await _repository.LogAuditAsync((int)resetRecord.UserID, null, "PASSWORD_RESET", "User", (int)resetRecord.UserID, null, null);

                return ApiResponse.Ok("Password reset successfully. You can now login with your new password.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] ResetPassword error: {ex.Message}");
                return ApiResponse.Fail("An error occurred. Please try again.");
            }
        }

        public async Task<ApiResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request)
        {
            try
            {
                if (!ValidatePasswordStrength(request.NewPassword, out string passwordError))
                {
                    return ApiResponse.Fail(passwordError);
                }

                // Get user
                var user = await _repository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return ApiResponse.Fail("User not found.");
                }

                // Verify current password
                if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
                {
                    return ApiResponse.Fail("Current password is incorrect.");
                }

                // Check if new password is same as current
                if (VerifyPassword(request.NewPassword, user.PasswordHash))
                {
                    return ApiResponse.Fail("New password must be different from current password.");
                }

                // Update password
                var passwordHash = HashPassword(request.NewPassword);
                await _repository.UpdateUserPasswordAsync(userId, passwordHash);

                // Send confirmation email
                await _emailService.SendPasswordChangedNotificationAsync(user.Email, user.FullName ?? "User");

                await _repository.LogAuditAsync(userId, null, "PASSWORD_CHANGED", "User", userId, null, null);

                return ApiResponse.Ok("Password changed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] ChangePassword error: {ex.Message}");
                return ApiResponse.Fail("An error occurred. Please try again.");
            }
        }

        #endregion

        #region User Management

        public async Task<UserInfoDto> GetCurrentUserAsync(int userId)
        {
            var user = await _repository.GetUserByIdAsync(userId);
            return user != null ? MapToUserInfoDto(user) : null;
        }

        public async Task<ApiResponse<UserInfoDto>> InviteUserAsync(int adminUserId, InviteUserRequest request)
        {
            try
            {
                // Get admin user and company
                var admin = await _repository.GetUserByIdAsync(adminUserId);
                if (admin == null || !admin.IsAdmin || !admin.CompanyID.HasValue)
                {
                    return ApiResponse<UserInfoDto>.Fail("Unauthorized to invite users.");
                }

                var companyId = admin.CompanyID.Value;

                // Check company limits
                var canAdd = await CanAddResourceAsync(companyId, "BusinessUser");
                if (!canAdd)
                {
                    return ApiResponse<UserInfoDto>.Fail("Your plan's user limit has been reached. Please upgrade to add more users.");
                }

                var email = request.Email.Trim().ToLowerInvariant();
                var adminDomain = Company.ExtractDomainFromEmail(admin.Email);
                var inviteDomain = Company.ExtractDomainFromEmail(email);

                // Validate email domain matches company domain
                if (adminDomain != inviteDomain)
                {
                    return ApiResponse<UserInfoDto>.Fail($"Invited user must have the same email domain (@{adminDomain}).");
                }

                // Check if email already exists
                var existingUser = await _repository.GetUserByEmailAsync(email);
                if (existingUser != null)
                {
                    return ApiResponse<UserInfoDto>.Fail("A user with this email already exists.");
                }

                // Validate password
                if (!ValidatePasswordStrength(request.Password, out string passwordError))
                {
                    return ApiResponse<UserInfoDto>.Fail(passwordError);
                }

                // Create user
                var passwordHash = HashPassword(request.Password);
                var userId = await _repository.CreateInvitedUserAsync(
                    email: email,
                    fullName: request.FullName.Trim(),
                    passwordHash: passwordHash,
                    roleId: request.RoleId,
                    companyId: companyId,
                    forcePasswordChange: request.ForcePasswordChange,
                    invitedBy: adminUserId
                );

                // Grant access to specified dashboards
                if (request.DashboardIds?.Any() == true)
                {
                    foreach (var dashboardId in request.DashboardIds)
                    {
                        await _repository.GrantDashboardAccessAsync(userId, dashboardId, "VIEW", adminUserId);
                    }
                }

                // Get company name
                var company = await _repository.GetCompanyByIdAsync(companyId);

                // Send invitation email
                await _emailService.SendBusinessUserInvitationAsync(
                    email,
                    request.FullName,
                    request.Password,
                    company?.CompanyName ?? "Your Company",
                    admin.FullName ?? admin.Email,
                    request.ForcePasswordChange);

                await _repository.LogAuditAsync(adminUserId, companyId, "USER_INVITED", "User", userId,
                    JsonSerializer.Serialize(new { Email = email, Role = request.RoleId }), null);

                var newUser = await _repository.GetUserByIdAsync(userId);
                return ApiResponse<UserInfoDto>.Ok(MapToUserInfoDto(newUser), "User invited successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] InviteUser error: {ex.Message}");
                return ApiResponse<UserInfoDto>.Fail("An error occurred while inviting the user.");
            }
        }

        public async Task<ApiResponse<List<UserListItemDto>>> GetCompanyUsersAsync(int companyId)
        {
            try
            {
                var users = await _repository.GetUsersByCompanyIdAsync(companyId);
                return ApiResponse<List<UserListItemDto>>.Ok(users.ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] GetCompanyUsers error: {ex.Message}");
                return ApiResponse<List<UserListItemDto>>.Fail("An error occurred.");
            }
        }

        public async Task<ApiResponse> DeactivateUserAsync(int adminUserId, int targetUserId)
        {
            try
            {
                // Verify admin has permission
                var admin = await _repository.GetUserByIdAsync(adminUserId);
                var target = await _repository.GetUserByIdAsync(targetUserId);

                if (admin == null || target == null)
                    return ApiResponse.Fail("User not found.");

                if (!admin.IsAdmin || admin.CompanyID != target.CompanyID)
                    return ApiResponse.Fail("Unauthorized.");

                if (adminUserId == targetUserId)
                    return ApiResponse.Fail("You cannot deactivate your own account.");

                await _repository.SetUserActiveStatusAsync(targetUserId, false);

                // Revoke all sessions
                await _repository.RevokeAllUserSessionsAsync(targetUserId);

                await _repository.LogAuditAsync(adminUserId, admin.CompanyID, "USER_DEACTIVATED", "User", targetUserId, null, null);

                return ApiResponse.Ok("User deactivated successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] DeactivateUser error: {ex.Message}");
                return ApiResponse.Fail("An error occurred.");
            }
        }

        public async Task<ApiResponse> ReactivateUserAsync(int adminUserId, int targetUserId)
        {
            try
            {
                var admin = await _repository.GetUserByIdAsync(adminUserId);
                var target = await _repository.GetUserByIdAsync(targetUserId);

                if (admin == null || target == null)
                    return ApiResponse.Fail("User not found.");

                if (!admin.IsAdmin || admin.CompanyID != target.CompanyID)
                    return ApiResponse.Fail("Unauthorized.");

                await _repository.SetUserActiveStatusAsync(targetUserId, true);

                await _repository.LogAuditAsync(adminUserId, admin.CompanyID, "USER_REACTIVATED", "User", targetUserId, null, null);

                return ApiResponse.Ok("User reactivated successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] ReactivateUser error: {ex.Message}");
                return ApiResponse.Fail("An error occurred.");
            }
        }

        #endregion

        #region Company & Plan

        public async Task<CompanyUsageDto> GetCompanyUsageAsync(int companyId)
        {
            try
            {
                return await _repository.GetCompanyUsageAsync(companyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] GetCompanyUsage error: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> CanAddResourceAsync(int companyId, string resourceType)
        {
            var usage = await GetCompanyUsageAsync(companyId);
            if (usage == null) return false;

            return resourceType.ToLower() switch
            {
                "admin" => usage.CanAddAdmin,
                "businessuser" => usage.CanAddBusinessUser,
                "database" => usage.CanAddDatabase,
                "dashboard" => usage.CanAddDashboard,
                _ => false
            };
        }

        #endregion

        #region Utilities

        public string GenerateVerificationCode()
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var number = BitConverter.ToUInt32(bytes, 0) % 10000;
            return number.ToString("D4"); // Pad to 4 digits
        }

        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        public bool VerifyPassword(string password, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch
            {
                return false;
            }
        }

        public bool ValidatePasswordStrength(string password, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrEmpty(password))
            {
                errorMessage = "Password is required.";
                return false;
            }

            if (password.Length < 6)
            {
                errorMessage = "Password must be at least 6 characters long.";
                return false;
            }

            if (!Regex.IsMatch(password, @"[a-zA-Z]"))
            {
                errorMessage = "Password must contain at least one letter.";
                return false;
            }

            if (!Regex.IsMatch(password, @"\d"))
            {
                errorMessage = "Password must contain at least one number.";
                return false;
            }

            return true;
        }

        private bool IsValidEmailFormat(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email.Trim();
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Private Helpers

        private UserInfoDto MapToUserInfoDto(User user)
        {
            if (user == null) return null;

            return new UserInfoDto
            {
                UserId = user.UserID,
                Email = user.Email,
                FullName = user.FullName,
                Username = user.Username,
                RoleId = user.RoleID,
                RoleName = user.RoleID switch { 1 => "Admin", 2 => "BusinessUser", 3 => "SuperAdmin", _ => "User" },
                CompanyId = user.CompanyID,
                CompanyName = user.Company?.CompanyName,
                CompanyDomain = user.Company?.Domain,
                EmailVerified = user.EmailVerified,
                TwoFactorEnabled = user.TwoFactorEnabled,
                LastLogin = user.LastLogin,
                Plan = user.Company?.Plan != null ? new PlanInfoDto
                {
                    PlanId = user.Company.Plan.PlanID,
                    PlanName = user.Company.Plan.PlanName,
                    PlanCode = user.Company.Plan.PlanCode,
                    MaxAdmins = user.Company.Plan.MaxAdmins,
                    MaxBusinessUsers = user.Company.Plan.MaxBusinessUsers,
                    MaxDatabases = user.Company.Plan.MaxDatabases,
                    MaxDashboards = user.Company.Plan.MaxDashboards,
                    Features = user.Company.Plan.GetFeatures()
                } : null
            };
        }

        #endregion
    }
}