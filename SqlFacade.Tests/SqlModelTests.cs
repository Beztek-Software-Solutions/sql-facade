// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class SqlModelTests
    {
        [Test]
        public void JoinType_EqualsAndToString()
        {
            Assert.That(JoinType.InnerJoin.ToString(), Is.EqualTo("InnerJoin"));
            Assert.That(JoinType.InnerJoin, Is.EqualTo(JoinType.InnerJoin));
            Assert.That(JoinType.InnerJoin, Is.Not.EqualTo(JoinType.LeftJoin));
            Assert.That(JoinType.LeftJoin.GetHashCode(), Is.Not.EqualTo(JoinType.InnerJoin.GetHashCode()));
        }

        [Test]
        public void SqlRelation_EqualsAndHashCode()
        {
            Assert.That(SqlRelation.Union.Value, Is.EqualTo("Union"));
            Assert.That(SqlRelation.UnionAll, Is.EqualTo(SqlRelation.UnionAll));
            Assert.That(SqlRelation.Except, Is.Not.EqualTo(SqlRelation.Intersect));
        }

        [Test]
        public void LogicalRelation_EqualsAndHashCode()
        {
            Assert.That(LogicalRelation.And.Value, Is.EqualTo("And"));
            Assert.That(LogicalRelation.Or, Is.EqualTo(LogicalRelation.Or));
            Assert.That(LogicalRelation.AndNot, Is.Not.EqualTo(LogicalRelation.OrNot));
        }

        [Test]
        public void Relation_EqualsAndToString()
        {
            Assert.That(Relation.EqualTo.ToString(), Is.EqualTo("="));
            Assert.That(Relation.In, Is.EqualTo(Relation.In));
            Assert.That(Relation.Contains, Is.Not.EqualTo(Relation.StartsWith));
        }

        [Test]
        public void Filter_WithFilter_Nests()
        {
            Filter inner = new Filter().WithExpression(new Expression("a", 1));
            Filter outer = new Filter().WithFilter(inner);
            Assert.That(outer.Filters, Has.Count.EqualTo(1));
        }

        [Test]
        public void GroupBy_RawValue_Compiles()
        {
            ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:"));
            var select = new SqlSelect("canvas")
                .WithField(new Field("count(*)", "c", true))
                .WithGroupBy(new GroupBy("substr(id, 1, 4)", isRaw: true));
            Assert.That(sql.GetSql(select, false), Does.Contain("substr").IgnoreCase);
        }
    }
}
