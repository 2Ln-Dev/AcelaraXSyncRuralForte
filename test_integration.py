import pytest
from unittest.mock import MagicMock, patch
from sync import run_sync
from oracle_reader import OracleConnectionError
from supabase_writer import SupabaseAuthError


@pytest.fixture
def config():
    cfg = MagicMock()
    cfg.batch_size = 1000
    return cfg


@pytest.fixture
def logger():
    return MagicMock()


@pytest.fixture
def oracle():
    r = MagicMock()
    r.fetch.return_value = []
    return r


@pytest.fixture
def writer():
    w = MagicMock()
    w.upsert_table.return_value = {"total_upserted": 0, "errors": []}
    return w


def test_sync_all_tables_success(config, logger, oracle, writer):
    from tables_spec import SYNC_ORDER
    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 0
    assert oracle.fetch.call_count == len(SYNC_ORDER)
    assert writer.upsert_table.call_count == len(SYNC_ORDER)
    logger.summary.assert_called_once_with(exit_code=0)


def test_sync_respects_order(config, logger, oracle, writer):
    from tables_spec import SYNC_ORDER
    called_tables = []
    oracle.fetch.side_effect = lambda t: called_tables.append(t) or []

    run_sync(config, logger, oracle, writer)

    assert called_tables == SYNC_ORDER


def test_sync_continues_on_oracle_error(config, logger, oracle, writer):
    oracle.fetch.side_effect = OracleConnectionError("timeout")

    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 1
    logger.error.assert_called()
    writer.upsert_table.assert_not_called()


def test_sync_stops_on_auth_error(config, logger, oracle, writer):
    from tables_spec import SYNC_ORDER
    writer.upsert_table.side_effect = SupabaseAuthError("401 unauthorized")

    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 1
    # Should stop immediately, not process all tables
    assert writer.upsert_table.call_count == 1


def test_sync_logs_batch_errors(config, logger, oracle, writer):
    writer.upsert_table.return_value = {
        "total_upserted": 0,
        "errors": [{"batch_start": 0, "error": "constraint violation"}],
    }

    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 1
    logger.error.assert_called()


def test_sync_passes_correct_table_to_writer(config, logger, oracle, writer):
    called_with = []
    original = writer.upsert_table.side_effect
    writer.upsert_table.side_effect = lambda t, rows, bs: called_with.append(t) or {"total_upserted": 0, "errors": []}

    run_sync(config, logger, oracle, writer)

    from tables_spec import SYNC_ORDER
    assert called_with == SYNC_ORDER
