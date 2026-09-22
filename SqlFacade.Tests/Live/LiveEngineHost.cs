// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System;
    using System.Data;
    using System.IO;
    using System.Threading.Tasks;
    using Beztek.Facade.Sql;
    using DotNet.Testcontainers.Containers;
    using Testcontainers.MariaDb;
    using Testcontainers.MsSql;
    using Testcontainers.MySql;
    using Testcontainers.Oracle;
    using Testcontainers.PostgreSql;
    using SqlDbType = Beztek.Facade.Sql.DbType;

    /// <summary>
    /// Starts (or skips) a throwaway engine for live tests and exposes a configured <see cref="ISqlFacade"/>.
    /// SQLite uses an in-memory keep-alive DB; other engines use Testcontainers.
    /// </summary>
    public sealed class LiveEngineHost : IAsyncDisposable
    {
        private readonly IContainer _container;
        private readonly string _sqlitePath;
        private readonly bool _deleteSqlitePath;

        private LiveEngineHost(SqlDbType dbType, ISqlFacade sql, IContainer container, string sqlitePath, bool deleteSqlitePath)
        {
            DbType = dbType;
            Sql = sql;
            _container = container;
            _sqlitePath = sqlitePath;
            _deleteSqlitePath = deleteSqlitePath;
        }

        public SqlDbType DbType { get; }

        public ISqlFacade Sql { get; }

        public static async Task<LiveEngineHost> StartAsync(SqlDbType dbType)
        {
            if (dbType != SqlDbType.SQLITE)
                LiveContainerRuntime.EnsureConfigured();

            try
            {
                return dbType switch
                {
                    SqlDbType.SQLITE => StartSqlite(),
                    SqlDbType.POSTGRES => await StartPostgresAsync().ConfigureAwait(false),
                    SqlDbType.SQLSERVER => await StartSqlServerAsync().ConfigureAwait(false),
                    SqlDbType.MYSQL => await StartMySqlAsync().ConfigureAwait(false),
                    SqlDbType.MARIADB => await StartMariaDbAsync().ConfigureAwait(false),
                    SqlDbType.ORACLE => await StartOracleAsync().ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unsupported engine"),
                };
            }
            catch (Exception ex) when (IsDockerUnavailable(ex))
            {
                throw new InvalidOperationException(
                    $"Cannot start {dbType}: no container engine API available. " +
                    "Install Podman (preferred) or Docker, ensure the engine is running " +
                    $"(Podman socket typically at $XDG_RUNTIME_DIR/podman/podman.sock), then re-run with {LiveEngineSelection.EnvVar} set.",
                    ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_container != null)
                await _container.DisposeAsync().ConfigureAwait(false);

            if (_deleteSqlitePath && !string.IsNullOrEmpty(_sqlitePath) && File.Exists(_sqlitePath))
            {
                try { File.Delete(_sqlitePath); }
                catch { /* best-effort */ }
            }
        }

        private static LiveEngineHost StartSqlite()
        {
            // File-backed so DDL + facade calls share durable storage; :memory: also works via keep-alive.
            string path = Path.Combine(Path.GetTempPath(), $"sql-facade-live-{Guid.NewGuid():N}.db");
            var config = new SqlFacadeConfig(SqlDbType.SQLITE, $"Data Source={path}");
            ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
            LiveSchema.Ensure(sql);
            return new LiveEngineHost(SqlDbType.SQLITE, sql, container: null, sqlitePath: path, deleteSqlitePath: true);
        }

        private static async Task<LiveEngineHost> StartPostgresAsync()
        {
            PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("sqlfacade")
                .WithUsername("sqlfacade")
                .WithPassword("sqlfacade")
                .Build();
            await container.StartAsync().ConfigureAwait(false);
            return CreateHost(SqlDbType.POSTGRES, container.GetConnectionString(), container);
        }

        private static async Task<LiveEngineHost> StartSqlServerAsync()
        {
            MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword("SqlFacade_Test_1!")
                .Build();
            await container.StartAsync().ConfigureAwait(false);
            return CreateHost(SqlDbType.SQLSERVER, container.GetConnectionString(), container);
        }

        private static async Task<LiveEngineHost> StartMySqlAsync()
        {
            MySqlContainer container = new MySqlBuilder("mysql:8.0")
                .WithDatabase("sqlfacade")
                .WithUsername("sqlfacade")
                .WithPassword("sqlfacade")
                .Build();
            await container.StartAsync().ConfigureAwait(false);
            return CreateHost(SqlDbType.MYSQL, container.GetConnectionString(), container);
        }

        private static async Task<LiveEngineHost> StartMariaDbAsync()
        {
            MariaDbContainer container = new MariaDbBuilder("mariadb:10.11")
                .WithDatabase("sqlfacade")
                .WithUsername("sqlfacade")
                .WithPassword("sqlfacade")
                .Build();
            await container.StartAsync().ConfigureAwait(false);
            return CreateHost(SqlDbType.MARIADB, container.GetConnectionString(), container);
        }

        private static async Task<LiveEngineHost> StartOracleAsync()
        {
            OracleContainer container = new OracleBuilder("gvenzl/oracle-xe:21-slim-faststart")
                .WithPassword("sqlfacade")
                .Build();
            await container.StartAsync().ConfigureAwait(false);
            return CreateHost(SqlDbType.ORACLE, container.GetConnectionString(), container);
        }

        private static LiveEngineHost CreateHost(SqlDbType dbType, string connectionString, IContainer container)
        {
            var config = new SqlFacadeConfig(dbType, connectionString);
            ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
            LiveSchema.Ensure(sql);
            return new LiveEngineHost(dbType, sql, container, sqlitePath: null, deleteSqlitePath: false);
        }

        private static bool IsDockerUnavailable(Exception ex)
        {
            for (Exception cur = ex; cur != null; cur = cur.InnerException)
            {
                string msg = cur.Message ?? "";
                string type = cur.GetType().FullName ?? "";
                if (type.Contains("DockerUnavailable", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Cannot connect to the Docker", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("docker.sock", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("The Docker daemon", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
