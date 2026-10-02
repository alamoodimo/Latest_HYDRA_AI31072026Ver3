namespace DynamicDashboardCommon.Enums
{
    /// <summary>
    /// What the AI should do when asked to regenerate a dashboard component.
    /// Not stored in the database. If it is ever persisted (e.g. in an audit of AI repairs),
    /// store the numeric value in an INT column so existing values stay stable.
    /// </summary>
    public enum ComponentRegenerationMode
    {
        /// <summary>
        /// Not sent by the caller (older clients). The API infers the mode from the error message.
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// Keep the component's title, purpose and type; correct its SQL so it runs and returns data.
        /// Used by "Fix with AI" on a failed or empty component.
        /// </summary>
        Repair = 1,

        /// <summary>
        /// Replace the component with a different one of the same type (the user wants something else).
        /// </summary>
        Alternative = 2
    }

    /// <summary>
    /// Outcome of checking a component's SQL against its database.
    /// Not stored in the database (same note as above applies if it is persisted later).
    /// </summary>
    public enum ComponentSqlProblem
    {
        /// <summary>The query runs and returns at least one row.</summary>
        None = 0,

        /// <summary>The database rejected the query (syntax, unknown column, timeout, ...).</summary>
        QueryFailed = 1,

        /// <summary>The query runs but returns no rows.</summary>
        NoRows = 2,

        /// <summary>The component has no SQL.</summary>
        MissingSql = 3,

        /// <summary>The SQL is not a single read-only SELECT statement.</summary>
        NotReadOnly = 4
    }
}
