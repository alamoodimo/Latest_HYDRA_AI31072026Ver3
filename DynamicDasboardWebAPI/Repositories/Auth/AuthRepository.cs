using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using DynamicDashboardCommon.DTOs;
using DynamicDashboardCommon.Models;
using DynamicDasboardWebAPI.Utilities;

namespace DynamicDasboardWebAPI.Repositories
{
    /// <summary>
    /// Repository for authentication-related database operations.
    /// </summary>
    public class AuthRepository : BaseRepository
    {
        public AuthRepository(
            IDbConnection appDbConnection,
            DbConnectionFactory connectionFactory)
            : base(appDbConnection, connectionFactory)
        {
        }

        #region User Operations

        /// <summary>
        /// Gets a user by email with company and plan info
        /// </summary>
        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await WithConnectionAsync(async conn =>
            {
                var sql = @"
                    SELECT u.UserID, u.Username, u.Email, u.FullName, u.PasswordHash, u.RoleID, 
                           u.CompanyID, u.EmailVerified, u.IsActive, u.FailedLoginAttempts, 
                           u.LockedUntil, u.ForcePasswordChange, u.TwoFactorEnabled, 
                           u.LastLogin, u.CreatedAt, u.InvitedBy,
                           c.CompanyID, c.CompanyName, c.Domain, c.Size, c.PlanID, c.IsActive as CompanyIsActive,
                           p.PlanID, p.PlanName, p.PlanCode, p.MaxAdmins, p.MaxBusinessUsers, 
                           p.MaxDatabases, p.MaxDashboards, p.Features
                    FROM Users u
                    LEFT JOIN Companies c ON u.CompanyID = c.CompanyID
                    LEFT JOIN Plans p ON c.PlanID = p.PlanID
                    WHERE LOWER(u.Email) = @Email";

                var result = await conn.QueryAsync<User, Company, Plan, User>(
                    sql,
                    (user, company, plan) =>
                    {
                        if (company != null)
                        {
                            company.Plan = plan;
                            user.Company = company;
                        }
                        return user;
                    },
                    new { Email = email.ToLowerInvariant() },
                    splitOn: "CompanyID,PlanID"
                );

                return result.FirstOrDefault();
            });
        }

        /// <summary>
        /// Gets a user by ID with company and plan info
        /// </summary>
        public async Task<User> GetUserByIdAsync(int userId)
        {
            return await WithConnectionAsync(async conn =>
            {
                var sql = @"
                    SELECT u.UserID, u.Username, u.Email, u.FullName, u.PasswordHash, u.RoleID, 
                           u.CompanyID, u.EmailVerified, u.IsActive, u.FailedLoginAttempts, 
                           u.LockedUntil, u.ForcePasswordChange, u.TwoFactorEnabled, 
                           u.LastLogin, u.CreatedAt, u.InvitedBy,
                           c.CompanyID, c.CompanyName, c.Domain, c.Size, c.PlanID, c.IsActive as CompanyIsActive,
                           p.PlanID, p.PlanName, p.PlanCode, p.MaxAdmins, p.MaxBusinessUsers, 
                           p.MaxDatabases, p.MaxDashboards, p.Features
                    FROM Users u
                    LEFT JOIN Companies c ON u.CompanyID = c.CompanyID
                    LEFT JOIN Plans p ON c.PlanID = p.PlanID
                    WHERE u.UserID = @UserID";

                var result = await conn.QueryAsync<User, Company, Plan, User>(
                    sql,
                    (user, company, plan) =>
                    {
                        if (company != null)
                        {
                            company.Plan = plan;
                            user.Company = company;
                        }
                        return user;
                    },
                    new { UserID = userId },
                    splitOn: "CompanyID,PlanID"
                );

                return result.FirstOrDefault();
            });
        }

