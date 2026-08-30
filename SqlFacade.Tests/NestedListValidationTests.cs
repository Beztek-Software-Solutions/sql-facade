// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using Beztek.Facade.Sql;
    using NUnit.Framework;
    using SqlDbType = Beztek.Facade.Sql.DbType;

    [TestFixture]
    public class NestedListValidationTests
    {
        [Test]
        public void ToSql_UnsupportedDbType_Throws()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

            Assert.Throws<ArgumentException>(() => nested.ToSql((SqlDbType)999));
        }

        [Test]
        public void ToSql_MissingChildFields_Throws()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")),
                new Expression("c.parent_id", "p.id"));

            Assert.Throws<InvalidOperationException>(() => nested.ToSql(SqlDbType.SQLITE));
        }

        [Test]
        public void ToCorrelateWhere_UnsupportedRelation_Throws()
        {
            var filter = new Filter().WithExpression(new Expression("c.id", new[] { "a", "b" }).WithRelation(Relation.In));
            Assert.Throws<InvalidOperationException>(() => NestedList.ToCorrelateFilter(filter));
        }

        [Test]
        public void JsonKeyFor_UsesAliasOrColumnTail()
        {
            Assert.That(NestedList.JsonKeyFor(new Field("c.id", "Id")), Is.EqualTo("Id"));
            Assert.That(NestedList.JsonKeyFor(new Field("c.label")), Is.EqualTo("label"));
            Assert.That(NestedList.JsonKeyFor(new Field("schema.table.column")), Is.EqualTo("column"));
        }

        [Test]
        public void WithNestedList_Null_Throws()
        {
            var select = new SqlSelect("parent");
            Assert.Throws<ArgumentNullException>(() => select.WithNestedList(null));
        }

        [Test]
        public void ToSql_InvalidJsonKeyInField_Throws()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c"))
                    .WithField(new Field("c.id", "bad'key")),
                new Expression("c.parent_id", "p.id"));

            Assert.Throws<InvalidOperationException>(() => nested.ToSql(SqlDbType.SQLITE));
        }

        [Test]
        public void ToCorrelateWhere_MissingColumnName_Throws()
        {
            var filter = new Filter().WithExpression(new Expression(null, "p.id"));
            Assert.Throws<InvalidOperationException>(() => NestedList.ToCorrelateFilter(filter));
        }

        [Test]
        public void Validate_MissingElementType_Throws()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"))
            {
                ElementType = null,
                ElementTypeName = null
            };

            Assert.Throws<InvalidOperationException>(() => nested.ToSql(SqlDbType.SQLITE));
        }

        private sealed class ChildDto
        {
            public string Id { get; set; }
        }
    }
}
