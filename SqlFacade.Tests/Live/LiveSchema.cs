// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using Beztek.Facade.Sql;
    using SqlDbType = Beztek.Facade.Sql.DbType;

    /// <summary>Creates and resets the shared live-test schema for each engine.</summary>
    public static class LiveSchema
    {
        public static void Ensure(ISqlFacade sql)
        {
            foreach (string ddl in CreateStatements(sql.GetSqlFacadeConfig().DbType))
                ExecuteRaw(sql, ddl);
        }

        public static void Reset(ISqlFacade sql)
        {
            SqlDbType dbType = sql.GetSqlFacadeConfig().DbType;
            foreach (string table in new[] { "stroke_tag", "canvas_stroke", "canvas", "entity" })
            {
                try
                {
                    ExecuteRaw(sql, DeleteStatement(dbType, table));
                }
                catch (Exception)
                {
                    // Table may not exist yet on first run.
                }
            }
        }

        public static object GuidInsertValue(SqlDbType dbType, Guid id) =>
            dbType is SqlDbType.POSTGRES or SqlDbType.SQLSERVER ? id : (object)id.ToString("D");

        private static IEnumerable<string> CreateStatements(SqlDbType dbType) => dbType switch
        {
            SqlDbType.POSTGRES => new[]
            {
                "CREATE TABLE IF NOT EXISTS canvas (id TEXT PRIMARY KEY, color TEXT, ordering INT)",
                "CREATE TABLE IF NOT EXISTS canvas_stroke (id TEXT PRIMARY KEY, canvas_id TEXT NOT NULL, label TEXT, sort_ord INT)",
                "CREATE TABLE IF NOT EXISTS stroke_tag (id TEXT PRIMARY KEY, stroke_id TEXT NOT NULL, tag TEXT)",
                "CREATE TABLE IF NOT EXISTS entity (id UUID PRIMARY KEY, name TEXT)",
            },
            SqlDbType.SQLSERVER => new[]
            {
                @"IF OBJECT_ID('dbo.canvas', 'U') IS NULL CREATE TABLE dbo.canvas (id NVARCHAR(64) NOT NULL PRIMARY KEY, color NVARCHAR(64) NULL, ordering INT NULL)",
                @"IF OBJECT_ID('dbo.canvas_stroke', 'U') IS NULL CREATE TABLE dbo.canvas_stroke (id NVARCHAR(64) NOT NULL PRIMARY KEY, canvas_id NVARCHAR(64) NOT NULL, label NVARCHAR(128) NULL, sort_ord INT NULL)",
                @"IF OBJECT_ID('dbo.stroke_tag', 'U') IS NULL CREATE TABLE dbo.stroke_tag (id NVARCHAR(64) NOT NULL PRIMARY KEY, stroke_id NVARCHAR(64) NOT NULL, tag NVARCHAR(128) NULL)",
                @"IF OBJECT_ID('dbo.entity', 'U') IS NULL CREATE TABLE dbo.entity (id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, name NVARCHAR(64) NULL)",
            },
            SqlDbType.MYSQL or SqlDbType.MARIADB => new[]
            {
                "CREATE TABLE IF NOT EXISTS canvas (id VARCHAR(64) PRIMARY KEY, color VARCHAR(64), ordering INT)",
                "CREATE TABLE IF NOT EXISTS canvas_stroke (id VARCHAR(64) PRIMARY KEY, canvas_id VARCHAR(64) NOT NULL, label VARCHAR(128), sort_ord INT)",
                "CREATE TABLE IF NOT EXISTS stroke_tag (id VARCHAR(64) PRIMARY KEY, stroke_id VARCHAR(64) NOT NULL, tag VARCHAR(128))",
                "CREATE TABLE IF NOT EXISTS entity (id CHAR(36) PRIMARY KEY, name VARCHAR(64))",
            },
            SqlDbType.ORACLE => new[]
            {
                // SqlKata quotes lowercase identifiers — table and columns must be created quoted.
                // Drop both quoted and unquoted leftovers from earlier runs, then recreate.
                """BEGIN EXECUTE IMMEDIATE 'DROP TABLE "canvas" CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN NULL; END;""",
                """BEGIN EXECUTE IMMEDIATE 'DROP TABLE canvas CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN NULL; END;""",
                """BEGIN EXECUTE IMMEDIATE 'DROP TABLE "canvas_stroke" CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN NULL; END;""",
                """BEGIN EXECUTE IMMEDIATE 'DROP TABLE canvas_stroke CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN NULL; END;""",
                """BEGIN EXECUTE IMMEDIATE 'DROP TABLE "stroke_tag" CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN NULL; END;""",
                """BEGIN EXECUTE IMMEDIATE 'DROP TABLE stroke_tag CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN NULL; END;""",
                """BEGIN EXECUTE IMMEDIATE 'DROP TABLE "entity" CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN NULL; END;""",
                """BEGIN EXECUTE IMMEDIATE 'DROP TABLE entity CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN NULL; END;""",
                """BEGIN EXECUTE IMMEDIATE 'CREATE TABLE "canvas" ("id" VARCHAR2(64) PRIMARY KEY, "color" VARCHAR2(64), "ordering" NUMBER)'; END;""",
                """BEGIN EXECUTE IMMEDIATE 'CREATE TABLE "canvas_stroke" ("id" VARCHAR2(64) PRIMARY KEY, "canvas_id" VARCHAR2(64) NOT NULL, "label" VARCHAR2(128), "sort_ord" NUMBER)'; END;""",
                """BEGIN EXECUTE IMMEDIATE 'CREATE TABLE "stroke_tag" ("id" VARCHAR2(64) PRIMARY KEY, "stroke_id" VARCHAR2(64) NOT NULL, "tag" VARCHAR2(128))'; END;""",
                """BEGIN EXECUTE IMMEDIATE 'CREATE TABLE "entity" ("id" CHAR(36) PRIMARY KEY, "name" VARCHAR2(64))'; END;""",
            },
            SqlDbType.SQLITE => new[]
            {
                "CREATE TABLE IF NOT EXISTS canvas (id TEXT PRIMARY KEY, color TEXT, ordering INT)",
                "CREATE TABLE IF NOT EXISTS canvas_stroke (id TEXT PRIMARY KEY, canvas_id TEXT NOT NULL, label TEXT, sort_ord INT)",
                "CREATE TABLE IF NOT EXISTS stroke_tag (id TEXT PRIMARY KEY, stroke_id TEXT NOT NULL, tag TEXT)",
                "CREATE TABLE IF NOT EXISTS entity (id TEXT PRIMARY KEY, name TEXT)",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, null),
        };

        private static string DeleteStatement(SqlDbType dbType, string table) => dbType switch
        {
            SqlDbType.SQLSERVER => $"DELETE FROM dbo.{table}",
            SqlDbType.ORACLE => $"DELETE FROM \"{table}\"",
            _ => $"DELETE FROM {table}",
        };

        private static void ExecuteRaw(ISqlFacade sql, string commandText)
        {
            using IDbConnection con = sql.GetSqlFacadeConfig().GetConnection();
            using IDbCommand cmd = con.CreateCommand();
            cmd.CommandText = commandText;
            cmd.ExecuteNonQuery();
        }
    }
}
