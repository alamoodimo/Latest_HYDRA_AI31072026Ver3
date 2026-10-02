using DynamicDashboardCommon.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace DynamicDasboardWebAPI.Utilities
{
    /// <summary>
    /// Builds the compact schema text placed in LLM prompts.
    /// Tables whose names appear in <c>relevantText</c> (usually the current SQL and the title) are
    /// listed first, then the remaining active tables until <c>maxTables</c> is reached, so very
    /// large databases stay within the prompt budget. Every listed table shows ALL its active
    /// columns with their data types, so the AI never has to guess a column name.
    /// Shared by ComponentSqlRepairService and AssistantService.
    /// </summary>
    public static class SchemaPromptBuilder
    {
        public static string Build(DatabaseSchema schema, string relevantText, int maxTables)
        {
            if (schema?.Tables == null)
            {
                return string.Empty;
            }

            var activeTables = schema.Tables
                .Where(t => t != null && t.IsActive && !string.IsNullOrWhiteSpace(t.DBName))
                .ToList();

            var searchText = relevantText ?? string.Empty;
            var referenced = activeTables
                .Where(t => Regex.IsMatch(searchText, $@"(?<![\w]){Regex.Escape(t.DBName)}(?![\w])", RegexOptions.IgnoreCase))
                .ToList();

            var ordered = referenced
                .Concat(activeTables.Except(referenced))
                .Take(Math.Max(1, maxTables))
                .ToList();

            var sb = new StringBuilder();
            foreach (var table in ordered)
            {
                var columns = (table.Columns ?? new List<ColumnSchema>())
                    .Where(c => c != null && c.IsActive && !string.IsNullOrWhiteSpace(c.DBName))
                    .Select(c => c.IsPrimaryKey ? $"{c.DBName} ({c.DataType}, PK)" : $"{c.DBName} ({c.DataType})");

                var friendly = !string.IsNullOrWhiteSpace(table.FriendlyName) &&
                               !string.Equals(table.FriendlyName, table.DBName, StringComparison.OrdinalIgnoreCase)
                    ? $" [{table.FriendlyName}]"
                    : string.Empty;

                sb.AppendLine($"- {table.DBName}{friendly}: {string.Join(", ", columns)}");
            }

            if (activeTables.Count > ordered.Count)
            {
                sb.AppendLine($"(+ {activeTables.Count - ordered.Count} more tables not listed)");
            }

            return sb.ToString();
        }
    }
}
