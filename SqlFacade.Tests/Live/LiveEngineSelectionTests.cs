// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System;
    using System.Collections.Generic;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class LiveEngineSelectionTests
    {
        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(LiveEngineSelection.EnvVar, null);
        }

        [Test]
        public void Resolve_Unset_ReturnsEmpty()
        {
            Environment.SetEnvironmentVariable(LiveEngineSelection.EnvVar, null);
            Assert.That(LiveEngineSelection.Resolve(), Is.Empty);
            Assert.That(LiveEngineSelection.IsConfigured, Is.False);
        }

        [Test]
        public void Resolve_SingleEngine_ReturnsOne()
        {
            Environment.SetEnvironmentVariable(LiveEngineSelection.EnvVar, "postgres");
            Assert.That(LiveEngineSelection.Resolve(), Is.EqualTo(new[] { DbType.POSTGRES }));
        }

        [Test]
        public void Resolve_Subset_ParsesAliases()
        {
            Environment.SetEnvironmentVariable(LiveEngineSelection.EnvVar, "pg, mssql, maria");
            Assert.That(
                LiveEngineSelection.Resolve(),
                Is.EqualTo(new[] { DbType.POSTGRES, DbType.SQLSERVER, DbType.MARIADB }));
        }

        [Test]
        public void Resolve_All_ReturnsEveryEngine()
        {
            Environment.SetEnvironmentVariable(LiveEngineSelection.EnvVar, "all");
            IReadOnlyList<DbType> engines = LiveEngineSelection.Resolve();
            Assert.That(engines, Does.Contain(DbType.SQLITE));
            Assert.That(engines, Does.Contain(DbType.POSTGRES));
            Assert.That(engines, Does.Contain(DbType.SQLSERVER));
            Assert.That(engines, Does.Contain(DbType.MYSQL));
            Assert.That(engines, Does.Contain(DbType.MARIADB));
            Assert.That(engines, Does.Contain(DbType.ORACLE));
            Assert.That(engines.Count, Is.EqualTo(6));
        }

        [Test]
        public void Resolve_UnknownToken_Throws()
        {
            Environment.SetEnvironmentVariable(LiveEngineSelection.EnvVar, "cosmos");
            Assert.Throws<ArgumentException>(() => LiveEngineSelection.Resolve());
        }
    }
}