        /// <summary>
        /// Gets users by company ID for admin listing
        /// </summary>
        public async Task<IEnumerable<UserListItemDto>> GetUsersByCompanyIdAsync(int companyId)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QueryAsync<UserListItemDto>(@"
                    SELECT u.UserID AS UserId, u.Email, u.FullName, r.RoleName, u.IsActive, 
                           u.EmailVerified, u.LastLogin, u.CreatedAt,
                           (SELECT COUNT(*) FROM UserDashboardAccess WHERE UserID = u.UserID) AS DashboardCount
                    FROM Users u
                    INNER JOIN UserRoles r ON u.RoleID = r.RoleID
                    WHERE u.CompanyID = @CompanyID
                    ORDER BY u.RoleID, u.CreatedAt DESC",
                    new { CompanyID = companyId });
            });
        }

        /// <summary>
        /// Creates a new invited user (business user)
        /// </summary>
        public async Task<int> CreateInvitedUserAsync(string email, string fullName, string passwordHash,
            int roleId, int companyId, bool forcePasswordChange, int invitedBy)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QuerySingleAsync<int>(@"
                    INSERT INTO Users (Username, Email, FullName, PasswordHash, RoleID, CompanyID, 
                                      EmailVerified, IsActive, ForcePasswordChange, InvitedBy, CreatedAt)
                    VALUES (@Email, @Email, @FullName, @PasswordHash, @RoleID, @CompanyID, 
                            1, 1, @ForcePasswordChange, @InvitedBy, GETUTCDATE());
                    SELECT CAST(SCOPE_IDENTITY() as int)",
                    new
                    {
                        Email = email,
                        FullName = fullName,
                        PasswordHash = passwordHash,
                        RoleID = roleId,
                        CompanyID = companyId,
                        ForcePasswordChange = forcePasswordChange,
                        InvitedBy = invitedBy
                    });
            });
        }

        /// <summary>
        /// Updates user after failed login attempt
        /// </summary>
        public async Task UpdateUserLoginFailedAsync(int userId, int failedAttempts, DateTime? lockedUntil)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE Users 
                    SET FailedLoginAttempts = @Attempts, LockedUntil = @LockedUntil 
                    WHERE UserID = @UserID",
                    new { Attempts = failedAttempts, LockedUntil = lockedUntil, UserID = userId });
            });
        }

        /// <summary>
        /// Updates user after successful login
        /// </summary>
        public async Task UpdateUserLoginSuccessAsync(int userId)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE Users 
                    SET FailedLoginAttempts = 0, LockedUntil = NULL, LastLogin = GETUTCDATE() 
                    WHERE UserID = @UserID",
                    new { UserID = userId });
            });
        }

        /// <summary>
        /// Updates user password
        /// </summary>
        public async Task UpdateUserPasswordAsync(int userId, string passwordHash)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE Users 
                    SET PasswordHash = @PasswordHash, 
                        PasswordChangedAt = GETUTCDATE(), 
                        ForcePasswordChange = 0,
                        FailedLoginAttempts = 0,
                        LockedUntil = NULL
                    WHERE UserID = @UserID",
                    new { PasswordHash = passwordHash, UserID = userId });
            });
        }

        /// <summary>
        /// Sets user active status
        /// </summary>
        public async Task SetUserActiveStatusAsync(int userId, bool isActive)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(
                    "UPDATE Users SET IsActive = @IsActive WHERE UserID = @UserID",
                    new { IsActive = isActive, UserID = userId });
            });
        }

        #endregion

        #region Company Operations

        /// <summary>
        /// Gets a company by domain
        /// </summary>
        public async Task<Company> GetCompanyByDomainAsync(string domain)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QueryFirstOrDefaultAsync<Company>(@"
                    SELECT CompanyID, CompanyName, Domain, Size, PlanID, IsActive, CreatedAt
                    FROM Companies 
                    WHERE Domain = @Domain AND IsActive = 1",
                    new { Domain = domain });
            });
        }

        /// <summary>
        /// Gets a company by ID
        /// </summary>
        public async Task<Company> GetCompanyByIdAsync(int companyId)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QueryFirstOrDefaultAsync<Company>(@"
                    SELECT CompanyID, CompanyName, Domain, Size, PlanID, IsActive, CreatedAt
                    FROM Companies 
                    WHERE CompanyID = @CompanyID",
                    new { CompanyID = companyId });
            });
        }

        /// <summary>
        /// Creates company and admin user in a transaction
        /// </summary>
        public async Task<(bool Success, int CompanyId, int UserId, string ErrorMessage)> CreateCompanyAndAdminAsync(
            string companyName, string domain, string size, string email, string fullName, string passwordHash)
        {
            return await WithConnectionAsync(async conn =>
            {
                // Check if we need to use transaction (if connection supports it)
                if (conn is System.Data.Common.DbConnection dbConn)
                {
                    if (dbConn.State != ConnectionState.Open)
                        await dbConn.OpenAsync();

                    using var transaction = await dbConn.BeginTransactionAsync();

                    try
                    {
                        // Create company
                        var companyId = await conn.QuerySingleAsync<int>(@"
                            INSERT INTO Companies (CompanyName, Domain, Size, PlanID, IsActive, CreatedAt)
                            VALUES (@CompanyName, @Domain, @Size, 1, 1, GETUTCDATE());
                            SELECT CAST(SCOPE_IDENTITY() as int)",
                            new { CompanyName = companyName, Domain = domain, Size = size },
                            transaction);

                        // Create admin user
                        var userId = await conn.QuerySingleAsync<int>(@"
                            INSERT INTO Users (Username, Email, FullName, PasswordHash, RoleID, CompanyID, 
                                              EmailVerified, IsActive, CreatedAt, PasswordChangedAt)
                            VALUES (@Email, @Email, @FullName, @PasswordHash, 1, @CompanyID, 
                                    1, 1, GETUTCDATE(), GETUTCDATE());
                            SELECT CAST(SCOPE_IDENTITY() as int)",
                            new
                            {
                                Email = email,
                                FullName = fullName,
                                PasswordHash = passwordHash,
                                CompanyID = companyId
                            },
                            transaction);

                        await transaction.CommitAsync();

                        return (true, companyId, userId, null);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, 0, ex.Message);
                    }
                }
                else
                {
                    // Fallback without transaction (not recommended for production)
                    try
                    {
                        var companyId = await conn.QuerySingleAsync<int>(@"
                            INSERT INTO Companies (CompanyName, Domain, Size, PlanID, IsActive, CreatedAt)
                            VALUES (@CompanyName, @Domain, @Size, 1, 1, GETUTCDATE());
                            SELECT CAST(SCOPE_IDENTITY() as int)",
                            new { CompanyName = companyName, Domain = domain, Size = size });

                        var userId = await conn.QuerySingleAsync<int>(@"
                            INSERT INTO Users (Username, Email, FullName, PasswordHash, RoleID, CompanyID, 
                                              EmailVerified, IsActive, CreatedAt, PasswordChangedAt)
                            VALUES (@Email, @Email, @FullName, @PasswordHash, 1, @CompanyID, 
                                    1, 1, GETUTCDATE(), GETUTCDATE());
                            SELECT CAST(SCOPE_IDENTITY() as int)",
                            new
                            {
                                Email = email,
                                FullName = fullName,
                                PasswordHash = passwordHash,
                                CompanyID = companyId
                            });

                        return (true, companyId, userId, null);
                    }
                    catch (Exception ex)
                    {
                        return (false, 0, 0, ex.Message);
                    }
                }
            });
        }

        /// <summary>
        /// Gets company usage statistics
        /// </summary>
        public async Task<CompanyUsageDto> GetCompanyUsageAsync(int companyId)
        {
            return await WithConnectionAsync(async conn =>
            {
                var usage = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT
                        c.CompanyID,
                        c.CompanyName,
                        p.PlanName,
                        p.MaxAdmins,
                        p.MaxBusinessUsers,
                        p.MaxDatabases,
                        p.MaxDashboards,
                        (SELECT COUNT(*) FROM Users WHERE CompanyID = @CompanyID AND RoleID = 1 AND IsActive = 1) AS CurrentAdmins,
                        (SELECT COUNT(*) FROM Users WHERE CompanyID = @CompanyID AND RoleID = 2 AND IsActive = 1) AS CurrentBusinessUsers,
                        (SELECT COUNT(*) FROM Databases WHERE CompanyID = @CompanyID AND IsActive = 1) AS CurrentDatabases,
                        (SELECT COUNT(*) FROM Dashboards WHERE CompanyID = @CompanyID) AS CurrentDashboards
                    FROM Companies c
                    INNER JOIN Plans p ON c.PlanID = p.PlanID
                    WHERE c.CompanyID = @CompanyID",
                    new { CompanyID = companyId });

                if (usage == null)
                    return null;

                return new CompanyUsageDto
                {
                    CurrentAdmins = (int)usage.CurrentAdmins,
                    CurrentBusinessUsers = (int)usage.CurrentBusinessUsers,
                    CurrentDatabases = (int)usage.CurrentDatabases,
                    CurrentDashboards = (int)usage.CurrentDashboards,
                    CanAddAdmin = (int)usage.MaxAdmins == -1 || (int)usage.CurrentAdmins < (int)usage.MaxAdmins,
                    CanAddBusinessUser = (int)usage.MaxBusinessUsers == -1 || (int)usage.CurrentBusinessUsers < (int)usage.MaxBusinessUsers,
                    CanAddDatabase = (int)usage.MaxDatabases == -1 || (int)usage.CurrentDatabases < (int)usage.MaxDatabases,
                    CanAddDashboard = (int)usage.MaxDashboards == -1 || (int)usage.CurrentDashboards < (int)usage.MaxDashboards
                };
            });
        }

        #endregion

        #region Verification Code Operations

        /// <summary>
        /// Gets the latest active verification code
        /// </summary>
        public async Task<dynamic> GetVerificationCodeAsync(string email, string purpose)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT VerificationID, Code, ExpiresAt, Attempts 
                    FROM EmailVerificationCodes 
                    WHERE Email = @Email AND Purpose = @Purpose AND IsUsed = 0
                    ORDER BY CreatedAt DESC",
                    new { Email = email.ToLowerInvariant(), Purpose = purpose });
            });
        }

        /// <summary>
        /// Checks if there's a recent verification code (rate limiting)
        /// </summary>
        public async Task<bool> HasRecentVerificationCodeAsync(string email, string purpose, int minutesAgo)
        {
            return await WithConnectionAsync(async conn =>
            {
                var count = await conn.QuerySingleAsync<int>(@"
                    SELECT COUNT(*) FROM EmailVerificationCodes 
                    WHERE Email = @Email AND Purpose = @Purpose AND IsUsed = 0 
                    AND CreatedAt > DATEADD(MINUTE, -@Minutes, GETUTCDATE())",
                    new { Email = email.ToLowerInvariant(), Purpose = purpose, Minutes = minutesAgo });

                return count > 0;
            });
        }

        /// <summary>
        /// Gets count of recent verification codes (for rate limiting)
        /// </summary>
        public async Task<int> GetRecentVerificationCodeCountAsync(string email, string purpose, int hoursAgo)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QuerySingleAsync<int>(@"
                    SELECT COUNT(*) FROM EmailVerificationCodes 
                    WHERE Email = @Email AND Purpose = @Purpose 
                    AND CreatedAt > DATEADD(HOUR, -@Hours, GETUTCDATE())",
                    new { Email = email.ToLowerInvariant(), Purpose = purpose, Hours = hoursAgo });
            });
        }

        /// <summary>
        /// Creates a new verification code (marks old ones as used)
        /// </summary>
        public async Task CreateVerificationCodeAsync(string email, string code, string purpose, DateTime expiresAt, string ipAddress)
        {
            await WithConnectionAsync(async conn =>
            {
                // Mark old codes as used
                await conn.ExecuteAsync(@"
                    UPDATE EmailVerificationCodes 
                    SET IsUsed = 1 
                    WHERE Email = @Email AND Purpose = @Purpose AND IsUsed = 0",
                    new { Email = email.ToLowerInvariant(), Purpose = purpose });

                // Insert new code
                await conn.ExecuteAsync(@"
                    INSERT INTO EmailVerificationCodes (Email, Code, Purpose, ExpiresAt, IPAddress, CreatedAt)
                    VALUES (@Email, @Code, @Purpose, @ExpiresAt, @IPAddress, GETUTCDATE())",
                    new
                    {
                        Email = email.ToLowerInvariant(),
                        Code = code,
                        Purpose = purpose,
                        ExpiresAt = expiresAt,
                        IPAddress = ipAddress
                    });
            });
        }

        /// <summary>
        /// Marks a verification code as used
        /// </summary>
        public async Task MarkVerificationCodeUsedAsync(int verificationId)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE EmailVerificationCodes 
                    SET IsUsed = 1, UsedAt = GETUTCDATE() 
                    WHERE VerificationID = @Id",
                    new { Id = verificationId });
            });
        }

        /// <summary>
        /// Increments verification code attempts
        /// </summary>
        public async Task IncrementVerificationAttemptsAsync(int verificationId)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE EmailVerificationCodes 
                    SET Attempts = Attempts + 1 
                    WHERE VerificationID = @Id",
                    new { Id = verificationId });
            });
        }

        /// <summary>
        /// Checks if email was recently verified
        /// </summary>
        public async Task<bool> IsEmailVerifiedRecentlyAsync(string email, string purpose, int hoursAgo)
        {
            return await WithConnectionAsync(async conn =>
            {
                var result = await conn.QueryFirstOrDefaultAsync<bool?>(@"
                    SELECT 1 FROM EmailVerificationCodes 
                    WHERE Email = @Email AND Purpose = @Purpose AND IsUsed = 1
                    AND UsedAt > DATEADD(HOUR, -@Hours, GETUTCDATE())",
                    new { Email = email.ToLowerInvariant(), Purpose = purpose, Hours = hoursAgo });

                return result == true;
            });
        }

        #endregion

        #region Password Reset Operations

        /// <summary>
        /// Gets count of recent password reset tokens
        /// </summary>
        public async Task<int> GetRecentPasswordResetCountAsync(int userId, int hoursAgo)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QuerySingleAsync<int>(@"
                    SELECT COUNT(*) FROM PasswordResetTokens 
                    WHERE UserID = @UserID AND CreatedAt > DATEADD(HOUR, -@Hours, GETUTCDATE())",
                    new { UserID = userId, Hours = hoursAgo });
            });
        }

        /// <summary>
        /// Creates a password reset token
        /// </summary>
        public async Task CreatePasswordResetTokenAsync(int userId, string token, DateTime expiresAt, string ipAddress)
        {
            await WithConnectionAsync(async conn =>
            {
                // Invalidate existing tokens
                await conn.ExecuteAsync(@"
                    UPDATE PasswordResetTokens 
                    SET IsUsed = 1 
                    WHERE UserID = @UserID AND IsUsed = 0",
                    new { UserID = userId });

                // Create new token
                await conn.ExecuteAsync(@"
                    INSERT INTO PasswordResetTokens (UserID, Token, ExpiresAt, IPAddress, CreatedAt)
                    VALUES (@UserID, @Token, @ExpiresAt, @IPAddress, GETUTCDATE())",
                    new { UserID = userId, Token = token, ExpiresAt = expiresAt, IPAddress = ipAddress });
            });
        }

        /// <summary>
        /// Gets a password reset token with user info
        /// </summary>
        public async Task<dynamic> GetPasswordResetTokenAsync(string token, string email)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT r.TokenID, r.UserID, r.ExpiresAt, u.Email, u.FullName
                    FROM PasswordResetTokens r
                    INNER JOIN Users u ON r.UserID = u.UserID
                    WHERE r.Token = @Token AND r.IsUsed = 0 AND LOWER(u.Email) = @Email",
                    new { Token = token, Email = email.ToLowerInvariant() });
            });
        }

        /// <summary>
        /// Marks a password reset token as used
        /// </summary>
        public async Task MarkPasswordResetTokenUsedAsync(int tokenId)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE PasswordResetTokens 
                    SET IsUsed = 1, UsedAt = GETUTCDATE() 
                    WHERE TokenID = @TokenID",
                    new { TokenID = tokenId });
            });
        }

        #endregion

        #region Session Operations

        /// <summary>
        /// Creates a new user session
        /// </summary>
        public async Task CreateSessionAsync(int userId, string refreshToken, string deviceInfo, string ipAddress, DateTime expiresAt)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO UserSessions (UserID, RefreshToken, DeviceInfo, IPAddress, ExpiresAt, CreatedAt)
                    VALUES (@UserID, @RefreshToken, @DeviceInfo, @IPAddress, @ExpiresAt, GETUTCDATE())",
                    new
                    {
                        UserID = userId,
                        RefreshToken = refreshToken,
                        DeviceInfo = deviceInfo,
                        IPAddress = ipAddress,
                        ExpiresAt = expiresAt
                    });
            });
        }

        /// <summary>
        /// Gets a session by refresh token
        /// </summary>
        public async Task<dynamic> GetSessionByRefreshTokenAsync(string refreshToken)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT s.SessionID, s.UserID, s.ExpiresAt, u.IsActive
                    FROM UserSessions s
                    INNER JOIN Users u ON s.UserID = u.UserID
                    WHERE s.RefreshToken = @RefreshToken AND s.IsRevoked = 0",
                    new { RefreshToken = refreshToken });
            });
        }

        /// <summary>
        /// Revokes a session by session ID
        /// </summary>
        public async Task RevokeSessionAsync(int sessionId)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(
                    "UPDATE UserSessions SET IsRevoked = 1 WHERE SessionID = @SessionID",
                    new { SessionID = sessionId });
            });
        }

        /// <summary>
        /// Revokes a session by user ID and refresh token
        /// </summary>
        public async Task RevokeSessionByTokenAsync(int userId, string refreshToken)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE UserSessions 
                    SET IsRevoked = 1 
                    WHERE UserID = @UserID AND RefreshToken = @RefreshToken",
                    new { UserID = userId, RefreshToken = refreshToken });
            });
        }

        /// <summary>
        /// Revokes the latest session for a user
        /// </summary>
        public async Task RevokeLatestSessionAsync(int userId)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    UPDATE UserSessions 
                    SET IsRevoked = 1 
                    WHERE SessionID = (
                        SELECT TOP 1 SessionID 
                        FROM UserSessions 
                        WHERE UserID = @UserID AND IsRevoked = 0 
                        ORDER BY CreatedAt DESC
                    )",
                    new { UserID = userId });
            });
        }

        /// <summary>
        /// Revokes all sessions for a user
        /// </summary>
        public async Task<int> RevokeAllUserSessionsAsync(int userId)
        {
            return await WithConnectionAsync(async conn =>
            {
                return await conn.ExecuteAsync(@"
                    UPDATE UserSessions 
                    SET IsRevoked = 1 
                    WHERE UserID = @UserID AND IsRevoked = 0",
                    new { UserID = userId });
            });
        }

        #endregion

        #region Dashboard Access Operations

        /// <summary>
        /// Grants dashboard access to a user
        /// </summary>
        public async Task GrantDashboardAccessAsync(int userId, int dashboardId, string accessLevel, int grantedBy)
        {
            await WithConnectionAsync(async conn =>
            {
                // Check if access already exists
                var exists = await conn.QueryFirstOrDefaultAsync<int?>(@"
                    SELECT AccessID FROM UserDashboardAccess 
                    WHERE UserID = @UserID AND DashboardID = @DashboardID",
                    new { UserID = userId, DashboardID = dashboardId });

                if (exists.HasValue)
                {
                    // Update existing
                    await conn.ExecuteAsync(@"
                        UPDATE UserDashboardAccess 
                        SET AccessLevel = @AccessLevel, GrantedBy = @GrantedBy, GrantedAt = GETUTCDATE()
                        WHERE AccessID = @AccessID",
                        new { AccessLevel = accessLevel, GrantedBy = grantedBy, AccessID = exists.Value });
                }
                else
                {
                    // Insert new
                    await conn.ExecuteAsync(@"
                        INSERT INTO UserDashboardAccess (UserID, DashboardID, AccessLevel, GrantedBy, GrantedAt)
                        VALUES (@UserID, @DashboardID, @AccessLevel, @GrantedBy, GETUTCDATE())",
                        new { UserID = userId, DashboardID = dashboardId, AccessLevel = accessLevel, GrantedBy = grantedBy });
                }
            });
        }

        #endregion

        #region Audit Operations

        /// <summary>
        /// Logs an audit entry
        /// </summary>
        public async Task LogAuditAsync(int? userId, int? companyId, string action, string entityType, int? entityId, string details, string ipAddress)
        {
            await WithConnectionAsync(async conn =>
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO AuditLogs (UserID, CompanyID, Action, EntityType, EntityID, Details, IPAddress, CreatedAt)
                    VALUES (@UserID, @CompanyID, @Action, @EntityType, @EntityID, @Details, @IPAddress, GETUTCDATE())",
                    new
                    {
                        UserID = userId,
                        CompanyID = companyId,
                        Action = action,
                        EntityType = entityType,
                        EntityID = entityId,
                        Details = details,
                        IPAddress = ipAddress
                    });
            });
        }

        #endregion

        #region Blocked Domains Operations

        /// <summary>
        /// Checks if an email domain is blocked
        /// </summary>
        public async Task<bool> IsEmailDomainBlockedAsync(string domain)
        {
            return await WithConnectionAsync(async conn =>
            {
                var blocked = await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT Domain FROM BlockedEmailDomains WHERE Domain = @Domain",
                    new { Domain = domain.ToLowerInvariant() });

                return blocked != null;
            });
        }

        #endregion
    }
}