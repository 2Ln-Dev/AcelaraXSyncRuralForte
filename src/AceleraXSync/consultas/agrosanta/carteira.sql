-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
-- 12. carteira  (era EST_CICLO_AF)  ->  agora 100% nativa
--     EF_GLOBAL / EF_LOJA = 0 (fora do escopo da POC), o que eliminou a
--     unica dependencia de objeto inexistente.
--     A subconsulta correlacionada (a mesma do ERP antigo, VERBATIM na
--     logica) foi movida para um CROSS JOIN LATERAL: no original ela
--     aparecia 3x (ENTREGUE, SALDO e WHERE); aqui e avaliada UMA vez por
--     linha. Mesmo resultado, ~3x menos trabalho.
--     Medido: 71 linhas em 120s no periodo completo (desde 2022-01-01).
--
--     Regras de negocio herdadas do ERP antigo — CONFIRMAR com a AgroSanta:
--       • corte DEMI_PED >= 2022-01-01
--       • situacoes de pedido SITU_PED IN ('1','0','5')
--       • exclusao da operacao CODI_TOP = 38
-- ---------------------------------------------------------------------
SELECT DISTINCT
  TO_CHAR(PE.CODI_EMP) || '|' || TO_CHAR(PE.PEDI_PED) || '|' ||
    TO_CHAR(PE.SERI_PED) || '|' || TO_CHAR(IPE.CODI_PSV) AS "ORIGEM_ERP_ID",
  TO_CHAR(PE.CODI_EMP)  AS "CODI_EMP",
  TO_CHAR(PE.PEDI_PED)  AS "PEDIDO",
  TO_CHAR(PE.CODI_TRA)  AS "CODI_TRA",
  TO_CHAR(PE.PROP_PRO)  AS "PROP_PRO",
  TO_CHAR(PE.COD1_PES)  AS "CODI_PES",
  TO_CHAR(IPE.CODI_PSV) AS "CODI_PSV",
  TO_CHAR(PE.CODI_CIC)  AS "CODI_CIC",
  IPE.QTDE_IPE                     AS "QTDE",
  (IPE.QTDE_IPE - NVL(SL.SALDO,0)) AS "ENTREGUE",
  0                                AS "EF_GLOBAL",
  0                                AS "EF_LOJA",
  NVL(SL.SALDO,0)                  AS "SALDO"
FROM PEDIDO PE
INNER JOIN IPEDIDO IPE ON (IPE.CODI_EMP = PE.CODI_EMP AND IPE.PEDI_PED = PE.PEDI_PED AND IPE.SERI_PED = PE.SERI_PED)
INNER JOIN TRANSAC  TR ON (TR.CODI_TRA = PE.CODI_TRA)
INNER JOIN CLIENTE  CL ON (CL.CODI_TRA = PE.CODI_TRA)
INNER JOIN PRODSERV PD ON (PD.CODI_PSV = IPE.CODI_PSV)
INNER JOIN GRUPO    GP ON (PD.CODI_GPR = GP.CODI_GPR)
INNER JOIN PRODUTO  PR ON (PR.CODI_PSV = IPE.CODI_PSV)
CROSS JOIN LATERAL (
  SELECT ROUND(SUM((IP.QTDE_IPE - COALESCE(IP.QPER_IPE,0)) - (
    CASE
      WHEN (P.CCFO_CFO LIKE '5922%' OR P.CCFO_CFO LIKE '6922%')
      THEN COALESCE((
        SELECT SUM(QENT_INO) + SUM(QDEV_INO) FROM INOTA INO
        INNER JOIN NOTA NT ON NT.NPRE_NOT = INO.NPRE_NOT
        WHERE NT.SITU_NOT = '5'
          AND INO.EMPR_PED = P.CODI_EMP AND INO.PEDI_PED = P.PEDI_PED
          AND INO.SERI_PED = P.SERI_PED AND INO.CODI_PSV = IP.CODI_PSV), 0)
      ELSE (SELECT QENT FROM TABLE(QTDE_ENTR_PED_VEN(IP.CODI_EMP,IP.PEDI_PED,IP.SERI_PED,IP.CODI_PSV)))
    END)), 6) AS SALDO
  FROM IPEDIDO IP
  INNER JOIN PEDIDO P ON (IP.PEDI_PED = P.PEDI_PED AND IP.CODI_EMP = P.CODI_EMP)
  WHERE (P.DEMI_PED >= DATE '2022-01-01' AND P.DEMI_PED <= SYSDATE)
    AND IP.CODI_PSV IS NOT NULL AND P.SITU_PED IN ('1','0','5') AND P.CODI_TOP <> 38
    AND IP.PEDI_PED = IPE.PEDI_PED AND IP.CODI_EMP = IPE.CODI_EMP
    AND IP.SERI_PED = IPE.SERI_PED AND IP.CODI_PSV = IPE.CODI_PSV
) SL
WHERE (PE.DEMI_PED >= DATE '2022-01-01' AND PE.DEMI_PED <= SYSDATE)
  AND IPE.CODI_PSV IS NOT NULL
  AND PE.SITU_PED IN ('1','0','5')
  AND PE.CODI_TOP <> 38
  AND PD.CODI_GPR NOT IN (50,51,52,53)
  AND NVL(SL.SALDO,0) > 0;
