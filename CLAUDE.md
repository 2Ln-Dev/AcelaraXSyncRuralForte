# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Run all tests
venv\Scripts\python.exe -m pytest . --ignore=venv -v

# Run a single test file
venv\Scripts\python.exe -m pytest test_transformer.py -v

# Run a single test by name
venv\Scripts\python.exe -m pytest test_transformer.py::test_transform_renames_column_for_tabelas_preco -v

# Run the sync manually (requires .env with real credentials)
venv\Scripts\python.exe sync.py
```

## Architecture

`sync.py` is the entrypoint and orchestrator. It loads config, opens connections, iterates `SYNC_ORDER` from `tables_spec.py`, and calls the three pipeline stages in sequence for each table: fetch → transform → upsert.

**Pipeline stages:**
- `oracle_reader.py` — connects to Oracle via `cx_Oracle`, executes `SELECT` on HUBRAIZ views, returns `list[dict]`. Retries once (5s delay) on `cx_Oracle.DatabaseError` before raising `OracleConnectionError`.
- `transformer.py` — maps Oracle column names to Supabase names and converts types (datetime → ISO string, whole float → int). Mapping is driven by the `columns` dict in each table's spec.
- `supabase_writer.py` — splits rows into `batch_size` chunks and calls `.upsert(on_conflict=...)` per batch. Retries once on timeout; raises `SupabaseAuthError` immediately on 401/403 (stops entire sync).

**Central config: `tables_spec.py`**

All table behaviour is declared here: Oracle view name, Supabase table name, UPSERT conflict key, and the Oracle→Supabase column map. Adding or changing a table only requires editing this file — no changes to the pipeline modules.

**`cx_Oracle` is optional at import time.** The real Oracle client is not available in CI/dev environments, so `oracle_reader.py` wraps the import in `try/except ModuleNotFoundError` and sets `cx_Oracle = None`. Tests stub the module via `sys.modules` before importing `oracle_reader`. Do not change this pattern.

**Test locations:** `tests/` holds `test_config.py` and `test_logger.py`; the remaining test files live at the project root. Both locations are auto-discovered by pytest.

**Error handling contract in `sync.py`:**
- `OracleConnectionError` → log, skip table, continue to next table
- `SupabaseAuthError` → log, break out of loop entirely
- Batch errors from `upsert_table` → log each, set exit code 1, continue
