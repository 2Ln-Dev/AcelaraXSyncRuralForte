from datetime import datetime, date
from tables_spec import TABLES_SPEC


def _convert_value(value):
    if value is None:
        return None
    if isinstance(value, datetime):
        return value.isoformat()
    if isinstance(value, date):
        return value.isoformat()
    if isinstance(value, float) and value == int(value):
        return int(value)
    return value


def transform(rows, table_name):
    spec = TABLES_SPEC[table_name]
    col_map = spec["columns"]
    result = []
    for row in rows:
        mapped = {}
        for oracle_col, supabase_col in col_map.items():
            raw = row.get(oracle_col)
            mapped[supabase_col] = _convert_value(raw)
        result.append(mapped)
    return result
