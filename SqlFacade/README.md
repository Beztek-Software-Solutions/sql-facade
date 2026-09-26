# SQL Facade library

## Introduction

`Beztek.Facade.Sql` is a database-portable SQL facade for .NET. Services build queries with typed objects (`SqlSelect`, `SqlInsert`, …) instead of hand-written SQL strings; the library compiles them per dialect via SQLKata and executes with Dapper.

Use SQLite for offline development and switch to PostgreSQL, SQL Server, MySQL, MariaDB, or Oracle in production by changing `SqlFacadeConfig` only.

## Core API (`ISqlFacade`)

| Method | Behavior |
|--------|----------|
| `GetSqlFacadeConfig` | Returns the config used to create this instance |
| `GetResults<T>` | Execute a `SqlSelect` and map rows to `T` |
| `GetSingleResult<T>` | One row or `default(T)`; throws if more than one row |
| `GetTotalNumResults` | Count rows matching a `SqlSelect` (ignores sort/pagination) |
| `GetPagedResults<T>` | Page of results; optional total count via `PagedResultsWithTotal<T>` |
| `ExecuteSqlWrite` | Run `SqlInsert`, `SqlUpdate`, or `SqlDelete`; returns rows affected |
| `ExecuteMultiSqlWrite` | Same transaction, sequential writes; returns rows affected per statement |
| `GetSql` | Compile any `ISql` to dialect SQL (raw or parameterized) |
| `DeserializeFromJson` | Rehydrate `ISql` from JSON produced by `ToString()` |

Obtain instances via `SqlFacadeFactory.GetSqlFacade`. The factory caches one `SqlFacade` per equal `SqlFacadeConfig`.

## Initializing the facade

### SQLite (in-memory — ideal for tests and local dev)

```csharp
var config = new SqlFacadeConfig(DbType.SQLITE, "Data Source=:memory:");
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
```

### SQLite (file)

```csharp
var config = new SqlFacadeConfig(DbType.SQLITE, "Data Source=/tmp/app.db");
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
```

### PostgreSQL

```csharp
var config = new SqlFacadeConfig(
    DbType.POSTGRES,
    "Host=localhost;Database=mydb;Username=app;Password=secret");
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
```

**RDS IAM authentication:** this library does not generate IAM auth tokens.
The host must put a (short-lived) token in the connection-string `Password`
(or equivalent) before constructing `SqlFacadeConfig`, and refresh it as needed.

### SQL Server

```csharp
var config = new SqlFacadeConfig(
    DbType.SQLSERVER,
    "Server=localhost;Database=mydb;Trusted_Connection=True;");
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
```

### MySQL

```csharp
var config = new SqlFacadeConfig(
    DbType.MYSQL,
    "Server=localhost;Port=3306;Database=mydb;User ID=app;Password=secret");
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
```

### MariaDB

