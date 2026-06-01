import time
from tables_spec import TABLES_SPEC

try:
    import cx_Oracle
except ModuleNotFoundError:
    try:
        import oracledb as cx_Oracle
        cx_Oracle.init_oracle_client()  # thick mode — requires Oracle Instant Client in PATH
    except Exception:
        cx_Oracle = None


RETRY_DELAY = 5


class OracleConnectionError(Exception):
    pass


def _is_db_error(exc):
    if cx_Oracle is None:
        return False
    return isinstance(exc, cx_Oracle.DatabaseError)


class OracleReader:
    def __init__(self, config):
        self.config = config
        self._conn = None

    def connect(self):
        dsn = (
            f"(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)"
            f"(HOST={self.config.oracle_host})(PORT={self.config.oracle_port})))"
            f"(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME={self.config.oracle_sid})))"
        )
        self._conn = cx_Oracle.connect(
            user=self.config.oracle_user,
            password=self.config.oracle_password,
            dsn=dsn,
        )

    def disconnect(self):
        if self._conn:
            self._conn.close()
            self._conn = None

    def fetch(self, table_name):
        spec = TABLES_SPEC[table_name]
        view = spec["oracle_view"]
        columns = list(spec["columns"].keys())
        col_list = ", ".join(columns)
        sql = f"SELECT {col_list} FROM {view}"

        last_exc = None
        for attempt in range(2):
            try:
                cursor = self._conn.cursor()
                cursor.execute(sql)
                rows = [dict(zip(columns, row)) for row in cursor.fetchall()]
                cursor.close()
                return rows
            except Exception as e:
                if not _is_db_error(e):
                    raise
                last_exc = e
                if attempt == 0:
                    time.sleep(RETRY_DELAY)

        raise OracleConnectionError(
            f"Oracle fetch failed for {table_name}: {last_exc}"
        ) from last_exc
