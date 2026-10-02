namespace DynamicDasboardWebAPI.Utilities
{
    /// <summary>
    /// SQL syntax rules per database type, added to LLM prompts so generated SQL matches the
    /// target database (SQL Server, MySQL, PostgreSQL, Oracle).
    /// Single shared copy: AssistantService and DashboardGenerationService each had an identical
    /// private GetDbSyntaxGuidance method; both switch to this class in the next steps.
    /// </summary>
    public static class SqlDialectGuidance
    {
        /// <summary>
        /// Returns the syntax rules for a database type name (e.g. "SQL Server", "PostgreSQL").
        /// Unknown or empty names fall back to SQL Server.
        /// </summary>
        public static string For(string databaseType)
        {
            var dbType = databaseType?.ToLower() ?? "sql server";

            if (dbType.Contains("mysql"))
            {
                return @"**MySQL Syntax Rules - YOU MUST FOLLOW:**
- Use `LIMIT N` to restrict rows (e.g., `SELECT * FROM table LIMIT 10`)
- Use `DATE_FORMAT(date, '%Y-%m')` for date formatting
- Use `NOW()` or `CURDATE()` for current date/time
- Use `IFNULL(column, default)` for null handling
- Use `CONCAT(str1, str2)` for string concatenation
- Use `YEAR(date)`, `MONTH(date)`, `DAY(date)` for date parts
- Use `DATEDIFF(date1, date2)` for date difference (returns days)
- Boolean: Use `TRUE`/`FALSE` or `1`/`0`
- Use `DATE_ADD(date, INTERVAL 1 DAY)` for date arithmetic
- DO NOT use: TOP, GETDATE(), ISNULL(), FORMAT()";
            }
            else if (dbType.Contains("oracle"))
            {
                return @"**Oracle Syntax Rules - YOU MUST FOLLOW:**
- Use `FETCH FIRST N ROWS ONLY` to restrict rows
- Or use `WHERE ROWNUM <= N` for older Oracle versions
- Use `TO_CHAR(date, 'YYYY-MM')` for date formatting
- Use `SYSDATE` for current date/time
- Use `NVL(column, default)` for null handling
- Use `||` for string concatenation
- Use `EXTRACT(YEAR FROM date)` for date parts
- Use `ADD_MONTHS(date, 1)` for date arithmetic
- Every SELECT must have FROM (use `FROM DUAL` for constants)
- String literals: Use single quotes only
- DO NOT use: TOP, LIMIT, GETDATE(), ISNULL(), DATE_FORMAT()";
            }
            else if (dbType.Contains("postgres"))
            {
                return @"**PostgreSQL Syntax Rules - YOU MUST FOLLOW:**
- Use `LIMIT N` to restrict rows (e.g., `SELECT * FROM table LIMIT 10`)
- Use `TO_CHAR(date, 'YYYY-MM')` for date formatting
- Use `NOW()` or `CURRENT_DATE` for current date/time
- Use `COALESCE(column, default)` for null handling
- Use `||` or `CONCAT(str1, str2)` for string concatenation
- Use `EXTRACT(YEAR FROM date)` or `DATE_PART('year', date)` for date parts
- Boolean: Use `TRUE`/`FALSE`
- Use `INTERVAL '1 day'` for date arithmetic (e.g., `date + INTERVAL '1 day'`)
- Use `::` for type casting (e.g., `'2023-01-01'::date`)
- Use `ILIKE` for case-insensitive pattern matching
- String literals: Use single quotes only
- DO NOT use: TOP, GETDATE(), ISNULL(), FORMAT(), + for string concatenation
- JSON support: Use `->>` for JSON extraction, `@?` for JSON path queries";
            }
            else // Default: SQL Server
            {
                return @"**SQL Server Syntax Rules - YOU MUST FOLLOW:**
- Use `TOP N` to restrict rows (e.g., `SELECT TOP 10 * FROM table`)
- Use `FORMAT(date, 'yyyy-MM')` for date formatting
- Use `GETDATE()` for current date/time
- Use `ISNULL(column, default)` for null handling
- Use `+` or `CONCAT(str1, str2)` for string concatenation
- Use `YEAR(date)`, `MONTH(date)`, `DAY(date)` for date parts
- Use `DATEDIFF(day, date1, date2)` for date difference
- Use `DATEADD(day, 1, date)` for date arithmetic
- Boolean: Use `1`/`0` (no TRUE/FALSE)
- String literals: Use single quotes only
- Use `LIKE` with `%` wildcard
- DO NOT use: LIMIT, NOW(), IFNULL(), DATE_FORMAT(), NVL(), FETCH FIRST";
            }
        }
    }
}
