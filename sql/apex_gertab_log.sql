-- ============================================================
-- apex_gertab_log.sql
--
-- Log + idempotência da geração de Tabela de Preço via integração
-- (POST /gerar-tabela do acelera-x-sync).
--
-- Chave de idempotência: ID_GER + MOEDA_ALVO + COMMODITY + CONDICAO + RT_REAIS + RT_DOLAR.
-- Se já existe log GERADA com essa chave, a API devolve o(s) TABE_CTA existentes
-- em vez de gerar de novo. Uma geração produz 1-2 linhas (origem R$=1 / US$=2).
--
-- Idempotente: roda várias vezes sem erro (ignora ORA-00955 se já existir).
-- ============================================================

BEGIN
  EXECUTE IMMEDIATE q'[
    CREATE TABLE AGROINSU.APEX_GERTAB_LOG (
      ID              NUMBER        NOT NULL,
      DATA_HORA       TIMESTAMP     DEFAULT SYSTIMESTAMP NOT NULL,
      ID_GER          NUMBER,
      CICLO           VARCHAR2(60),
      MOEDA_ALVO      VARCHAR2(10),
      COMMODITY       NUMBER,
      CONDICAO        VARCHAR2(10),
      ORIGEM          VARCHAR2(5),
      ORIGEM_COD      NUMBER(1),
      RT_REAIS        NUMBER,
      RT_DOLAR        NUMBER,
      CAMBIO          NUMBER,
      TABE_CTA        NUMBER,
      QTD_ITENS       NUMBER,
      STATUS          VARCHAR2(20),
      ORIGEM_CHAMADA  VARCHAR2(80),
      CONSTRAINT PK_APEX_GERTAB_LOG PRIMARY KEY (ID)
    )
  ]';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE = -955 THEN NULL;   -- ORA-00955: nome já em uso (tabela já existe)
    ELSE RAISE;
    END IF;
END;
/

-- Índice para a consulta de idempotência
BEGIN
  EXECUTE IMMEDIATE
    'CREATE INDEX AGROINSU.IX_GERTAB_LOG_CHAVE ON AGROINSU.APEX_GERTAB_LOG ' ||
    '(ID_GER, MOEDA_ALVO, COMMODITY, CONDICAO, RT_REAIS, RT_DOLAR)';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE = -955 THEN NULL;   -- índice já existe
    ELSE RAISE;
    END IF;
END;
/

-- ---------- VERIFICAÇÃO ----------
SELECT COUNT(*) AS TEM_TABELA FROM ALL_TABLES
WHERE OWNER = 'AGROINSU' AND TABLE_NAME = 'APEX_GERTAB_LOG';
