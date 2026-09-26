// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text.Json;
    using Beztek.Facade.Sql;
    using Microsoft.Data.Sqlite;
    using NUnit.Framework;
    using SqlDbType = Beztek.Facade.Sql.DbType;

    [TestFixture]
    public class ExpressionBranchCoverageTests
    {
        private ISqlFacade _sql;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var config = new SqlFacadeConfig(SqlDbType.SQLITE, "Data Source=:memory:")
            {
                // Distinct cache key so this fixture does not share NestedListTests' in-memory schema.
                TransactionIsolationLevel = System.Transactions.IsolationLevel.RepeatableRead
            };
            _sql = SqlFacadeFactory.GetSqlFacade(config);
            using IDbConnection con = _sql.GetSqlFacadeConfig().GetConnection();
            using var cmd = new SqliteCommand(
                "CREATE TABLE IF NOT EXISTS item(id TEXT PRIMARY KEY, name TEXT, qty INT, flag INT, color TEXT);"
                + "CREATE TABLE IF NOT EXISTS child(id TEXT PRIMARY KEY, item_id TEXT);",
                (SqliteConnection)con);
            cmd.ExecuteNonQuery();
        }

        [SetUp]
        public void SetUp()
        {
            _sql.ExecuteSqlWrite(new SqlDelete("child"));
            _sql.ExecuteSqlWrite(new SqlDelete("item"));
            _sql.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("item").WithField(new Field("id", "1")).WithField(new Field("name", "alpha"))
                    .WithField(new Field("qty", 10)).WithField(new Field("flag", 1)).WithField(new Field("color", "red")),
                new SqlInsert("item").WithField(new Field("id", "2")).WithField(new Field("name", "beta"))
                    .WithField(new Field("qty", 20)).WithField(new Field("flag", 0)).WithField(new Field("color", "blue")),
                new SqlInsert("child").WithField(new Field("id", "c1")).WithField(new Field("item_id", "1")),
            });
        }

        [Test]
        public void NestedFilters_AndNot_Or_OrNot_CompileAndRun()
        {
            // Nested filter combination uses the *parent* Filter.LogicalRelation.
            var andNotParent = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter(LogicalRelation.AndNot)
                    .WithExpression(new Expression("qty", 0).WithRelation(Relation.GreaterThan))
                    .WithFilter(new Filter().WithExpression(new Expression("color", "missing"))));

            var orParent = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter(LogicalRelation.Or)
                    .WithExpression(new Expression("color", "red"))
                    .WithFilter(new Filter().WithExpression(new Expression("color", "blue"))));

            var orNotParent = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter(LogicalRelation.OrNot)
                    .WithExpression(new Expression("id", "1"))
                    .WithFilter(new Filter().WithExpression(new Expression("color", "missing"))));

            Assert.That(_sql.GetSql(andNotParent, true), Is.Not.Empty);
            Assert.That(_sql.GetResults<string>(orParent).Count, Is.GreaterThan(0));
            Assert.That(_sql.GetSql(orNotParent, true), Is.Not.Empty);
        }

        [Test]
        public void AndNot_Comparisons_AndStringMatchers_Compile()
        {
            foreach (Relation relation in new[]
                     {
                         Relation.EqualTo, Relation.GreaterThan, Relation.GreaterThanOrEqualTo,
                         Relation.LessThan, Relation.LessThanOrEqualTo,
                         Relation.StartsWith, Relation.EndsWith, Relation.Contains
                     })
            {
                object value = relation == Relation.EqualTo || relation.ToString().Contains("With")
                    || Object.Equals(relation, Relation.StartsWith)
                    || Object.Equals(relation, Relation.EndsWith)
                    || Object.Equals(relation, Relation.Contains)
                    ? "a"
                    : 5;
                if (Object.Equals(relation, Relation.EqualTo))
                    value = "alpha";

                var select = new SqlSelect("item")
                    .WithField(new Field("id"))
                    .WithWhere(new Filter()
                        .WithExpression(new Expression("name", "x"))
                        .WithExpression(new Expression(
                                Object.Equals(relation, Relation.StartsWith)
                                || Object.Equals(relation, Relation.EndsWith)
                                || Object.Equals(relation, Relation.Contains)
                                || Object.Equals(relation, Relation.EqualTo)
                                    ? "name"
                                    : "qty",
                                value)
                            .WithRelation(relation)
                            .WithLogicalRelation(LogicalRelation.AndNot)));

                Assert.That(_sql.GetSql(select, true), Is.Not.Empty);
            }
        }

        [Test]
        public void OrNot_Comparisons_Compile()
        {
            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("id", "1"))
                    .WithExpression(new Expression("qty", 100)
                        .WithRelation(Relation.GreaterThan)
                        .WithLogicalRelation(LogicalRelation.OrNot))
                    .WithExpression(new Expression("qty", 0)
                        .WithRelation(Relation.LessThan)
                        .WithLogicalRelation(LogicalRelation.OrNot))
                    .WithExpression(new Expression("qty", 100)
                        .WithRelation(Relation.GreaterThanOrEqualTo)
                        .WithLogicalRelation(LogicalRelation.OrNot))
                    .WithExpression(new Expression("qty", 0)
                        .WithRelation(Relation.LessThanOrEqualTo)
                        .WithLogicalRelation(LogicalRelation.OrNot))
                    .WithExpression(new Expression("name", "z")
                        .WithLogicalRelation(LogicalRelation.OrNot))
                    .WithExpression(new Expression("name", "z")
                        .WithRelation(Relation.StartsWith)
                        .WithLogicalRelation(LogicalRelation.OrNot))
                    .WithExpression(new Expression("name", "z")
                        .WithRelation(Relation.EndsWith)
                        .WithLogicalRelation(LogicalRelation.OrNot))
                    .WithExpression(new Expression("name", "z")
                        .WithRelation(Relation.Contains)
                        .WithLogicalRelation(LogicalRelation.OrNot)));

            Assert.That(_sql.GetSql(select, false), Does.Contain("OR"));
        }

        [Test]
        public void NullValue_AndNot_And_Or_OrNot_Compile()
        {
            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("color", null).WithRelation(Relation.NullValue))
                    .WithExpression(new Expression("color", null)
                        .WithRelation(Relation.NullValue)
                        .WithLogicalRelation(LogicalRelation.AndNot))
                    .WithExpression(new Expression("name", null)
                        .WithRelation(Relation.NullValue)
                        .WithLogicalRelation(LogicalRelation.Or))
                    .WithExpression(new Expression("name", null)
                        .WithRelation(Relation.NullValue)
                        .WithLogicalRelation(LogicalRelation.OrNot)));

            Assert.That(_sql.GetSql(select, true), Does.Contain("NULL").IgnoreCase);
        }

        [Test]
        public void TrueValue_Branches_Compile()
        {
            // SQLite has no native boolean column helper in this schema; compile-only via GetSql.
            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("flag", null).WithRelation(Relation.TrueValue))
                    .WithExpression(new Expression("flag", null)
                        .WithRelation(Relation.TrueValue)
                        .WithLogicalRelation(LogicalRelation.AndNot))
                    .WithExpression(new Expression("flag", null)
                        .WithRelation(Relation.TrueValue)
                        .WithLogicalRelation(LogicalRelation.Or))
                    .WithExpression(new Expression("flag", null)
                        .WithRelation(Relation.TrueValue)
                        .WithLogicalRelation(LogicalRelation.OrNot)));

            Assert.That(_sql.GetSql(select, true), Is.Not.Empty);
        }

        [Test]
        public void Exists_And_AndNot_Or_OrNot_Compile()
        {
            SqlSelect exists = new SqlSelect("child").WithField(new Field("id"));

            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("id", exists).WithRelation(Relation.Exists))
                    .WithExpression(new Expression("id", exists)
                        .WithRelation(Relation.Exists)
                        .WithLogicalRelation(LogicalRelation.AndNot))
                    .WithExpression(new Expression("id", exists)
                        .WithRelation(Relation.Exists)
                        .WithLogicalRelation(LogicalRelation.Or))
                    .WithExpression(new Expression("id", exists)
                        .WithRelation(Relation.Exists)
                        .WithLogicalRelation(LogicalRelation.OrNot)));

            string sql = _sql.GetSql(select, true);
            Assert.That(sql.ToUpperInvariant(), Does.Contain("EXISTS"));
        }

        [Test]
        public void In_NumericTypes_AndOrModes_Compile()
        {
            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("qty", new[] { 10, 20 }).WithRelation(Relation.In))
                    .WithExpression(new Expression("qty", new long[] { 10L, 20L })
                        .WithRelation(Relation.In)
                        .WithLogicalRelation(LogicalRelation.Or))
                    .WithExpression(new Expression("qty", new float[] { 10f })
                        .WithRelation(Relation.In)
                        .WithLogicalRelation(LogicalRelation.OrNot))
                    .WithExpression(new Expression("qty", new double[] { 10d })
                        .WithRelation(Relation.In)
                        .WithLogicalRelation(LogicalRelation.AndNot)));

            Assert.That(_sql.GetResults<string>(select).Count, Is.GreaterThan(0));
        }

        [Test]
        public void In_UnsupportedElementType_Throws()
        {
            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("name", new[] { DateTime.UtcNow }).WithRelation(Relation.In)));

            Assert.Throws<ArgumentException>(() => _sql.GetSql(select, true));
        }

        [Test]
        public void In_UnsupportedScalar_Throws()
        {
            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("name", 123).WithRelation(Relation.In)));

            Assert.Throws<ArgumentException>(() => _sql.GetSql(select, true));
        }

        [Test]
        public void In_JsonNullKind_Throws()
        {
            string json = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("qty", (object)null).WithRelation(Relation.In)))
                .ToString();

            // Force a JsonElement null into an In expression via deserialize mutation isn't trivial;
            // compile a select whose In value is a JSON null element.
            var expression = new Expression("qty", JsonDocument.Parse("null").RootElement)
                .WithRelation(Relation.In);
            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(expression));

            Assert.Throws<ArgumentException>(() => _sql.GetSql(select, true));
        }

        [Test]
        public void In_Subquery_AndJsonObject_Compile()
        {
            var sub = new SqlSelect("item").WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "red")));

            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", sub).WithRelation(Relation.In)));

            Assert.That(_sql.GetResults<string>(select), Is.EqualTo(new[] { "1" }));

            // Round-trip so ApplyInExpression sees JsonElement object.
            var roundTripped = (SqlSelect)_sql.DeserializeFromJson(select.ToString());
            Assert.That(_sql.GetResults<string>(roundTripped), Is.EqualTo(new[] { "1" }));
        }

        [Test]
        public void In_OrNot_Values_Compile()
        {
            var select = new SqlSelect("item")
                .WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("id", "1"))
                    .WithExpression(new Expression("qty", new[] { 999 })
                        .WithRelation(Relation.In)
                        .WithLogicalRelation(LogicalRelation.OrNot)));

            Assert.That(_sql.GetResults<string>(select).Select(x => x).ToList(), Does.Contain("1"));
        }

        [Test]
        public void Raw_AndNot_And_OrNot_Throw()
        {
            var andNot = new SqlSelect("item").WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("1=1", Array.Empty<object>()).WithIsRaw())
                    .WithExpression(new Expression("1=0", Array.Empty<object>())
                        .WithIsRaw()
                        .WithLogicalRelation(LogicalRelation.AndNot)));
            Assert.Throws<ArgumentException>(() => _sql.GetSql(andNot, true));

            var orNot = new SqlSelect("item").WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("1=1", Array.Empty<object>()).WithIsRaw())
                    .WithExpression(new Expression("1=0", Array.Empty<object>())
                        .WithIsRaw()
                        .WithLogicalRelation(LogicalRelation.OrNot)));
            Assert.Throws<ArgumentException>(() => _sql.GetSql(orNot, true));
        }

        [Test]
        public void OrWhereRaw_Compiles()
        {
            var select = new SqlSelect("item").WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("id", "1"))
                    .WithExpression(new Expression("qty = 20", Array.Empty<object>())
                        .WithIsRaw()
                        .WithLogicalRelation(LogicalRelation.Or)));

            Assert.That(_sql.GetResults<string>(select).Count, Is.EqualTo(2));
        }

        [Test]
        public void DeserializeFromJson_Invalid_Throws()
        {
            Assert.Throws<ArgumentException>(() => _sql.DeserializeFromJson("{not-json"));
            Assert.Throws<ArgumentException>(() => _sql.DeserializeFromJson("""{"SqlType":"nope"}"""));
        }

        [Test]
        public void UnwrapJsonValue_ViaInsertRoundTrip()
        {
            // JsonElement values appear after deserialize; execute write paths that call UnwrapJsonValue.
            var insert = new SqlInsert("item")
                .WithField(new Field("id", "3"))
                .WithField(new Field("name", "gamma"))
                .WithField(new Field("qty", 30))
                .WithField(new Field("flag", true))
                .WithField(new Field("color", "green"));

            var roundTripped = (SqlInsert)_sql.DeserializeFromJson(insert.ToString());
            Assert.That(_sql.ExecuteSqlWrite(roundTripped), Is.EqualTo(1));
        }

        [Test]
        public void GetInClauseMode_Unsupported_Throws()
        {
            var bogus = new LogicalRelation { Value = "Xor" };
            var select = new SqlSelect("item").WithField(new Field("id"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("qty", new[] { 10 })
                        .WithRelation(Relation.In)
                        .WithLogicalRelation(bogus)));

            Assert.Throws<ArgumentException>(() => _sql.GetSql(select, true));
        }

        [Test]
        public void Cte_SelectBased_OnInsertUpdateDelete_Compile()
        {
            var cteSelect = new SqlSelect("item").WithField(new Field("id")).WithField(new Field("name"));
            var cte = new CommonTableExpression(cteSelect, "seed");

            Assert.That(_sql.GetSql(new SqlInsert("item").WithCommonTableExpression(cte)
                .WithQuery(new SqlSelect("seed").WithField(new Field("id")).WithField(new Field("name"))
                    .WithField(new Field("1", "qty", isRaw: true))
                    .WithField(new Field("0", "flag", isRaw: true))
                    .WithField(new Field("'x'", "color", isRaw: true))), true), Does.Contain("WITH").IgnoreCase);

            Assert.That(_sql.GetSql(new SqlUpdate("item")
                .WithCommonTableExpression(cte)
                .WithField(new Field("name", "z"))
                .WithFilter(new Expression("id", "1")), true), Does.Contain("WITH").IgnoreCase);

            Assert.That(_sql.GetSql(new SqlDelete("item")
                .WithCommonTableExpression(cte)
                .WithFilter(new Expression("id", "1")), true), Does.Contain("WITH").IgnoreCase);
        }

        [Test]
        public void UnwrapJsonValue_NumberKinds_ViaUpdateRoundTrip()
        {
            // Large number → Int64; bool false; null field value after JSON round-trip.
            var update = new SqlUpdate("item")
                .WithField(new Field("qty", 3000000000L))
                .WithField(new Field("flag", false))
                .WithFilter(new Expression("id", "1"));
            var roundTripped = (SqlUpdate)_sql.DeserializeFromJson(update.ToString());
            Assert.That(_sql.ExecuteSqlWrite(roundTripped), Is.EqualTo(1));
        }

        [Test]
        public void ExecuteSqlWrite_ConstraintFailure_WrapsException()
        {
            Assert.Throws<ArgumentException>(() =>
                _sql.ExecuteSqlWrite(new SqlInsert("item")
                    .WithField(new Field("id", "1"))
                    .WithField(new Field("name", "dup"))
                    .WithField(new Field("qty", 1))
                    .WithField(new Field("flag", 0))
                    .WithField(new Field("color", "x"))));
        }

        [Test]
        public void JoinType_Equals_NonJoinType_IsFalse()
        {
            Assert.That(JoinType.InnerJoin.Equals("InnerJoin"), Is.False);
            Assert.That(JoinType.LeftJoin.Equals(JoinType.InnerJoin), Is.False);
        }

        [Test]
        public void QFactory_UnsupportedDbType_Throws()
        {
            Assert.Throws<ArgumentException>(() => QFactory.GetCompiler((SqlDbType)999));
        }
    }
}
