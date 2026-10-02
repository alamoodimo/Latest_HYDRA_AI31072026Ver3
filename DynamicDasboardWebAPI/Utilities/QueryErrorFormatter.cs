using System.Data.Common;

namespace DynamicDasboardWebAPI.Utilities
{
    /// <summary>
    /// Turns failures from running a SQL query into messages a user can act on.
    /// Shared by QueryService (query execution) and ComponentSqlRepairService (SQL checks),
    /// so both report errors the same way.
    /// </summary>
    public static class QueryErrorFormatter
    {
        /// <summary>
        /// True for failures caused by running the SQL itself: a database error from any
        /// provider (SQL Server, PostgreSQL, MySQL, Oracle all derive from DbException),
        /// a timeout, or the wrapper DatabaseHelper throws around them.
        /// Other failures (configuration, programming errors) are not query failures.
        /// </summary>
        public static bool IsQueryExecutionFailure(Exception ex)
        {
            return FindException<DbException>(ex) != null
                || FindException<TimeoutException>(ex) != null
                || (ex is InvalidOperationException &&
                    ex.Message.StartsWith("Error executing query", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Builds a message a user can act on: the database's own error text
        /// (e.g. "Invalid column name 'Revenue'.") without technical prefixes, plus
        /// DatabaseHelper's provider-specific hint ("Note: ...") when there is one.
        /// The result is cut to <paramref name="maxLength"/> characters.
        /// </summary>
        public static string BuildMessage(Exception ex, int maxLength)
        {
            var dbException = FindException<DbException>(ex);
            var rootException = dbException ?? GetInnermostException(ex);
            var databaseText = (rootException.Message ?? string.Empty).Trim();

            string message;
            if (FindException<TimeoutException>(ex) != null ||
                databaseText.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                message = "The query took too long and was stopped. " +
                          "Try adding filters, aggregating the data or returning fewer rows.";
            }
            else if (dbException != null)
            {
                message = $"The database rejected the query: {databaseText}";
            }
            else
            {
                message = $"The query could not be run: {databaseText}";
            }

            var noteIndex = ex.Message?.IndexOf("Note:", StringComparison.Ordinal) ?? -1;
            if (noteIndex >= 0 && !message.Contains("Note:", StringComparison.Ordinal))
            {
                message += " " + ex.Message.Substring(noteIndex).Trim();
            }

            return maxLength > 0 && message.Length > maxLength
                ? message.Substring(0, maxLength) + "…"
                : message;
        }

        /// <summary>Returns the first exception of type T in the exception chain, or null.</summary>
        private static T FindException<T>(Exception ex) where T : Exception
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is T match)
                {
                    return match;
                }
            }
            return null;
        }

        private static Exception GetInnermostException(Exception ex)
        {
            var current = ex;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }
            return current;
        }
    }
}
