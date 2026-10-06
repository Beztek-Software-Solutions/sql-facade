// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using Beztek.Facade.Sql;
    using NUnit.Framework;
    using SqlDbType = Beztek.Facade.Sql.DbType;

    [TestFixture]
    public class NestedListEdgeCoverageTests
    {
        private static NestedList ValidNested(SqlSelect select = null, Filter correlate = null) =>
            new NestedList<ChildDto>(
                "Children",
                select ?? new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                correlate ?? new Filter().WithExpression(new Expression("c.parent_id", "p.id")));

        [Test]
        public void ElementType_ResolvesFromElementTypeName()
        {
            var nested = new NestedList
            {
                ResultAlias = "Children",
                Select = new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                Correlate = new Filter().WithExpression(new Expression("c.parent_id", "p.id")),
                ElementTypeName = typeof(ChildDto).AssemblyQualifiedName
            };

            Assert.That(nested.ElementType, Is.EqualTo(typeof(ChildDto)));
            Assert.That(nested.ElementType, Is.EqualTo(typeof(ChildDto))); // cached
        }

        [Test]
        public void ElementType_BlankName_ReturnsNull()
        {
            var nested = new NestedList { ElementTypeName = "  " };
            Assert.That(nested.ElementType, Is.Null);
        }

        [Test]
        public void Ctor_ValidationBranches_Throw()
        {
            var select = new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id"));
            var correlate = new Expression("c.parent_id", "p.id");

            Assert.Throws<ArgumentException>(() => new NestedList(" ", select, correlate, typeof(ChildDto)));
            Assert.Throws<ArgumentNullException>(() => new NestedList("Children", null, correlate, typeof(ChildDto)));
            Assert.Throws<ArgumentNullException>(() =>
                new NestedList("Children", select, (Filter)null, typeof(ChildDto)));
            Assert.Throws<ArgumentException>(() =>
                new NestedList("Children", select, new Filter(), typeof(ChildDto)));
            Assert.Throws<ArgumentNullException>(() =>
                new NestedList("Children", select, correlate, null));
            Assert.Throws<ArgumentNullException>(() =>
                NestedList.ToCorrelateFilter(null));
        }

        [Test]
        public void SelectForCompile_DefaultOverload_AndExistingWhere()
        {
            var select = new SqlSelect(new Table("child", "c"))
                .WithField(new Field("c.id", "id"))
                .WithWhere(new Filter().WithExpression(new Expression("c.label", "x")));
            var nested = ValidNested(select);
            SqlSelect compiled = nested.SelectForCompile();
            Assert.That(compiled.Where.Filters, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Wrap_EmptyInnerSql_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                NestedList.Wrap(SqlDbType.SQLITE, "  ",
                    new SqlSelect(new Table("c")).WithField(new Field("id"))));
        }

        [Test]
        public void ToCorrelateWhere_Raw_AndDefaultOverload()
        {
            var raw = new Expression("c.parent_id = p.id", Array.Empty<object>()).WithIsRaw();
            Assert.That(NestedList.ToCorrelateWhere(raw).IsRaw, Is.True);
            Assert.That(NestedList.ToCorrelateWhere(new Expression("c.parent_id", "p.id")).IsRaw, Is.True);
        }

        [Test]
        public void ToCorrelateWhere_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => NestedList.ToCorrelateWhere(null));
        }

        [Test]
        public void ToCorrelateWhere_MissingRight_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                NestedList.ToCorrelateWhere(new Expression("c.parent_id", null)));
            Assert.Throws<InvalidOperationException>(() =>
                NestedList.ToCorrelateWhere(new Expression("c.parent_id", "  ")));
        }

        [Test]
        public void ToCorrelateFilter_NestedFilters_AndNullEntries()
        {
            var filter = new Filter()
                .WithExpression(new Expression("c.parent_id", "p.id"))
                .WithExpression(null)
                .WithFilter(new Filter().WithExpression(new Expression("c.label", "p.name")))
                .WithFilter(null);

            Filter correlated = NestedList.ToCorrelateFilter(filter, SqlDbType.SQLSERVER);
            Assert.That(correlated.Expressions, Has.Count.EqualTo(1));
            Assert.That(correlated.Filters, Has.Count.EqualTo(1));
            Assert.That(correlated.Expressions[0].Name, Does.Contain("["));
        }

        [Test]
        public void QuoteCorrelateIdent_MySqlFamily_AndComplexLeftAlone()
        {
            var mysql = NestedList.ToCorrelateWhere(
                new Expression("c.parent_id", "p.id"), SqlDbType.MYSQL);
            Assert.That(mysql.Name, Does.Contain("`"));

            var complex = NestedList.ToCorrelateWhere(
                new Expression("c.parent_id + 0", "p.id"), SqlDbType.POSTGRES);
            Assert.That(complex.Name, Does.Contain("c.parent_id + 0"));
        }

        [Test]
        public void ToSql_Dialects_WithNestedGrandchildren()
        {
            var tags = new NestedList<TagDto>("Tags",
                new SqlSelect(new Table("tag", "t")).WithField(new Field("t.id", "id")),
                new Expression("t.child_id", "c.id"));

            var children = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c"))
                    .WithField(new Field("c.id", "id"))
                    .WithNestedList(tags)
                    .WithSort(new Sort("c.id")),
                new Expression("c.parent_id", "p.id"));

            Assert.That(children.ToSql(SqlDbType.SQLITE), Does.Contain("json("));
            Assert.That(children.ToSql(SqlDbType.MYSQL), Does.Contain("JSON_ARRAYAGG"));
            Assert.That(children.ToSql(SqlDbType.MARIADB), Does.Contain("JSON_EXTRACT"));
            Assert.That(children.ToSql(SqlDbType.ORACLE), Does.Contain("JSON_ARRAYAGG"));
            Assert.That(children.ToSql(SqlDbType.SQLSERVER), Does.Contain("FOR JSON"));
        }

        [Test]
        public void ToSql_Oracle_WithoutSorts()
        {
            var nested = ValidNested();
            Assert.That(nested.ToSql(SqlDbType.ORACLE), Does.Contain("JSON_OBJECT"));
        }

        [Test]
        public void Validate_NullFieldInList_Throws()
        {
            var select = new SqlSelect(new Table("child", "c"));
            select.Fields = new System.Collections.Generic.List<Field> { null };
            var nested = new NestedList
            {
                ResultAlias = "Children",
                Select = select,
                Correlate = new Filter().WithExpression(new Expression("c.parent_id", "p.id")),
                ElementType = typeof(ChildDto)
            };
            Assert.Throws<InvalidOperationException>(() => nested.ToSql(SqlDbType.SQLITE));
        }

        [Test]
        public void Validate_MissingAlias_Throws()
        {
            var nested = ValidNested();
            nested.ResultAlias = " ";
            Assert.Throws<InvalidOperationException>(() => nested.ToSql(SqlDbType.SQLITE));
        }

        [Test]
        public void QuoteHelpers_SpecialIdentifiers()
        {
            // Force non-alphanumeric JSON keys through dialect wraps.
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c"))
                    .WithField(new Field("c.id", "odd-key"))
                    .WithField(new Field("c.label", "also.key")),
                new Expression("c.parent_id", "p.id"));

            Assert.That(nested.ToSql(SqlDbType.SQLITE), Does.Contain("odd-key").Or.Contain("\"odd-key\""));
            Assert.That(nested.ToSql(SqlDbType.MYSQL), Does.Contain("odd-key"));
            Assert.That(nested.ToSql(SqlDbType.ORACLE), Does.Contain("odd-key"));
        }

        [Test]
        public void Wrap_EmptySql_ThrowsViaInternal()
        {
            Assert.Throws<ArgumentException>(() =>
                NestedList.Wrap(SqlDbType.POSTGRES, "",
                    new SqlSelect(new Table("c")).WithField(new Field("id"))));
        }

        [Test]
        public void NestedListGeneric_FilterCtor()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Filter().WithExpression(new Expression("c.parent_id", "p.id")));
            Assert.That(nested.ElementType, Is.EqualTo(typeof(ChildDto)));
        }

        [Test]
        public void NestedListGeneric_ParameterlessCtor()
        {
            var nested = new NestedList<ChildDto>
            {
                ResultAlias = "Children",
                Select = new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                Correlate = new Filter().WithExpression(new Expression("c.parent_id", "p.id")),
                ElementType = typeof(ChildDto)
            };
            Assert.That(nested.ElementType, Is.EqualTo(typeof(ChildDto)));
            Assert.That(nested.ToSql(SqlDbType.SQLITE), Does.Contain("json_group_array"));
        }

        [Test]
        public void Ctor_EmptyCorrelate_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new NestedList("Children",
                    new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                    new Filter(),
                    typeof(ChildDto)));
        }

        [Test]
        public void Wrap_UnsupportedDbType_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                NestedList.Wrap((SqlDbType)999, "SELECT 1",
                    new SqlSelect(new Table("c")).WithField(new Field("id"))));
        }

        [Test]
        public void Correlate_NestedFilterOnly_IsValid()
        {
            var correlate = new Filter().WithFilter(
                new Filter().WithExpression(new Expression("c.parent_id", "p.id")));
            var nested = new NestedList<ChildDto>(
                "Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                correlate);
            Assert.That(nested.ToSql(SqlDbType.POSTGRES), Does.Contain("json_agg"));
        }

        [Test]
        public void Wrap_EmptyFields_ThrowsForSqliteAndMySqlFamily()
        {
            var empty = new SqlSelect(new Table("child", "c"));
            Assert.Throws<InvalidOperationException>(() =>
                NestedList.Wrap(SqlDbType.SQLITE, "SELECT 1 AS x", empty));
            Assert.Throws<InvalidOperationException>(() =>
                NestedList.Wrap(SqlDbType.MYSQL, "SELECT 1 AS x", empty));
            Assert.Throws<InvalidOperationException>(() =>
                NestedList.Wrap(SqlDbType.MARIADB, "SELECT 1 AS x", empty));
        }

        [Test]
        public void Validate_MissingElementTypeAndSelect_Throw()
        {
            var noType = new NestedList
            {
                ResultAlias = "Children",
                Select = new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                Correlate = new Filter().WithExpression(new Expression("c.parent_id", "p.id"))
            };
            Assert.Throws<InvalidOperationException>(() => noType.ToSql(SqlDbType.SQLITE));

            var noSelect = new NestedList
            {
                ResultAlias = "Children",
                Correlate = new Filter().WithExpression(new Expression("c.parent_id", "p.id")),
                ElementType = typeof(ChildDto)
            };
            Assert.Throws<InvalidOperationException>(() => noSelect.ToSql(SqlDbType.SQLITE));
        }

        [Test]
        public void Wrap_BlankFieldName_QuotesJsonKeyAsExpression()
        {
            // ToSql validates Field.Name; Wrap is the path that quotes a blank Name via JsonKeyFor.
            var select = new SqlSelect(new Table("child", "c"))
                .WithField(new Field("  ", "odd-alias"));
            Assert.That(
                NestedList.Wrap(SqlDbType.MYSQL, "SELECT 1 AS x FROM child c", select),
                Does.Contain("`odd-alias`").Or.Contain("odd-alias"));
            Assert.That(
                NestedList.Wrap(SqlDbType.ORACLE, "SELECT 1 AS x FROM child c", select),
                Does.Contain("\"odd-alias\""));

            // Alphanumeric JSON key: backtick helper returns bare ident; double-quote helper always quotes.
            var simple = new SqlSelect(new Table("child", "c"))
                .WithField(new Field("  ", "id"));
            Assert.That(
                NestedList.Wrap(SqlDbType.MYSQL, "SELECT 1 AS x FROM child c", simple),
                Does.Contain(", id"));
            Assert.That(
                NestedList.Wrap(SqlDbType.ORACLE, "SELECT 1 AS x FROM child c", simple),
                Does.Contain("\"id\""));
        }

        [Test]
        public void QuoteHelpers_BacktickAndDouble_EscapeSpecialChars()
        {
            // Quote*Ident escape branches need special chars in the JSON key with a blank SQL Name
            // (Wrap only — Validate rejects quotes in JSON keys on ToSql).
            var backtick = new SqlSelect(new Table("child", "c"))
                .WithField(new Field("  ", "a`b"));
            Assert.That(
                NestedList.Wrap(SqlDbType.MYSQL, "SELECT 1 AS x FROM child c", backtick),
                Does.Contain("`a``b`"));

            var doubled = new SqlSelect(new Table("child", "c"))
                .WithField(new Field("  ", "a\"b"));
            Assert.That(
                NestedList.Wrap(SqlDbType.ORACLE, "SELECT 1 AS x FROM child c", doubled),
                Does.Contain("\"a\"\"b\""));
        }

        [Test]
        public void Wrap_EmptyFields_ThrowsForOracle()
        {
            var empty = new SqlSelect(new Table("child", "c"));
            Assert.Throws<InvalidOperationException>(() =>
                NestedList.Wrap(SqlDbType.ORACLE, "SELECT 1 AS x", empty));
        }

        [Test]
        public void ToCorrelateFilter_NullExpressions_CopiesNestedFiltersOnly()
        {
            var filter = new Filter
            {
                Filters = new System.Collections.Generic.List<Filter>
                {
                    new Filter().WithExpression(new Expression("c.parent_id", "p.id"))
                }
            };
            Filter correlated = NestedList.ToCorrelateFilter(filter, SqlDbType.POSTGRES);
            Assert.That(correlated.Expressions, Is.Null.Or.Empty);
            Assert.That(correlated.Filters, Has.Count.EqualTo(1));
        }

        [Test]
        public void Validate_NullCorrelate_Throws()
        {
            var nested = new NestedList
            {
                ResultAlias = "Children",
                Select = new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                Correlate = null,
                ElementType = typeof(ChildDto)
            };
            Assert.Throws<InvalidOperationException>(() => nested.ToSql(SqlDbType.SQLITE));
        }

        private sealed class ChildDto
        {
            public string Id { get; set; }
            public System.Collections.Generic.List<TagDto> Tags { get; set; }
        }

        private sealed class TagDto
        {
            public string Id { get; set; }
        }
    }
}
