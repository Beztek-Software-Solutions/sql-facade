# SQL Facade

Unified .NET SQL facade (`Beztek.Facade.Sql`) over PostgreSQL, SQL Server, SQLite, MySQL, MariaDB, and Oracle, built on SQLKata and Dapper.

Source: https://github.com/Beztek-Software-Solutions/sql-facade

## Projects

| Project | Description |
|---------|-------------|
| [`SqlFacade/`](SqlFacade/) | Library package `Beztek.Facade.Sql` (see [SqlFacade/README.md](SqlFacade/README.md) for full API and query-building guidance) |
| [`SqlFacade.Tests/`](SqlFacade.Tests/) | NUnit unit tests + optional Testcontainers live suite (`SQLFACADE_LIVE_ENGINES`) |
| [`SqlFacade.Example/`](SqlFacade.Example/) | Runnable sample (`Program.cs`) demonstrating every major feature |

## Quick start

```bash
dotnet restore sql-facade.sln
dotnet build sql-facade.sln
dotnet test SqlFacade.Tests/Beztek.Facade.Sql.Test.csproj
```

With coverage (Coverlet; target ≥ 85% line coverage):

```bash
dotnet test SqlFacade.Tests/Beztek.Facade.Sql.Test.csproj \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=./coverage/ \
  /p:Include='[Beztek.Facade.Sql]*' \
  /p:Threshold=85 \
  /p:ThresholdType=line
```

### Live container tests

Optional Testcontainers suite under `SqlFacade.Tests/Live/`. Unset env → not discovered (default CI stays container-free).

Uses the **Docker Engine API**. Prefer **Podman** (rootless): the suite auto-detects `$XDG_RUNTIME_DIR/podman/podman.sock` and sets `DOCKER_HOST` — no `docker` CLI or alias needed. Docker Desktop still works if that socket is what your machine exposes.

Podman is **not** preinstalled on every OS (common on Linux; on macOS/Windows install [Podman Desktop](https://podman-desktop.io/) / a Podman machine). `sqlite` needs no container engine.

```bash
# One engine
SQLFACADE_LIVE_ENGINES=postgres \
  dotnet test SqlFacade.Tests/Beztek.Facade.Sql.Test.csproj --filter Category=Live

# Several engines
SQLFACADE_LIVE_ENGINES=postgres,mysql,mariadb \
  dotnet test SqlFacade.Tests/Beztek.Facade.Sql.Test.csproj --filter Category=Live

# Every engine (SQLite in-process + containers for the rest)
SQLFACADE_LIVE_ENGINES=all \
  dotnet test SqlFacade.Tests/Beztek.Facade.Sql.Test.csproj --filter Category=Live
```

Aliases: `postgres`/`pg`, `sqlserver`/`mssql`, `mysql`, `mariadb`/`maria`, `oracle`, `sqlite`, `all`. Oracle image cold-start is heavy.

Run the sample project:

```bash
dotnet run --project SqlFacade.Example/Beztek.Facade.Sql.Example.csproj
```

## NuGet

```bash
dotnet add package Beztek.Facade.Sql
```

See [SqlFacade/README.md](SqlFacade/README.md) for initialization samples, query objects, pagination, JSON round-trip, and `NestedList` child collections.

## Database engines

| Engine | `DbType` | Driver | Status |
|--------|----------|--------|--------|
| PostgreSQL | `POSTGRES` | Npgsql | Implemented |
| SQL Server | `SQLSERVER` | Microsoft.Data.SqlClient | Implemented |
| SQLite (file or in-memory) | `SQLITE` | Microsoft.Data.Sqlite | Implemented |
| MySQL | `MYSQL` | MySqlConnector | Implemented |
| MariaDB | `MARIADB` | MySqlConnector | Implemented (separate NestedList wrap) |
| Oracle | `ORACLE` | Oracle.ManagedDataAccess.Core | Implemented |

Local development can use SQLite (in-memory or file); production can switch engines via `SqlFacadeConfig` without changing application query objects.

**Do not treat MariaDB as MySQL.** They share SQLKata’s `MySqlCompiler` for quoting/`LIMIT`, but NestedList JSON nesting differs — see [Dialect quirks](SqlFacade/README.md#dialect-quirks) in the library README.

For expression-level differences the facade does not abstract (boolean literals, `NOW()`, casts, NestedList-safe JSON fields, and similar), keep an application **dialect helper** next to your SQL generators — a multi-engine consolidated sample (from AnchoredLove / Grasp / MemoryMark patterns) is in [Application dialect helpers](SqlFacade/README.md#application-dialect-helpers) and [`SqlFacade.Example/ExampleSqlDialect.cs`](SqlFacade.Example/ExampleSqlDialect.cs). That helper belongs in the **app**, not in the NuGet package.
