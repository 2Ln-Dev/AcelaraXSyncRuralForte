-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- estoque  ->  SALDO_INICIAL por tipo de controle (CTRLSALDO):
--    1 = estoque fisico (EF)      9 = remessa dep/arm emitida (RDE)
--    2 = pedido de compra nao recebido (PCNR)   3 = pedido de venda nao entregue (PVNE)
-- DISPONIVEL = EF + PCNR - PVNE + RDE.
--
-- Oracle 11.2 nao tem CROSS JOIN LATERAL (a versao da AgroSanta usa): os saldos sao
-- subconsultas escalares numa inline view, e o DISPONIVEL e' somado por fora.
-- SUM(ESTOQUE) e' a forma como o HUB le a SALDO_INICIAL (EstoqueModule.cs).
--
-- CUSTO: ~0,06 s por linha na Rural (TREINO: 9.625 linhas em 9,4 min, antes do escopo).
-- ESCOPO: todas as filiais ativas (inclui a 50); grupos 49/50/71 fora (ver grupo.sql).
SELECT CODI_EMP, CODI_PSV, DESC_PSV, QTE1_PRO, QTE2_PRO, VALOR_UNIT,
       EF, RDE, PCNR, PVNE, CF, VF, MT,
       (EF + PCNR - PVNE + RDE) AS DISPONIVEL
FROM (
  SELECT /*+ NO_MERGE */
    D.CODI_EMP,
    PSV.CODI_PSV,
    PSV.DESC_PSV,
    COALESCE(PR.QTE1_PRO, 0) AS QTE1_PRO,   -- NOT NULL no destino
    COALESCE(PR.QTE2_PRO, 0) AS QTE2_PRO,   -- NOT NULL no destino
    0 AS VALOR_UNIT,
    COALESCE((SELECT SUM(ESTOQUE) FROM TABLE(SALDO_INICIAL(D.CODI_EMP, 1, PSV.CODI_PSV, TRUNC(SYSDATE), 0))), 0) AS EF,
    COALESCE((SELECT SUM(ESTOQUE) FROM TABLE(SALDO_INICIAL(D.CODI_EMP, 9, PSV.CODI_PSV, TRUNC(SYSDATE), 0))), 0) AS RDE,
    COALESCE((SELECT SUM(ESTOQUE) FROM TABLE(SALDO_INICIAL(D.CODI_EMP, 2, PSV.CODI_PSV, TRUNC(SYSDATE), 0))), 0) AS PCNR,
    COALESCE((SELECT SUM(ESTOQUE) FROM TABLE(SALDO_INICIAL(D.CODI_EMP, 3, PSV.CODI_PSV, TRUNC(SYSDATE), 0))), 0) AS PVNE,
    0 AS CF, 0 AS VF, 0 AS MT
  FROM PRODSERV PSV
  INNER JOIN DADOSPRO D ON D.CODI_PSV = PSV.CODI_PSV
  INNER JOIN CADEMP C   ON C.CODI_EMP = D.CODI_EMP AND C.SITU_EMP = 'A'
  LEFT JOIN PRODUTO PR  ON PR.CODI_PSV = PSV.CODI_PSV
  WHERE PSV.SITU_PSV = 'A'
    AND PSV.CODI_GPR NOT IN (49, 50, 71)
);
