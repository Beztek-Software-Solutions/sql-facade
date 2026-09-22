// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Beztek.Facade.Sql;

    /// <summary>
    /// Selects which engines run live container tests via <c>SQLFACADE_LIVE_ENGINES</c>.
    /// <para>
    /// Examples:
    /// <list type="bullet">
    /// <item><c>postgres</c> — one engine</item>
    /// <item><c>mysql,mariadb</c> — subset</item>
    /// <item><c>all</c> — every supported engine (SQLite in-process + containers for the rest)</item>
    /// </list>
    /// Unset / empty → no live fixtures are registered (default unit CI stays fast and Docker-free).
    /// </para>
    /// </summary>
    public static class LiveEngineSelection
    {
        public const string EnvVar = "SQLFACADE_LIVE_ENGINES";

        private static readonly DbType[] AllEngines =
        {
            DbType.SQLITE,
            DbType.POSTGRES,
            DbType.SQLSERVER,
            DbType.MYSQL,
            DbType.MARIADB,
            DbType.ORACLE,
        };

        /// <summary>True when the env var is set to a non-empty value.</summary>
        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar));

        /// <summary>Engines requested for this process; empty when live tests should not run.</summary>
        public static IReadOnlyList<DbType> Resolve()
        {
            string raw = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<DbType>();

            var selected = new List<DbType>();
            foreach (string token in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.Trim().ToLowerInvariant();
                if (t is "all" or "*")
                    return AllEngines.ToList();

                if (TryParse(t, out DbType dbType) && !selected.Contains(dbType))
                    selected.Add(dbType);
                else
                    throw new ArgumentException(
                        $"Unknown engine '{token}' in {EnvVar}. " +
                        "Use: all | sqlite | postgres | sqlserver | mysql | mariadb | oracle " +
                        "(comma-separated for a subset).");
            }

            return selected;
        }

        public static bool TryParse(string token, out DbType dbType)
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "sqlite":
                    dbType = DbType.SQLITE;
                    return true;
                case "postgres":
                case "postgresql":
                case "pg":
                    dbType = DbType.POSTGRES;
                    return true;
                case "sqlserver":
                case "mssql":
                case "sql":
                    dbType = DbType.SQLSERVER;
                    return true;
                case "mysql":
                    dbType = DbType.MYSQL;
                    return true;
                case "mariadb":
                case "maria":
                    dbType = DbType.MARIADB;
                    return true;
                case "oracle":
                case "ora":
                    dbType = DbType.ORACLE;
                    return true;
                default:
                    dbType = default;
                    return false;
            }
        }
    }
}
