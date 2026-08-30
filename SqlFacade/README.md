# SQL Facade library

## Introduction

`Beztek.Facade.Sql` is a database-portable SQL facade for .NET. Services build queries with typed objects (`SqlSelect`, `SqlInsert`, …) instead of hand-written SQL strings; the library compiles them per dialect via SQLKata and executes with Dapper.

Use SQLite for offline development and switch to PostgreSQL or SQL Server in production by changing `SqlFacadeConfig` only.

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

### SQL Server

```csharp
var config = new SqlFacadeConfig(
    DbType.SQLSERVER,
    "Server=localhost;Database=mydb;Trusted_Connection=True;");
ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(config);
```

### Transaction isolation

Every call runs inside a `TransactionScope`. `SqlFacadeConfig.TransactionIsolationLevel` controls isolation (default **ReadCommitted**). Set `Serializable` only when callers truly need serializable semantics.

```csharp
var config = new SqlFacadeConfig(DbType.POSTGRES, connectionString)
{
    TransactionIsolationLevel = System.Transactions.IsolationLevel.ReadCommitted
};
```

For nested application transactions, wrap your code in an outer `TransactionScope`; the facade uses `RequiresNew` when a scope is already active.

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
| `In` | Value in list or subquery (`WithSqlIn`) |
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
| **Postgres** | `json_agg(row_to_json(...))` (typed `json`, not `::text`) |
| **SQLite** | `json_group_array(json_object(...))` |
| **SQL Server** | child `SELECT … FOR JSON PATH, INCLUDE_NULL_VALUES` |

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
```

### Type mapping notes

`NestedListMapper` parses JSON child arrays with flexible converters for `DateTime` (offset-less SQLite/Postgres text treated as UTC), `DateOnly`, `bool`, and `decimal`. Parent scalar columns map to DTO properties by name (case-insensitive).

## Dialect compilation (`GetSql`)

Compile without executing — useful for logging, review, or cross-dialect tests:

```csharp
string raw = sql.GetSql(select, isParameterized: false);
string bound = sql.GetSql(select, isParameterized: true);  // @p0, @p1, …
```

## Application dialect helpers

This facade lets you run **integration-style unit tests against SQLite in-memory** while deploying against Postgres or SQL Server. Point `SqlFacadeConfig` at `:memory:` (or a temp file), flip an application dialect helper to the SQLite branch, and exercise the same SQL generators, filters, NestedList mapping, and write paths your services use in production — without a real database in CI.

SQLKata (via this facade) already handles **structural** dialect differences: identifier quoting, `LIMIT`/`OFFSET`, parameterized placeholders, and similar. What it does **not** unify are **expression-level** fragments that still differ across engines — boolean literals, `NOW()`, type casts, date binding for `DateOnly`, NestedList-safe JSON column selects, PostGIS vs plain lat/lon columns, and so on.

Those belong in an **application-owned dialect helper** (not in this library). Keep a static `SqlDialect` next to your SQL generators: configure it once from the same `DbType` used to create `ISqlFacade`, then call it whenever a query needs a dialect-specific fragment. The helper is the map between the **deployed** engine and **SQLite under test** — production code stays dialect-agnostic at the call site; only the helper emits the right fragment.

### Why a helper

| Layer | Responsibility |
|-------|----------------|
| `SqlFacade` / SQLKata | Compile `SqlSelect` / `SqlInsert` / … into Postgres, SQL Server, or SQLite SQL |
| App `SqlDialect` | Emit engine-specific **raw expressions**, **bind values**, and **Field** helpers used *inside* those query objects |

Without a helper, every SQL generator sprouts `if (sqlite) … else …` branches. Centralizing them keeps generators readable and makes “SQLite for tests / Postgres (or SQL Server) for deploy” a single flag instead of scattered conditionals.

Typical flow:

1. Production starts with `DbType.POSTGRES` (or `SQLSERVER`) and `SqlDialect` on the matching branch.
2. The test fixture sets `DbType.SQLITE` + `SqlDialect.UseSqlite = true`, creates tables (often a SQLite-friendly subset of the production schema), and runs the same generators/services.
3. Where engines diverge (casts, bools, timestamps, geography), the helper supplies the SQLite equivalent so assertions hit a real connection — not mocks of SQL.

### Process

1. **Pick the facade dialect** when creating `SqlFacadeConfig` (`DbType.POSTGRES`, `SQLITE`, or `SQLSERVER`).
2. **Mirror that choice** on the app helper at startup (and in test fixtures).
3. **Use the helper in SQL generators** for any fragment that is not portable.
4. **Keep NestedList columns JSON-safe** via helper field factories (casts to text / CASE for bools) so `NestedListMapper` can deserialize reliably.
5. **Prefer invariant string forms for writes** when SqlKata would otherwise culture-format a type (e.g. `DateOnly` → `yyyy-MM-dd`).

### Skeleton

```csharp
public static class SqlDialect
{
    // Set once from config / environment (Postgres default; flip for SQLite tests/local).
    public static bool UseSqlite { get; set; }

