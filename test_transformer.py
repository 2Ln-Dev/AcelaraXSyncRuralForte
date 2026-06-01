import pytest
from datetime import datetime, date
from transformer import transform, _convert_value


def test_transform_maps_columns_for_transac():
    rows = [{"CODI_TRA": "T001", "RAZA_TRA": "Cliente A", "DESC_MUN": "São Paulo", "ESTA_MUN": "SP"}]
    result = transform(rows, "transac")
    assert result[0]["codi_tra"] == "T001"
    assert result[0]["raza_tra"] == "Cliente A"
    assert "CODI_TRA" not in result[0]


def test_transform_renames_column_for_tabelas_preco():
    rows = [{"ID_CTAB": 42, "NOME": "Lista 2026", "SITU_CTAB": "A", "ORIGEM": "M"}]
    result = transform(rows, "tabelas_preco")
    assert result[0]["codi_tab"] == 42
    assert result[0]["nome_tab"] == "Lista 2026"
    assert "ID_CTAB" not in result[0]


def test_transform_handles_null_values():
    rows = [{"CODI_TRA": None, "RAZA_TRA": "X", "DESC_MUN": None, "ESTA_MUN": None}]
    result = transform(rows, "transac")
    assert result[0]["codi_tra"] is None


def test_transform_converts_datetime_to_iso():
    dt = datetime(2026, 5, 31, 14, 30, 0)
    assert _convert_value(dt) == "2026-05-31T14:30:00"


def test_transform_converts_date_to_iso():
    d = date(2026, 5, 31)
    assert _convert_value(d) == "2026-05-31"


def test_transform_converts_whole_float_to_int():
    assert _convert_value(100.0) == 100
    assert isinstance(_convert_value(100.0), int)


def test_transform_preserves_fractional_float():
    assert _convert_value(3.14) == 3.14


def test_transform_returns_empty_list_for_empty_input():
    result = transform([], "transac")
    assert result == []


def test_transform_multiple_rows():
    rows = [
        {"CODI_TRA": "T001", "RAZA_TRA": "A", "DESC_MUN": "SP", "ESTA_MUN": "SP"},
        {"CODI_TRA": "T002", "RAZA_TRA": "B", "DESC_MUN": "RJ", "ESTA_MUN": "RJ"},
    ]
    result = transform(rows, "transac")
    assert len(result) == 2
    assert result[1]["codi_tra"] == "T002"
