import time
from tables_spec import TABLES_SPEC

try:
    from supabase import create_client
except ModuleNotFoundError:
    create_client = None  # deferred; real runs need supabase installed


RETRY_DELAY = 5


class SupabaseAuthError(Exception):
    pass


class SupabaseWriter:
    def __init__(self, config):
        self.config = config
        self._client = None

    def connect(self):
        self._client = create_client(self.config.supabase_url, self.config.supabase_key)

    def upsert_batch(self, table_name, rows):
        if not rows:
            return {"upserted": 0, "error": None}

        spec = TABLES_SPEC[table_name]
        upsert_key = spec["upsert_key"]
        on_conflict = ",".join(upsert_key)
        supabase_table = spec["supabase_table"]

        for attempt in range(2):
            try:
                resp = (
                    self._client.table(supabase_table)
                    .upsert(rows, on_conflict=on_conflict)
                    .execute()
                )
                count = len(resp.data) if resp.data else 0
                return {"upserted": count, "error": None}
            except Exception as e:
                msg = str(e).lower()
                if "auth" in msg or "unauthorized" in msg or "403" in msg:
                    raise SupabaseAuthError(f"Auth failure on {table_name}: {e}") from e
                if attempt == 0:
                    time.sleep(RETRY_DELAY)
                    continue
                return {"upserted": 0, "error": str(e)}

    def upsert_table(self, table_name, rows, batch_size):
        total = 0
        errors = []
        for i in range(0, max(len(rows), 1), batch_size):
            batch = rows[i : i + batch_size]
            if not batch:
                break
            result = self.upsert_batch(table_name, batch)
            total += result["upserted"]
            if result["error"]:
                errors.append({"batch_start": i, "error": result["error"]})
        return {"total_upserted": total, "errors": errors}