    public static string Now =>
        UseSqlite ? "datetime('now')" : "now()";

    public static object BooleanValue(bool value) =>
        UseSqlite ? (value ? 1 : 0) : value;

    public static string CastToText(string expression) =>
        UseSqlite ? $"CAST({expression} AS TEXT)" : $"{expression}::text";

    public static string CastToBool(string column) =>
        UseSqlite ? $"CASE WHEN {column} THEN 1 ELSE 0 END" : column;

    public static bool CastToBoolIsRaw => UseSqlite;

    // NestedList / NestedListMapper-safe select fields
    public static Field NestedListBool(string column, string alias) =>
        new Field(CastToBool(column), alias, CastToBoolIsRaw);

    public static Field NestedListDate(string column, string alias) =>
        new Field(UseSqlite ? $"CAST(date({column}) AS TEXT)" : $"({column})::text", alias, isRaw: true);

    // Writes: always ISO date text so SqlKata does not culture-format DateOnly
    public static object DateOnlyField(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
```

### Wire-up

```csharp
// Application startup (match SqlFacadeConfig.DbType)
SqlDialect.UseSqlite = dbType == DbType.SQLITE;

ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(dbType, connectionString));
```

```csharp
// Test fixture — same in-memory SQLite as the facade under test
SqlDialect.UseSqlite = true;
```

### Usage in SQL generators

```csharp
// Filter / write values
.WithExpression(new Expression("is_active", SqlDialect.BooleanValue(true)))
.WithField(new Field("is_deleted", SqlDialect.BooleanValue(false)))
.WithField(new Field("occurrence_date", SqlDialect.DateOnlyField(date)))

// Raw timestamp comparisons
.WithRawExpression($"updated_at >= {SqlDialect.Now}")

// NestedList child fields (JSON-safe across Postgres and SQLite)
.WithField(SqlDialect.NestedListBool("li.is_active", "isActive"))
.WithField(SqlDialect.NestedListDate("li.ship_date", "shipDate"))

// Dialect-specific raw predicates (e.g. write-behind etag gates)
var raw = SqlDialect.UseSqlite
    ? "(etag IS NULL OR etag NOT GLOB '[0-9]*' OR CAST(etag AS INTEGER) < ?)"
    : "(etag IS NULL OR etag !~ '^[0-9]+$' OR CAST(etag AS BIGINT) < ?)";
update.WithFilter(new Expression(raw, new object[] { incomingSeq }).WithIsRaw());
```

### Typical helper surface

| Concern | Postgres | SQLite (typical) |
|---------|----------|------------------|
| Current timestamp | `now()` | `datetime('now')` |
| Boolean bind / literal | `true` / `false` | `1` / `0` |
| Cast to text | `expr::text` | `CAST(expr AS TEXT)` |
| Bool in NestedList JSON | column as-is | `CASE WHEN col THEN 1 ELSE 0 END` |
| Date in NestedList JSON | `(col)::text` | `CAST(date(col) AS TEXT)` |
| `DateOnly` write value | invariant `yyyy-MM-dd` string | same (avoids culture-formatted literals) |
| Geography / extensions | PostGIS `ST_X` / WKT | separate `longitude` / `latitude` columns |

Extend the helper as your schema needs (SQL Server branches, schema-qualified names, etc.). Keep **one** process-wide setting aligned with `SqlFacadeConfig.DbType` so generators never hard-code an engine.

## Testing

Unit tests use **SQLite in-memory** (`Data Source=:memory:`) for full runtime coverage without external databases. Dialect-specific SQL (Postgres, SQL Server, `NestedList.ToSql`) is verified via `GetSql` / `ToSql` compilation tests — no cloud DB credentials required in CI.

When you adopt an application `SqlDialect`, set it to SQLite in the test fixture (and to Postgres when asserting compiled Postgres SQL). The sample project [`SqlFacade.Example`](../SqlFacade.Example/Program.cs) exercises inserts, updates, deletes, filters, joins, derived tables, CTEs, set operations, group/having, pagination, JSON round-trip, and nested lists.

XML documentation is included in the NuGet package (`GenerateDocumentationFile`).
