import pytest
from unittest.mock import MagicMock, patch
from supabase_writer import SupabaseWriter, SupabaseAuthError


@pytest.fixture
def config():
    cfg = MagicMock()
    cfg.supabase_url = "https://proj.supabase.co"
    cfg.supabase_key = "key123"
    return cfg


@pytest.fixture
def writer(config):
    w = SupabaseWriter(config)
    w._client = MagicMock()
    return w


def _mock_resp(data):
    resp = MagicMock()
    resp.data = data
    return resp


def test_upsert_batch_success(writer):
    rows = [{"codi_ten": "001", "codi_tra": "T001", "nome_tra": "A"}]
    writer._client.table.return_value.upsert.return_value.execute.return_value = _mock_resp(rows)

    result = writer.upsert_batch("transac", rows)

    assert result["upserted"] == 1
    assert result["error"] is None


def test_upsert_batch_uses_correct_table_and_key(writer):
    rows = [{"codi_ten": "001", "codi_tra": "T001", "nome_tra": "A"}]
    writer._client.table.return_value.upsert.return_value.execute.return_value = _mock_resp(rows)

    writer.upsert_batch("transac", rows)

    writer._client.table.assert_called_once_with("transac")
    upsert_call = writer._client.table.return_value.upsert
    _, kwargs = upsert_call.call_args
    assert "codi_ten" in kwargs["on_conflict"]
    assert "codi_tra" in kwargs["on_conflict"]


def test_upsert_batch_empty_rows_returns_zero(writer):
    result = writer.upsert_batch("transac", [])
    assert result["upserted"] == 0
    writer._client.table.assert_not_called()


def test_upsert_batch_retries_once_on_error(writer):
    rows = [{"codi_ten": "001", "codi_tra": "T001", "nome_tra": "A"}]
    execute = writer._client.table.return_value.upsert.return_value.execute
    execute.side_effect = [Exception("timeout"), _mock_resp(rows)]

    with patch("supabase_writer.time.sleep") as mock_sleep:
        result = writer.upsert_batch("transac", rows)

    mock_sleep.assert_called_once_with(5)
    assert result["upserted"] == 1


def test_upsert_batch_returns_error_after_two_failures(writer):
    rows = [{"codi_ten": "001", "codi_tra": "T001", "nome_tra": "A"}]
    execute = writer._client.table.return_value.upsert.return_value.execute
    execute.side_effect = Exception("constraint violation")

    with patch("supabase_writer.time.sleep"):
        result = writer.upsert_batch("transac", rows)

    assert result["upserted"] == 0
    assert "constraint" in result["error"]


def test_upsert_batch_raises_on_auth_error(writer):
    rows = [{"codi_ten": "001", "codi_tra": "T001", "nome_tra": "A"}]
    execute = writer._client.table.return_value.upsert.return_value.execute
    execute.side_effect = Exception("401 unauthorized")

    with pytest.raises(SupabaseAuthError):
        writer.upsert_batch("transac", rows)


def test_upsert_table_batches_rows(writer):
    rows = [{"codi_ten": "001", "codi_tra": f"T{i:03d}", "nome_tra": f"C{i}"} for i in range(5)]
    execute = writer._client.table.return_value.upsert.return_value.execute
    execute.return_value = _mock_resp(rows[:3])

    result = writer.upsert_table("transac", rows, batch_size=3)

    assert writer._client.table.call_count == 2
    assert result["total_upserted"] == 6  # 3 rows returned per batch
    assert result["errors"] == []


def test_upsert_table_collects_batch_errors(writer):
    rows = [{"codi_ten": "001", "codi_tra": "T001", "nome_tra": "A"}]
    execute = writer._client.table.return_value.upsert.return_value.execute
    execute.side_effect = Exception("db error")

    with patch("supabase_writer.time.sleep"):
        result = writer.upsert_table("transac", rows, batch_size=100)

    assert result["total_upserted"] == 0
    assert len(result["errors"]) == 1
