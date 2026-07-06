import sys
from config import Config, ConfigError
from logger import Logger
from oracle_reader import OracleReader, OracleConnectionError
from api_writer import ApiWriter, ApiAuthError, ENTITY_TO_SLUG
from transformer import transform
from tables_spec import SYNC_ORDER

# A tabela de preço é enviada num endpoint combinado (header + itens), então
# sai do loop das entidades simples e é tratada à parte, no fim.
PRECO_HEADER = "tabelas_preco"
PRECO_ITENS = "itens_tabela_preco"


def run_sync(config, logger, oracle, writer):
    exit_code = 0
    stop = False

    # 1. Entidades simples: 1 tabela = 1 endpoint. codi_ten NÃO é enviado
    #    (vem da API key); o servidor faz o upsert idempotente.
    for entity in SYNC_ORDER:
        if entity in (PRECO_HEADER, PRECO_ITENS):
            continue

        logger.info(table=entity, status="start")
        try:
            rows = oracle.fetch(entity)
        except OracleConnectionError as e:
            logger.error(table=entity, error_type="oracle_connection", detail=str(e))
            exit_code = 1
            continue

        transformed = transform(rows, entity)
        try:
            result = writer.post_items(ENTITY_TO_SLUG[entity], transformed)
        except ApiAuthError as e:
            logger.error(table=entity, error_type="auth_failure", detail=str(e))
            exit_code = 1
            stop = True
            break

        if result["errors"]:
            for err in result["errors"]:
                logger.error(table=entity, error_type="api_post",
                             detail=err["error"], batch_start=err["batch_start"])
            exit_code = 1

        logger.info(table=entity, status="success",
                    rows_fetched=len(rows), rows_upserted=result["saved"])

    # 2. Tabela de preço (header + itens no mesmo request). O servidor resolve
    #    erp_id -> codi_tab e ignora itens órfãos (reportados aqui).
    if not stop:
        logger.info(table=PRECO_HEADER, status="start")
        try:
            headers = transform(oracle.fetch(PRECO_HEADER), PRECO_HEADER)
            items = transform(oracle.fetch(PRECO_ITENS), PRECO_ITENS)
        except OracleConnectionError as e:
            logger.error(table=PRECO_HEADER, error_type="oracle_connection", detail=str(e))
            exit_code = 1
        else:
            try:
                result = writer.post_tabelas_preco(headers, items)
            except ApiAuthError as e:
                logger.error(table=PRECO_HEADER, error_type="auth_failure", detail=str(e))
                exit_code = 1
            else:
                if result["errors"]:
                    for err in result["errors"]:
                        logger.error(table=PRECO_HEADER, error_type="api_post",
                                     detail=err["error"], batch_start=err["batch_start"])
                    exit_code = 1
                if result.get("orphans"):
                    logger.error(table=PRECO_ITENS, error_type="orphan_items",
                                 detail=f"{result['orphans']} itens sem tabela de preço correspondente")
                    exit_code = 1
                logger.info(table=PRECO_HEADER, status="success",
                            rows_fetched=len(headers) + len(items),
                            rows_upserted=result["headers_saved"] + result["items_saved"])

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
    writer = ApiWriter(config)

    try:
        oracle.connect()
        writer.connect()
        exit_code = run_sync(config, logger, oracle, writer)
    finally:
        oracle.disconnect()

    sys.exit(exit_code)


if __name__ == "__main__":
    main()
