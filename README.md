# Beztek.Facade.Sql Library

This library is intended for providing an facade ORM layer over SQL Databases. It uses SQLKata, and thus enables a level of abstraction over the nuances and particular syntaxes of various databases.

# Overview

It is intended to be cloud portable and take advantage of the native managed services in each cloud, such as managed Postgres DBs or managed Sql Server DBs.
It is a reusable and configurable sql facade library.

## Steps to use Sql Facade

1. Find SQL connection string. It currently supports Postgres SQL, Sql Server and SQLite (in-memory and file-based)/
2. Instantiate the SqlFacade object from the SqlFacadeFactory, using the appropriate SqlFacadeConfig object

`SqlFacadeConfig.TransactionIsolationLevel` controls the `TransactionScope` isolation for every SQL call (default **ReadCommitted**). Set to `Serializable` only when a caller truly needs serializable semantics.

## Sample Project

The solution contains a sample project that you can modify and run to test out different use cases and scenarios. Simply set it as the startup project and then run. The unit tests also provide examples of how to use this library.

### Useful ways to use this library

1. Local development can use a SQLite DB file, and when deployed it can use DBs such as Postgres or Sql Server, which could be managed cloud DBs as well. SQLite can be isntantiated in-memory as in the example project and the unit tests. This enables quick-and-dirty offline development without the need of a full database.

### Nested lists (`NestedList`)

For 1:N child collections on a parent row, attach a child **`SqlSelect`** via `WithNestedList`. Under the hood the facade emits a dialect-specific JSON array aggregate, then **maps it onto a typed list property** on the parent DTO (`List<T>`, `IList<T>`, or `T[]`). The property name must match the `NestedList` result alias.

- **Postgres** — `json_agg(row_to_json(...))::text`
- **SQLite** — `json_group_array(json_object(...))` (keys from field aliases)
- **SQL Server** — child `SELECT … FOR JSON PATH, INCLUDE_NULL_VALUES`

```csharp
public class CareFundRow
{
    public string Id { get; set; }
    public List<DonationDto> Donations { get; set; }  // filled automatically
}

var select = new SqlSelect(new Table("care_funds", "c"))
    .WithField(new Field("c.id", "Id"))
    .WithNestedList(
        new NestedList<DonationDto>("Donations",
            new SqlSelect(new Table("care_fund_donations", "d"))
                .WithField(new Field("d.id", "id"))
                .WithField(new Field("d.amount", "amount"))
                .WithSort(new Sort("d.created_timestamp")),
            new Expression("d.care_fund_id", "c.id")));

IList<CareFundRow> rows = sqlFacade.GetResults<CareFundRow>(select);
// rows[0].Donations is already List<DonationDto> — no manual JSON parse

// Complex correlate: pass a Filter (And / Or / AndNot / OrNot, nested filters)
var complex = new NestedList<DonationDto>("Donations",
    donationSelect,
    new Filter()
        .WithExpression(new Expression("d.care_fund_id", "c.id"))
        .WithExpression(new Expression("d.is_active", "c.is_active")
            .WithLogicalRelation(LogicalRelation.And)));
```

Child `Fields` are required (alias = JSON property name). Correlate is required: pass a Join-style ON `Expression` (both sides columns) or a `Filter`. Non-correlation filters can stay on the child `SqlSelect.Where`.