Use `DbType.MARIADB` even though the wire protocol/driver matches MySQL — NestedList JSON nesting is compiled differently (see [Dialect quirks](#dialect-quirks)).

```csharp
var config = new SqlFacadeConfig(
    DbType.MARIADB,
    "Server=localhost;Port=3306;Database=mydb;User ID=app;Password=secret");
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
```

### Oracle

```csharp
var config = new SqlFacadeConfig(
    DbType.ORACLE,
    "User Id=app;Password=secret;Data Source=localhost:1521/XEPDB1");
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
```

### Transaction isolation

Every call runs inside a `TransactionScope` with **`TransactionScopeOption.Required`** — it joins an ambient outer scope when one exists, otherwise it creates one. `SqlFacadeConfig.TransactionIsolationLevel` controls isolation (default **ReadCommitted**). Set `Serializable` only when callers truly need serializable semantics.

```csharp
var config = new SqlFacadeConfig(DbType.POSTGRES, connectionString)
{
    TransactionIsolationLevel = System.Transactions.IsolationLevel.ReadCommitted
};
```

To commit or roll back several facade calls together, wrap them in an outer scope (the facade joins it):

```csharp
using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
sql.ExecuteSqlWrite(...);
sql.GetResults<T>(...);
scope.Complete();
```

File-backed SQLite is opened on each call like the other engines. **Microsoft.Data.Sqlite does not implement ambient `EnlistTransaction`**, so file SQLite does not join `TransactionScope` the way Postgres/SQL Server/MySQL/Oracle do. In-memory SQLite keeps a shared connection alive for the process (required so `:memory:` survives across calls) and is not re-enlisted on every call.

## Query model

Queries are built fluently and serialized to JSON for logging, APIs, or cache search (used by `Beztek.Facade.Cache`).

### `SqlSelect`

```csharp
var select = new SqlSelect(new Table("orders", "o"))
    .WithField(new Field("o.id", "Id"))
    .WithField(new Field("o.total", "Total"))
    .WithJoin(new Join(new Table("customers", "c"), new Expression("c.id", "o.customer_id")))
    .WithWhere(new Filter().WithExpression(new Expression("o.status", "open")))
    .WithSort(new Sort("o.created_at", ascending: false));
```

| Property / method | Purpose |
|-------------------|---------|
| `Table` / `FromDerivedTable` | FROM table or subquery |
| `WithCommonTableExpression` | CTE (`WITH` clause) |
| `WithField` | SELECT list; alias maps to DTO property |
| `WithNestedList` | Correlated 1:N child list (see below) |
| `WithJoin` | Inner / left join (table, derived table, or CTE) |
| `WithWhere` | Row filter |
| `WithGroupBy` / `WithHaving` | Aggregation |
| `WithSort` | ORDER BY |
| `WithCombine` | UNION / UNION ALL / EXCEPT / INTERSECT |

### `Filter` and `Expression`

Filters combine expressions with `LogicalRelation`: `And` (default), `Or`, `AndNot`, `OrNot`. Nest filters for parentheses.

```csharp
var filter = new Filter()
    .WithExpression(new Expression("status", "open"))
    .WithExpression(new Expression("total", 100)
        .WithRelation(Relation.GreaterThan)
        .WithLogicalRelation(LogicalRelation.And));
```

| `Relation` | Meaning |
|------------|---------|
| `EqualTo`, `GreaterThan`, `LessThan`, … | Comparison |
| `In` | Value in list or subquery (`WithSqlIn`). Lists may be `string`, numeric, or `Guid` / `Guid[]` / `List<Guid>`. **Postgres** and **SQL Server** bind `Guid` natively (`uuid` / `uniqueidentifier`); **SQLite, MySQL, MariaDB, Oracle** fall back to invariant `D`-format text (e.g. `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`). |
| `NullValue` / negation | IS NULL / IS NOT NULL |
| `TrueValue` | Raw boolean (`WithIsRaw()`) |
| `Exists` | Subquery exists (`WithSqlExists`) |
| `StartsWith`, `EndsWith`, `Contains` | String match |

Raw SQL predicates:

```csharp
filter.WithRawExpression("date(created_at) = date('now')");
// or
new Expression("count(*) > 1", null).WithIsRaw()
```

### Writes

```csharp
// Insert
new SqlInsert("orders")
    .WithField(new Field("id", orderId))
    .WithField(new Field("total", 99.50m));

// Insert … SELECT
new SqlInsert("orders_archive").WithQuery(
    new SqlSelect("orders").WithField(new Field("id")).WithWhere(...));

// Update
new SqlUpdate("orders")
    .WithField(new Field("status", "shipped"))
    .WithFilter(new Expression("id", orderId));

// Delete
new SqlDelete("orders").WithFilter(new Expression("status", "cancelled"));
```

Batch writes in one transaction:

```csharp
IList<int> rows = sql.ExecuteMultiSqlWrite(new List<ISqlWrite> { insert1, insert2, update1 });
```

### Common table expressions (CTE)

```csharp
var cte = new CommonTableExpression(
    new SqlSelect(new Table("orders"))
        .WithField(new Field("customer_id"))
        .WithField(new Field("sum(total)", "spent", isRaw: true)),
    "totals");

var select = new SqlSelect("totals")
    .WithCommonTableExpression(cte)
    .WithWhere(new Filter().WithExpression(new Expression("spent", 1000).WithRelation(Relation.GreaterThan)));
```

Raw CTE SQL is also supported:

```csharp
new CommonTableExpression("select 'red' as color", "palette")
```

### Derived tables

Wrap any `SqlSelect` as a subquery source:

```csharp
var inner = new SqlSelect(new Table("orders")).WithField(new Field("id")).WithField(new Field("total"));
var select = new SqlSelect(new DerivedTable(inner, "v")).WithField(new Field("v.total"));
```

### Joins

```csharp
// Simple inner join
new Join(new Table("line_items", "li"), new Expression("li.order_id", "o.id"))

// Left join with extra ON predicate
new Join(new Table("metadata", "m"), new Expression("m.id", "o.id"), JoinType.LeftJoin)
    .WithJoinExpression(new Expression("m.kind", "shipping"))
```

Join a CTE or derived table via `Join(DerivedTable, Expression, JoinType)`.

### Set operations (`SqlCombine`)

```csharp
var greens = new SqlSelect("products").WithField(new Field("sku"))
    .WithWhere(new Filter().WithExpression(new Expression("color", "green")));

var select = new SqlSelect("products")
    .WithField(new Field("sku"))
    .WithWhere(new Filter().WithExpression(new Expression("discontinued", true)))
    .WithCombine(new SqlCombine(greens, SqlRelation.UnionAll))
    .WithSort(new Sort("sku"));
```

When a select has both `SqlCombine` and `WithSort`, the facade wraps the union in a derived table so `ORDER BY` applies to the combined result (SQLKata otherwise attaches sort to the first branch).

Supported relations: `Union`, `UnionAll`, `Except`, `Intersect`.

### Pagination

```csharp
PagedResultsWithTotal<Order> page = (PagedResultsWithTotal<Order>)sql.GetPagedResults<Order>(
    select, pageNum: 2, pageSize: 25, retrieveTotalNumResults: true);

Console.WriteLine($"{page.PagedList.Count} of {page.TotalResults} (page {page.PageNum}/{page.TotalPages})");
```

`GetTotalNumResults(select)` returns the full count without fetching rows.

### JSON serialization

Every query object implements `ToString()` as JSON. Round-trip for APIs or persisted search:

```csharp
string json = select.ToString();
var restored = (SqlSelect)sql.DeserializeFromJson(json);
```

Supported types: `SqlSelect`, `SqlInsert`, `SqlUpdate`, `SqlDelete`.

## Nested lists (`NestedList`)

For 1:N child collections on a parent row, attach a child **`SqlSelect`** via `WithNestedList`. The facade emits a dialect-specific JSON array aggregate, then **maps it onto a typed list property** on the parent DTO (`List<T>`, `IList<T>`, or `T[]`). The property name must match the `NestedList` result alias.

| Engine | Aggregate SQL |
|--------|---------------|
| **Postgres** | `json_agg(row_to_json(...))` via `json_build_array()` empty fallback |
| **SQLite** | `json_group_array(json_object(...))` with `json(...)` for grandchildren |
| **SQL Server** | `JSON_QUERY((… FOR JSON PATH …))` |
| **MySQL** | `JSON_ARRAYAGG(JSON_OBJECT(…))` with `CAST(… AS JSON)` for grandchildren |
| **MariaDB** | Same shape, `JSON_EXTRACT(…, '$')` for grandchildren (not the MySQL cast) |
| **Oracle** | `JSON_SERIALIZE(JSON_ARRAYAGG(… ORDER BY … RETURNING CLOB))` |

```csharp
public class OrderRow
{
    public string Id { get; set; }
    public List<LineItemDto> Items { get; set; }  // filled automatically
}

var select = new SqlSelect(new Table("orders", "o"))
    .WithField(new Field("o.id", "Id"))
    .WithNestedList(
        new NestedList<LineItemDto>("Items",
            new SqlSelect(new Table("line_items", "li"))
                .WithField(new Field("li.id", "id"))
                .WithField(new Field("li.qty", "qty"))
                .WithSort(new Sort("li.line_no")),
            new Expression("li.order_id", "o.id")));

IList<OrderRow> rows = sql.GetResults<OrderRow>(select);
// rows[0].Items is already List<LineItemDto>
```

**Requirements**

- Child `Fields` are required (alias = JSON property name).
- `Correlate` is required: Join-style ON `Expression` (both sides columns) or a `Filter`.
- Grandchild nested lists are supported (nested `WithNestedList` on the child select).

Complex correlate with `Filter`:

```csharp
new NestedList<LineItemDto>("Items", childSelect,
    new Filter()
        .WithExpression(new Expression("li.order_id", "o.id"))
        .WithExpression(new Expression("li.active", "o.active")
            .WithLogicalRelation(LogicalRelation.And)));
```

Inspect dialect SQL without executing:

```csharp
string pgSql = nestedList.ToSql(DbType.POSTGRES);
string mysqlSql = nestedList.ToSql(DbType.MYSQL);
string mariaSql = nestedList.ToSql(DbType.MARIADB);
string oracleSql = nestedList.ToSql(DbType.ORACLE);
```

See the aggregate table above for per-engine NestedList shapes (including empty-array and grandchild handling).

### Type mapping notes

`NestedListMapper` parses JSON child arrays with flexible converters for `DateTime` (offset-less SQLite/Postgres text treated as UTC), `DateOnly`, `bool`, and `decimal`. Parent scalar columns map to DTO properties by name (case-insensitive).

## Dialect quirks

Structural SQL (identifier quoting, `LIMIT`/`OFFSET`, placeholders) comes from SQLKata. The following are **facade-specific or engine gotchas** to plan for when choosing an engine or writing app dialect helpers.

### Minimum versions (practical floors)

| Engine | Suggested floor | Why |
|--------|-----------------|-----|
| MySQL | **8.0.31+** | CTEs (8.0+); `EXCEPT` / `INTERSECT` (8.0.31+); NestedList needs `JSON_ARRAYAGG` / `JSON_OBJECT` |
| MariaDB | **10.5+** (10.3+ for set ops) | CTEs earlier; `EXCEPT` / `INTERSECT` from 10.3; NestedList JSON nesting fixes behave best on recent 10.5/10.6+ |
| Oracle | **19c+** | `JSON_OBJECT` / `JSON_ARRAYAGG` with `RETURNING CLOB` / `FORMAT JSON`; SqlKata uses `OFFSET … FETCH` (12c+) |

Older servers may compile SQL that fails at runtime.

### NestedList / JSON gotchas (verified live)

- **SqlKata `SelectRaw` mangles `[]`**: a literal empty JSON array in NestedList SQL becomes `""` when embedded via `SelectRaw`. Postgres uses `json_build_array()`; SQL Server / Oracle use `CHR(91)||CHR(93)` (or `CHAR(91)+CHAR(93)`); SQLite/MySQL/MariaDB use `json_array()` / `JSON_ARRAY()`.
- **MariaDB rejects outer refs in derived tables**: NestedList aggregates over the child `FROM`/`WHERE` directly (no `FROM (subquery) AS _j`). MySQL often tolerates the derived-table form; MariaDB does not — another reason `DbType.MARIADB` must stay separate.
- **SQL Server nested `FOR JSON`**: grandchild NestedLists wrap with `JSON_QUERY(...)` so nested arrays stay JSON inside the parent `FOR JSON PATH` (otherwise they become escaped strings).
- **Oracle NestedList**: `ORDER BY` must sit inside `JSON_ARRAYAGG(... ORDER BY ...)`; a trailing `ORDER BY` on the scalar subquery is `ORA-00907`. Results are `JSON_SERIALIZE(... RETURNING VARCHAR2)`; empty arrays use `CHR(91)||CHR(93)`. Correlate identifiers are dialect-quoted (`"ch"."parent_id" = "p"."id"`).
- NestedList returns serialized JSON text to Dapper for `NestedListMapper` (Oracle via `JSON_SERIALIZE`; others as JSON/text columns).
- **`GetPagedResults(..., retrieveTotalNumResults: true)`**: the count path strips `ORDER BY` / NestedLists before wrapping in a CTE — SQL Server rejects `ORDER BY` inside that CTE without `TOP`/`OFFSET`.

### MySQL vs MariaDB (do not collapse them)

| Topic | MySQL | MariaDB |
|-------|-------|---------|
| SQLKata compiler | `MySqlCompiler` (backticks, `LIMIT`) | **Same** compiler |
| `DbType` | `MYSQL` | **`MARIADB`** — pick this deliberately |
| JSON column type | Native binary `JSON` | Alias for `LONGTEXT` + validation |
| NestedList grandchildren | `CAST(col AS JSON)` inside `JSON_OBJECT` | `JSON_EXTRACT(col, '$')` so nested arrays are not double-escaped |
| Replication / dumps | Binary JSON not interchangeable with MariaDB `JSON` without conversion | Treat as text JSON |

**Watch out:** pointing a MariaDB instance at `DbType.MYSQL` (or the reverse) will often “work” for flat queries and then silently corrupt **grandchild NestedList** JSON (escaped string instead of array). Always match `DbType` to the real server.

### Oracle

- Pagination uses `OFFSET n ROWS FETCH NEXT m ROWS ONLY` (SqlKata). Queries without `ORDER BY` may get a synthetic order for safe fetch.
- Multi-row `INSERT` can compile to Oracle `INSERT ALL … SELECT 1 FROM DUAL`.
- NestedList returns serialized JSON text; prefer `JSON_SERIALIZE` / text mapping over assuming a native JSON CLR type from ODP.NET.
- Prefer `CHAR(36)` / `VARCHAR2(36)` for Guid columns if you use `Relation.In` with `Guid` lists (text bind).
- Container / CI images (`gvenzl/oracle-xe`, etc.) are heavier and slower than MySQL/Postgres — budget cold-start time for live tests.

### Guid / UUID columns

| Engine | `Relation.In` with `Guid` / `Guid[]` |
|--------|--------------------------------------|
| Postgres | Native `uuid` bind |
| SQL Server | Native `uniqueidentifier` bind |
| SQLite, MySQL, MariaDB, Oracle | Invariant `D`-format text (`aaaaaaaa-…`) |

Store UUID text consistently (`CHAR(36)` / `TEXT`) on the text engines, or convert explicitly in schema.

### Set operators and CTEs

`SqlCombine` supports `Union`, `UnionAll`, `Except`, `Intersect`. Availability:

- **Postgres / SQLite / SQL Server**: generally fine on supported versions.
- **MySQL**: `EXCEPT` / `INTERSECT` only from 8.0.31; prefer `Union`/`UnionAll` if you must support older 8.0.
- **MariaDB**: set ops from 10.3+.
- **Oracle**: historically `MINUS` instead of `EXCEPT`; SqlKata emits standard operators — verify against your Oracle version / compatibility settings.

Count-via-CTE (`GetTotalNumResults`) needs CTE support on the target engine (see floors above).

### Transactions

Every facade call wraps a `TransactionScope` with `Required` (joins an ambient outer scope) and enlists the ADO.NET connection where the provider supports it. **SQLite (Microsoft.Data.Sqlite) does not support ambient enlistment** — file connections are still opened; the in-memory keep-alive connection is shared across calls. MySqlConnector and ODP.NET Managed support ambient transactions, but:

- Distributed/`TransactionScope` + MySQL/MariaDB can require extra server/XA configuration depending on environment.
- Prefer an explicit outer `TransactionScope` in application code when coordinating multiple facade calls.

### Packages pulled in by the library

Adding MySQL/MariaDB/Oracle increases the NuGet dependency surface (`MySqlConnector`, `Oracle.ManagedDataAccess.Core`) even if your app only uses SQLite. That is intentional so one package can target any supported engine. Dapper arrives **transitively** via `SqlKata.Execution` (pinned there; not always the newest Dapper on NuGet).

### What the facade does *not* unify

Boolean literals, `NOW()` / `SYSDATE` / `UTC_TIMESTAMP()`, casts, `DateOnly` culture formatting, and NestedList-safe scalar selects still belong in an **application dialect helper** — see below.

## Dialect compilation (`GetSql`)

Compile without executing — useful for logging, review, or cross-dialect tests:

```csharp
string raw = sql.GetSql(select, isParameterized: false);
string bound = sql.GetSql(select, isParameterized: true);  // @p0, @p1, …
```

## Application dialect helpers

This facade lets you run **integration-style unit tests against SQLite in-memory** while deploying against any supported engine. Point `SqlFacadeConfig` at `:memory:` (or a temp file), set the app helper’s `Engine` to `DbType.SQLITE`, and exercise the same SQL generators, filters, NestedList mapping, and write paths your services use in production — without a real database in CI.

SQLKata (via this facade) already handles **structural** dialect differences: identifier quoting, `LIMIT`/`OFFSET`, parameterized placeholders, and similar. What it does **not** unify are **expression-level** fragments — boolean literals, `NOW()`, type casts, `DateOnly` writes, NestedList-safe JSON column selects, UUID text projection, PostGIS vs plain lat/lon, and so on.

Those belong in an **application-owned dialect helper** (not in this library). Applications typically keep a static `SqlDialect` next to their SQL generators and deploy Postgres while testing against SQLite (`UseSqlite` bool or equivalent). The consolidated sample below upgrades that pattern to a `DbType Engine` switch so the same generators can target MySQL, MariaDB, SQL Server, or Oracle as well. A runnable copy lives in [`SqlFacade.Example/ExampleSqlDialect.cs`](../SqlFacade.Example/ExampleSqlDialect.cs).

### Why a helper (and why it stays in the app)

| Layer | Responsibility |
|-------|----------------|
| `SqlFacade` / SQLKata | Compile `SqlSelect` / `SqlInsert` / … into dialect SQL for each `DbType` |
| App `SqlDialect` | Emit engine-specific **raw expressions**, **bind values**, and **Field** helpers used *inside* those query objects |

**Should this live in the NuGet library?** Generally **no**. The shared core (bools, casts, timestamps, NestedList field factories, Guid/DateOnly writes) is small and stable, but real apps also carry **domain-specific** helpers — PostGIS `ST_X` / WKT, HTML-escaped concat, schema-qualified table names, `AsyncLocal` overrides for parallel tests, and product-specific date rules. Baking those into `Beztek.Facade.Sql` would either omit what apps need or pull every product concern into the package. Prefer: copy the sample into each API (or a thin shared internal package owned by your org), keep it next to SQL generators, and align `Engine` with `SqlFacadeConfig.DbType` at startup.

### Process

1. **Pick the facade dialect** when creating `SqlFacadeConfig`.
2. **Set `SqlDialect.Engine` to the same `DbType`** (and in test fixtures set both to `SQLITE`).
3. **Use the helper in SQL generators** for any fragment that is not portable.
4. **Keep NestedList columns JSON-safe** via helper field factories so `NestedListMapper` can deserialize reliably.
5. **Prefer invariant string forms for writes** when SqlKata would otherwise culture-format a type (e.g. `DateOnly` → `yyyy-MM-dd`).

### Consolidated skeleton (`DbType` switch)

A practical union of helpers commonly needed across engines. Trim methods you do not need; add PostGIS / schema helpers in the app.

```csharp
public static class SqlDialect
{
    // Set once from SqlFacadeConfig.DbType (Postgres default in the sample).
    public static DbType Engine { get; set; } = DbType.POSTGRES;

    public static string Now => Engine switch
    {
        DbType.SQLITE => "datetime('now')",
        DbType.SQLSERVER => "SYSUTCDATETIME()",
        DbType.MYSQL or DbType.MARIADB => "UTC_TIMESTAMP()",
        DbType.ORACLE => "SYS_EXTRACT_UTC(SYSTIMESTAMP)",
        _ => "now()"
    };

    public static object BooleanValue(bool value) => Engine switch
    {
        DbType.SQLITE or DbType.MYSQL or DbType.MARIADB or DbType.ORACLE => value ? 1 : 0,
        _ => value // Postgres + SQL Server
    };

    public static string Boolean(bool value) => Engine switch
    {
        DbType.POSTGRES => value ? "true" : "false",
        _ => value ? "1" : "0"
    };

    public static object DateOnlyField(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    public static string CastToBool(string column) => Engine == DbType.POSTGRES
        ? column
        : $"CASE WHEN {column} THEN 1 ELSE 0 END";

    public static bool CastToBoolIsRaw => Engine != DbType.POSTGRES;

    public static object UuidValue(Guid value) =>
        Engine is DbType.POSTGRES or DbType.SQLSERVER ? value : value.ToString("D");

    public static Field NestedListBool(string column, string alias) =>
        new Field(CastToBool(column), alias, CastToBoolIsRaw);

    public static Field NestedListInt(string column, string alias) =>
        new Field(CastToInt(column), alias, isRaw: true);

    public static Field NestedListDate(string column, string alias) =>
        new Field(/* engine-specific date→text — see ExampleSqlDialect */, alias, isRaw: true);

    public static Field NestedListTimestamptz(string column, string alias) =>
        new Field(/* engine-specific timestamptz→text — see ExampleSqlDialect */, alias, isRaw: true);
}
```

### Wire-up

```csharp
// Application startup
SqlDialect.Engine = dbType; // same DbType as SqlFacadeConfig
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(dbType, connectionString));

// Test fixture
SqlDialect.Engine = DbType.SQLITE;
```

### Usage in SQL generators

```csharp
.WithExpression(new Expression("is_active", SqlDialect.BooleanValue(true)))
.WithField(new Field("occurrence_date", SqlDialect.DateOnlyField(date)))
.WithRawExpression($"updated_at >= {SqlDialect.Now}")
.WithField(SqlDialect.NestedListBool("li.is_active", "isActive"))
.WithField(SqlDialect.NestedListTimestamptz("li.shipped_at", "shippedAt"))
.WithField(new Field("id", SqlDialect.UuidValue(id)))
```

### Typical helper surface

| Concern | Postgres | SQLite | MySQL / MariaDB | SQL Server | Oracle |
|---------|----------|--------|-----------------|------------|--------|
| Current timestamp | `now()` | `datetime('now')` | `UTC_TIMESTAMP()` | `SYSUTCDATETIME()` | `SYS_EXTRACT_UTC(SYSTIMESTAMP)` |
| Boolean bind | `true`/`false` | `1`/`0` | `1`/`0` | `bool`/`bit` | `1`/`0` |
| Cast to text | `expr::text` | `CAST(… AS TEXT)` | `CAST(… AS CHAR)` | `CAST(… AS nvarchar)` | `TO_CHAR(…)` |
| Bool in NestedList | column as-is | `CASE … 1/0` | `CASE … 1/0` | `CASE … 1/0` | `CASE … 1/0` |
| Guid write / `IN` | native `Guid` | `D`-format text | `D`-format text | native `Guid` | `D`-format text |
| `DateOnly` write | invariant `yyyy-MM-dd` | same | same | same | same |

**App-only extensions** (keep out of the shared sample): PostGIS `ST_X` / `POINT(lon lat)` vs `longitude`/`latitude` columns; schema-qualified table names; regex / `GLOB` etag predicates; HTML entity escaping in concat.

## Testing

Unit tests use **SQLite in-memory** (`Data Source=:memory:`) for full runtime coverage without external databases. Dialect-specific SQL for every `DbType` (including MySQL, MariaDB, Oracle NestedList wraps) is verified via `GetSql` / `ToSql` compilation tests — no cloud DB credentials required in CI.

Optional **live container** tests live under `SqlFacade.Tests/Live/` (Testcontainers). They are discovered only when `SQLFACADE_LIVE_ENGINES` is set. Prefer **Podman** (auto-detected socket; no `docker` CLI). See the repo [README](../README.md#live-container-tests).

```bash
SQLFACADE_LIVE_ENGINES=postgres dotnet test --filter Category=Live   # one engine
SQLFACADE_LIVE_ENGINES=all      dotnet test --filter Category=Live   # every engine
```

When you adopt an application `SqlDialect`, set `Engine = DbType.SQLITE` in the test fixture (and to the deploy engine when asserting compiled SQL). The sample project [`SqlFacade.Example`](../SqlFacade.Example/Program.cs) exercises inserts, updates, deletes, filters, joins, derived tables, CTEs, set operations, group/having, pagination, JSON round-trip, and nested lists, and prints multi-engine `GetSql` / `NestedList.ToSql` / dialect-helper samples.

XML documentation is included in the NuGet package (`GenerateDocumentationFile`).
