# SQL Facade

Unified .NET SQL facade (`Beztek.Facade.Sql`) over PostgreSQL, SQL Server, and SQLite, built on SQLKata and Dapper.

Source: https://github.com/Beztek-Software-Solutions/sql-facade

## Projects

| Project | Description |
|---------|-------------|
| [`SqlFacade/`](SqlFacade/) | Library package `Beztek.Facade.Sql` (see [SqlFacade/README.md](SqlFacade/README.md) for full API and query-building guidance) |
| [`SqlFacade.Tests/`](SqlFacade.Tests/) | NUnit unit tests |
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

| Engine | `DbType` | Status |
|--------|----------|--------|
| PostgreSQL | `POSTGRES` | Implemented |
| SQL Server | `SQLSERVER` | Implemented |
| SQLite (file or in-memory) | `SQLITE` | Implemented |

Local development can use SQLite (in-memory or file); production can use managed Postgres or SQL Server without changing application query code.

For expression-level differences the facade does not abstract (boolean literals, `NOW()`, casts, NestedList-safe JSON fields, and similar), keep an application **dialect helper** next to your SQL generators — see [Application dialect helpers](SqlFacade/README.md#application-dialect-helpers) in the library README.
