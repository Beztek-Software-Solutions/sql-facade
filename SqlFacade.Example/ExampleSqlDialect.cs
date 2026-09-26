// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System;
    using System.Globalization;
    using Beztek.Facade.Sql;

    /// <summary>
    /// Consolidated application dialect helper sketch.
    /// <para>
    /// SQLKata (via SqlFacade) handles structural dialect differences. Expression-level fragments —
    /// <c>NOW()</c>, boolean binds, casts, NestedList-safe JSON fields, UUID text projection —
    /// stay in an app-owned helper keyed off the same <see cref="DbType"/> as <see cref="SqlFacadeConfig"/>.
    /// </para>
    /// <para>
    /// Typical deployments use Postgres and flip to SQLite for tests. This sample also branches
    /// MySQL, MariaDB, SQL Server, and Oracle so the same generators can target any facade-supported
    /// engine. Domain-specific helpers (PostGIS geography, HTML-escaped concat, schema prefixes)
    /// belong in the application, not this library.
    /// </para>
    /// </summary>
    public static class ExampleSqlDialect
    {
        /// <summary>Set once at startup from <see cref="SqlFacadeConfig.DbType"/>.</summary>
        public static DbType Engine { get; set; } = DbType.POSTGRES;

        public static bool IsSqlite => Engine == DbType.SQLITE;

        /// <summary>Current UTC timestamp expression for raw predicates.</summary>
        public static string Now => Engine switch
        {
            DbType.SQLITE => "datetime('now')",
            DbType.SQLSERVER => "SYSUTCDATETIME()",
            DbType.MYSQL or DbType.MARIADB => "UTC_TIMESTAMP()",
            DbType.ORACLE => "SYS_EXTRACT_UTC(SYSTIMESTAMP)",
            _ => "now()" // Postgres
        };

        public static string CurrentDateUtc => Engine switch
        {
            DbType.SQLITE => "date('now')",
            DbType.SQLSERVER => "CAST(SYSUTCDATETIME() AS date)",
            DbType.MYSQL or DbType.MARIADB => "UTC_DATE()",
            DbType.ORACLE => "TRUNC(SYS_EXTRACT_UTC(SYSTIMESTAMP))",
            _ => "(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::date"
        };

        public static string DateOfTimestamp(string column) => Engine switch
        {
            DbType.SQLITE => $"date({column})",
            DbType.SQLSERVER => $"CAST({column} AS date)",
            DbType.MYSQL or DbType.MARIADB => $"DATE({column})",
            DbType.ORACLE => $"TRUNC({column})",
            _ => $"(({column} AT TIME ZONE 'UTC')::date)"
        };

        /// <summary>Boolean literal embedded in raw SQL.</summary>
        public static string Boolean(bool value) => Engine switch
        {
            DbType.SQLITE or DbType.MYSQL or DbType.MARIADB or DbType.SQLSERVER or DbType.ORACLE =>
                value ? "1" : "0",
            _ => value ? "true" : "false" // Postgres
        };

        /// <summary>Boolean bind value for <see cref="Expression"/> / <see cref="Field"/>.</summary>
        public static object BooleanValue(bool value) => Engine switch
        {
            DbType.SQLITE or DbType.MYSQL or DbType.MARIADB or DbType.ORACLE => value ? 1 : 0,
            // SQL Server bit + Postgres bool accept CLR bool.
            _ => value
        };

        /// <summary>
        /// Write value for a date column as invariant <c>yyyy-MM-dd</c> so SqlKata does not
        /// culture-format <see cref="DateOnly"/> (e.g. <c>9/14/2026</c>).
        /// </summary>
        public static object DateOnlyField(DateOnly value) =>
            value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static object DateOnlyField(DateOnly? value) =>
            value.HasValue ? DateOnlyField(value.Value) : null;

        public static string CastToText(string expression) => Engine switch
        {
            DbType.SQLITE => $"CAST({expression} AS TEXT)",
            DbType.MYSQL or DbType.MARIADB => $"CAST({expression} AS CHAR)",
            DbType.SQLSERVER => $"CAST({expression} AS nvarchar(max))",
            DbType.ORACLE => $"TO_CHAR({expression})",
            _ => $"{expression}::text"
        };

        public static string CastToInt(string expression) => Engine switch
        {
            DbType.SQLITE => $"CAST({expression} AS INTEGER)",
            DbType.MYSQL or DbType.MARIADB => $"CAST({expression} AS SIGNED)",
            DbType.SQLSERVER => $"CAST({expression} AS int)",
            DbType.ORACLE => $"TO_NUMBER({expression})",
            _ => $"{expression}::int"
        };

        public static string CastToBool(string column) => Engine switch
        {
            DbType.SQLITE or DbType.MYSQL or DbType.MARIADB or DbType.ORACLE =>
                $"CASE WHEN {column} THEN 1 ELSE 0 END",
            DbType.SQLSERVER => $"CASE WHEN {column} = 1 THEN 1 ELSE 0 END",
            _ => column // Postgres native bool
        };

        public static bool CastToBoolIsRaw => Engine != DbType.POSTGRES;

        /// <summary>Timestamp as NestedList-safe text (ISO-ish UTC).</summary>
        public static string CastToTimestamptzText(string column) => Engine switch
        {
            DbType.SQLITE => $"CAST({column} AS TEXT)",
            DbType.SQLSERVER => $"CONVERT(varchar(33), {column}, 127)",
            DbType.MYSQL or DbType.MARIADB => $"DATE_FORMAT({column}, '%Y-%m-%dT%H:%i:%s.%fZ')",
            DbType.ORACLE => $"TO_CHAR({column} AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.FF6\"Z\"')",
            _ => $"to_char(({column}) AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')"
        };

        public static string CastToDateText(string column) => Engine switch
        {
            DbType.SQLITE => $"CAST(date({column}) AS TEXT)",
            DbType.MYSQL or DbType.MARIADB => $"DATE_FORMAT({column}, '%Y-%m-%d')",
            DbType.SQLSERVER => $"CONVERT(varchar(10), {column}, 23)",
            DbType.ORACLE => $"TO_CHAR({column}, 'YYYY-MM-DD')",
            _ => $"({column})::text"
        };

        /// <summary>
        /// Guid write/filter value: native <see cref="Guid"/> on Postgres/SQL Server;
        /// invariant <c>D</c>-format text elsewhere (matches facade <c>Relation.In</c> Guid rules).
        /// </summary>
        public static object UuidValue(Guid value) => Engine switch
        {
            DbType.POSTGRES or DbType.SQLSERVER => value,
            _ => value.ToString("D")
        };

        public static string UuidAsText(string column) => Engine switch
        {
            DbType.POSTGRES => $"{column}::text",
            DbType.SQLSERVER => $"CONVERT(nvarchar(36), {column})",
            DbType.ORACLE => $"TO_CHAR({column})",
            _ => column // already text on SQLite / MySQL / MariaDB
        };

        public static bool UuidAsTextIsRaw =>
            Engine is DbType.POSTGRES or DbType.SQLSERVER or DbType.ORACLE;

        public static object TimestampField(DateTimeOffset value)
        {
            var utc = value.UtcDateTime;
            return Engine switch
            {
                DbType.SQLITE or DbType.MYSQL or DbType.MARIADB or DbType.ORACLE =>
                    utc.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            };
        }

        public static Field NestedListBool(string column, string alias) =>
            new Field(CastToBool(column), alias, CastToBoolIsRaw);

        public static Field NestedListDate(string column, string alias) =>
            new Field(CastToDateText(column), alias, isRaw: true);

        public static Field NestedListTimestamptz(string column, string alias) =>
            new Field(CastToTimestamptzText(column), alias, isRaw: true);

        public static Field NestedListInt(string column, string alias) =>
            new Field(CastToInt(column), alias, isRaw: true);

        public static Field NestedListUuid(string column, string alias) =>
            new Field(UuidAsText(column), alias, UuidAsTextIsRaw);
    }
}
