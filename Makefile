# Copyright (c) Beztek Software Solutions. All rights reserved.

# SQL Facade — unit tests (+ optional live via SQLFACADE_LIVE_ENGINES).
#
#   make test
#   make test-unit
#   make coverage
#   make coverage-html

SHELL := /bin/bash
.SHELLFLAGS := -euo pipefail -c

SOLUTION := sql-facade.sln
TESTS_PROJECT := SqlFacade.Tests/Beztek.Facade.Sql.Test.csproj
ASSEMBLY_INCLUDE := [Beztek.Facade.Sql]*

COVERAGE_DIR := $(CURDIR)/coverage
COVERAGE_THRESHOLD ?= 0
OPEN_CMD ?= open

# Unit-only by default (no Testcontainers). Override: COVERAGE_FILTER=
COVERAGE_FILTER ?= Category!=Live

.DEFAULT_GOAL := help

.PHONY: help
help:
	@echo "SQL Facade"
	@echo "  make test              - unit tests (exclude Category=Live)"
	@echo "  make test-unit         - same as test"
	@echo "  make test-live         - Category=Live (needs SQLFACADE_LIVE_ENGINES)"
	@echo "  make coverage          - Coverlet + terminal summary (unit by default)"
	@echo "  make coverage-html     - HTML report opened in a browser"
	@echo "  make coverage-check    - fail if line coverage < COVERAGE_THRESHOLD (default 0→85)"
	@echo "  make clean             - drop build output and coverage/"
	@echo "  make tools             - restore local dotnet tools (reportgenerator)"
	@echo ""
	@echo "Coverage overrides: COVERAGE_FILTER=  COVERAGE_THRESHOLD=85"

.PHONY: tools
tools:
	dotnet tool restore

.PHONY: restore
restore:
	dotnet restore $(SOLUTION)

.PHONY: build
build: restore
	dotnet build $(SOLUTION) --no-restore

.PHONY: test test-unit
test test-unit: build
	dotnet test $(TESTS_PROJECT) --no-build --filter "Category!=Live"

.PHONY: test-live
test-live: build
	@test -n "$${SQLFACADE_LIVE_ENGINES:-}" || { echo "Set SQLFACADE_LIVE_ENGINES (e.g. sqlite,postgres,all)"; exit 1; }
	dotnet test $(TESTS_PROJECT) --no-build --filter "Category=Live"

.PHONY: coverage
coverage: tools
	@rm -rf "$(COVERAGE_DIR)"
	@mkdir -p "$(COVERAGE_DIR)"
	dotnet test $(TESTS_PROJECT) \
		$(if $(COVERAGE_FILTER),--filter "$(COVERAGE_FILTER)",) \
		/p:CollectCoverage=true \
		/p:CoverletOutputFormat=cobertura \
		/p:CoverletOutput="$(COVERAGE_DIR)/" \
		/p:Include='$(ASSEMBLY_INCLUDE)' \
		/p:Threshold=$(COVERAGE_THRESHOLD) \
		/p:ThresholdType=line
	@dotnet reportgenerator \
		"-reports:$(COVERAGE_DIR)/coverage.cobertura.xml" \
		"-targetdir:$(COVERAGE_DIR)/report" \
		-reporttypes:TextSummary \
		-verbosity:Warning
	@cat "$(COVERAGE_DIR)/report/Summary.txt"

.PHONY: coverage-html
coverage-html: coverage
	@dotnet reportgenerator \
		"-reports:$(COVERAGE_DIR)/coverage.cobertura.xml" \
		"-targetdir:$(COVERAGE_DIR)/html" \
		-reporttypes:Html \
		-verbosity:Warning
	@echo "opening $(COVERAGE_DIR)/html/index.html"
	@$(OPEN_CMD) "$(COVERAGE_DIR)/html/index.html" 2>/dev/null \
		|| xdg-open "$(COVERAGE_DIR)/html/index.html" 2>/dev/null \
		|| true

.PHONY: coverage-check
coverage-check: coverage
	@line=$$(sed -n 's/^[[:space:]]*Line coverage:[[:space:]]*\([0-9.]*\)%.*/\1/p' "$(COVERAGE_DIR)/report/Summary.txt" | head -1); \
	want="$(COVERAGE_THRESHOLD)"; \
	if [ -z "$$line" ]; then echo "could not parse line coverage from Summary.txt"; exit 1; fi; \
	if [ "$$want" = "0" ]; then want=85; fi; \
	awk -v got="$$line" -v w="$$want" 'BEGIN { \
		if (got+0 < w+0) { printf "FAIL: line coverage %.1f%% < %s%%\n", got, w; exit 1 } \
		printf "OK: line coverage %.1f%% >= %s%%\n", got, w; exit 0 }'

.PHONY: clean
clean:
	dotnet clean $(SOLUTION)
	rm -rf "$(COVERAGE_DIR)"
