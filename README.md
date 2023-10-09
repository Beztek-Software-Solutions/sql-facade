# Beztek.Facade.Sql Library

This library is intended for providing an facade ORM layer over SQL Databases. It uses SQLKata, and thus enables a level of abstraction over the nuances and particular syntaxes of various databases.

# Overview

It is intended to be cloud portable and take advantage of the native managed services in each cloud, such as managed Postgres DBs or managed Sql Server DBs.
It is a reusable and configurable sql facade library.

## Steps to use Sql Facade

1. Find SQL connection string. It currently supports Postgres SQL, Sql Server and SQLite (in-memory and file-based)/
2. Instantiate the SqlFacade object from the SqlFacadeFactory, using the appropriate SqlFacadeConfig object

## Sample Project

The solution contains a sample project that you can modify and run to test out different use cases and scenarios. Simply set it as the startup project and then run. The unit tests also provide examples of how to use this library.

### Useful ways to use this library

1. Local development can use a SQLite DB file, and when deployed it can use DBs such as Postgres or Sql Server, which could be managed cloud DBs as well. SQLite can be isntantiated in-memory as in the example project and the unit tests. This enables quick-and-dirty offline development without the need of a full database.
