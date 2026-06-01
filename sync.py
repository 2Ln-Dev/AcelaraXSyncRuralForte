import sys
from config import Config, ConfigError
from logger import Logger
from oracle_reader import OracleReader, OracleConnectionError
from supabase_writer import SupabaseWriter, SupabaseAuthError
from transformer import transform
from tables_spec import SYNC_ORDER, TABLES_SPEC


def run_sync(config, logger, oracle, writer):
    exit_code = 0

    for table_name in SYNC_ORDER:
        logger.info(table=table_name, status="start")
        try:
            rows = oracle.fetch(table_name)
        except OracleConnectionError as e:
            logger.error(table=table_name, error_type="oracle_connection", detail=str(e))
            exit_code = 1
            continue

        transformed = transform(rows, table_name)
        for row in transformed:
            row["codi_ten"] = config.codi_ten

        spec = TABLES_SPEC[table_name]
        if spec.get("is_header"):
            upsert_key = spec["upsert_key"]
            seen: set = set()
            deduped = []
            for row in transformed:
                key = tuple(row.get(k) for k in upsert_key)
                if key not in seen:
                    seen.add(key)
                    deduped.append(row)
            transformed = deduped

        try:
            result = writer.upsert_table(table_name, transformed, config.batch_size)
        except SupabaseAuthError as e:
            logger.error(table=table_name, error_type="auth_failure", detail=str(e))
            exit_code = 1
            break

        if result["errors"]:
            for err in result["errors"]:
                logger.error(
                    table=table_name,
                    error_type="upsert_batch",
                    detail=err["error"],
                    batch_start=err["batch_start"],
                )
            exit_code = 1

        logger.info(
            table=table_name,
            status="success",
            rows_fetched=len(rows),
            rows_upserted=result["total_upserted"],
        )

    logger.summary(exit_code=exit_code)
    return exit_code


def main():
    try:
        config = Config()
    except ConfigError as e:
        print(f"Config error: {e}", file=sys.stderr)
        sys.exit(1)

    logger = Logger(config.log_file_path)
    oracle = OracleReader(config)
    writer = SupabaseWriter(config)

    try:
        oracle.connect()
        writer.connect()
        exit_code = run_sync(config, logger, oracle, writer)
    finally:
        oracle.disconnect()

    sys.exit(exit_code)


if __name__ == "__main__":
    main()
