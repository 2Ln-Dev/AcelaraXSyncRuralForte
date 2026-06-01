import pytest
import sys
from types import ModuleType
from unittest.mock import MagicMock, patch


# Provide a cx_Oracle stub so oracle_reader can be imported without Oracle client
_cx_stub = ModuleType("cx_Oracle")


class _DBError(Exception):
    pass


_cx_stub.DatabaseError = _DBError
_cx_stub.makedsn = MagicMock(return_value="DSN")
_cx_stub.connect = MagicMock()
sys.modules.setdefault("cx_Oracle", _cx_stub)

import oracle_reader  # noqa: E402 — must come after stub registration
oracle_reader.cx_Oracle = _cx_stub  # point module at stub

from oracle_reader import OracleReader, OracleConnectionError  # noqa: E402


@pytest.fixture
def config():
    cfg = MagicMock()
    cfg.oracle_host = "localhost"
    cfg.oracle_port = 1521
    cfg.oracle_sid = "ORCL"
    cfg.oracle_user = "hubraiz"
    cfg.oracle_password = "secret"
    return cfg


@pytest.fixture
def reader(config):
    r = OracleReader(config)
    r._conn = MagicMock()
    return r


def test_fetch_returns_list_of_dicts(reader):
    cursor = MagicMock()
    cursor.fetchall.return_value = [("T001", "Cliente A", "São Paulo", "SP")]
    reader._conn.cursor.return_value = cursor

    rows = reader.fetch("transac")

    assert len(rows) == 1
    assert rows[0]["CODI_TRA"] == "T001"
    assert rows[0]["RAZA_TRA"] == "Cliente A"


def test_fetch_executes_correct_sql(reader):
    cursor = MagicMock()
    cursor.fetchall.return_value = []
    reader._conn.cursor.return_value = cursor

    reader.fetch("transac")

    sql_called = cursor.execute.call_args[0][0]
    assert "V_AX_CLIENTES" in sql_called
    assert "CODI_TRA" in sql_called


def test_fetch_retries_once_on_db_error(reader):
    cursor_fail = MagicMock()
    cursor_fail.execute.side_effect = _DBError("ORA-12170: timeout")
    cursor_ok = MagicMock()
    cursor_ok.fetchall.return_value = []
    reader._conn.cursor.side_effect = [cursor_fail, cursor_ok]

    with patch("oracle_reader.time.sleep") as mock_sleep:
        rows = reader.fetch("transac")

    mock_sleep.assert_called_once_with(5)
    assert rows == []


def test_fetch_raises_after_two_failures(reader):
    cursor_fail = MagicMock()
    cursor_fail.execute.side_effect = _DBError("ORA-12541: no listener")
    reader._conn.cursor.return_value = cursor_fail

    with patch("oracle_reader.time.sleep"):
        with pytest.raises(OracleConnectionError) as exc_info:
            reader.fetch("transac")

    assert "transac" in str(exc_info.value)


def test_connect_builds_dsn_and_connects(config):
    _cx_stub.connect.reset_mock()
    mock_conn = MagicMock()
    _cx_stub.connect.return_value = mock_conn

    reader = OracleReader(config)
    reader.connect()

    call_kwargs = _cx_stub.connect.call_args.kwargs
    assert call_kwargs["user"] == "hubraiz"
    assert "localhost" in call_kwargs["dsn"]
    assert "1521" in call_kwargs["dsn"]
    _cx_stub.connect.assert_called_once()
    assert reader._conn is mock_conn


def test_disconnect_closes_and_clears(config):
    reader = OracleReader(config)
    reader._conn = MagicMock()
    conn = reader._conn

    reader.disconnect()

    conn.close.assert_called_once()
    assert reader._conn is None
