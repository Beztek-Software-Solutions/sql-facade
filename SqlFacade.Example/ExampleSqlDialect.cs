// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System;
    using System.Globalization;
    using Beztek.Facade.Sql;

    /// <summary>
    /// Minimal application dialect helper used by this sample.
    /// <para>
    /// SQLKata (via SqlFacade) handles structural dialect differences (quoting, LIMIT, placeholders).
    /// Expression-level differences — <c>NOW()</c>, boolean bind values, casts — stay in an app-owned
    /// helper so the same SQL generators can run against SQLite in tests and Postgres/SQL Server in production.
    /// Flip <see cref="UseSqlite"/> to match <see cref="SqlFacadeConfig.DbType"/> at startup.
    /// </para>
    /// </summary>
    public static class ExampleSqlDialect
    {
        /// <summary>When true, emit SQLite fragments; when false, emit Postgres-style fragments.</summary>
        public static bool UseSqlite { get; set; }

        /// <summary>Current UTC timestamp expression for raw predicates.</summary>
        public static string Now =>
            UseSqlite ? "datetime('now')" : "now()";

        /// <summary>
        /// Boolean bind value for <see cref="Expression"/> / <see cref="Field"/>.
        /// SQLite typically stores 0/1; Postgres uses true/false.
        /// </summary>
        public static object BooleanValue(bool value) =>
            UseSqlite ? (value ? 1 : 0) : value;

        /// <summary>Cast a column/expression to text for portable selects.</summary>
        public static string CastToText(string expression) =>
            UseSqlite ? $"CAST({expression} AS TEXT)" : $"{expression}::text";

        /// <summary>
        /// NestedList-safe bool select field. SQLite needs a CASE so JSON maps cleanly;
        /// Postgres can select the boolean column as-is.
        /// </summary>
        public static Field NestedListBool(string column, string alias) =>
            new Field(
                UseSqlite ? $"CASE WHEN {column} THEN 1 ELSE 0 END" : column,
                alias,
                isRaw: UseSqlite);

        /// <summary>
        /// Write value for a date column as invariant <c>yyyy-MM-dd</c> so SqlKata does not
        /// culture-format <see cref="DateOnly"/> (e.g. <c>9/14/2026</c>).
        /// </summary>
        public static object DateOnlyField(DateOnly value) =>
            value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
