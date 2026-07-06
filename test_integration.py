import pytest
from unittest.mock import MagicMock
from sync import run_sync, PRECO_HEADER, PRECO_ITENS
from oracle_reader import OracleConnectionError
from api_writer import ApiAuthError
from tables_spec import SYNC_ORDER

# Entidades simples (endpoint próprio) e a ordem em que o sync as toca:
# primeiro as simples (na ordem do SYNC_ORDER), depois header+itens de preço.
SIMPLE = [e for e in SYNC_ORDER if e not in (PRECO_HEADER, PRECO_ITENS)]
EXPECTED_FETCH_ORDER = SIMPLE + [PRECO_HEADER, PRECO_ITENS]


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
    w.post_items.return_value = {"sent": 0, "saved": 0, "errors": []}
    w.post_tabelas_preco.return_value = {
        "headers_saved": 0, "items_saved": 0, "orphans": 0, "errors": []
    }
    return w


def test_sync_all_tables_success(config, logger, oracle, writer):
    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 0
    assert oracle.fetch.call_count == len(SYNC_ORDER)  # simples + header + itens
    assert writer.post_items.call_count == len(SIMPLE)
    writer.post_tabelas_preco.assert_called_once()
    logger.summary.assert_called_once_with(exit_code=0)


def test_sync_respects_order(config, logger, oracle, writer):
    called = []
    oracle.fetch.side_effect = lambda t: called.append(t) or []

    run_sync(config, logger, oracle, writer)

    assert called == EXPECTED_FETCH_ORDER


def test_sync_continues_on_oracle_error(config, logger, oracle, writer):
    oracle.fetch.side_effect = OracleConnectionError("timeout")

    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 1
    logger.error.assert_called()
    writer.post_items.assert_not_called()
    writer.post_tabelas_preco.assert_not_called()


def test_sync_stops_on_auth_error(config, logger, oracle, writer):
    writer.post_items.side_effect = ApiAuthError("401 unauthorized")

    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 1
    # Interrompe na primeira entidade; não segue para as demais nem para a preço.
    assert writer.post_items.call_count == 1
    writer.post_tabelas_preco.assert_not_called()


def test_sync_logs_api_errors(config, logger, oracle, writer):
    writer.post_items.return_value = {
        "sent": 0, "saved": 0,
        "errors": [{"batch_start": 0, "error": "HTTP 422 em clientes"}],
    }

    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 1
    logger.error.assert_called()


def test_sync_reports_orphan_price_items(config, logger, oracle, writer):
    writer.post_tabelas_preco.return_value = {
        "headers_saved": 1, "items_saved": 2, "orphans": 3, "errors": []
    }

    exit_code = run_sync(config, logger, oracle, writer)

    assert exit_code == 1
    logger.error.assert_called()


def test_sync_passes_correct_slug_to_writer(config, logger, oracle, writer):
    from api_writer import ENTITY_TO_SLUG
    slugs = []
    writer.post_items.side_effect = lambda slug, rows: slugs.append(slug) or {
        "sent": 0, "saved": 0, "errors": []
    }

    run_sync(config, logger, oracle, writer)

    assert slugs == [ENTITY_TO_SLUG[e] for e in SIMPLE]
