-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  8. estoque  (era EST_CICLO_AF2)  ->  SALDO_INICIAL por CODI_CTR
--     Mapa dos tipos de saldo (tabela CTRLSALDO):
--        1 = ESTOQUE FISICO               -> EF
--        9 = REMESSA DEP/ARM. EMITIDA     -> RDE
--        2 = PEDIDO DE COMPRA NAO RECEB.  -> PCNR
--        3 = PEDIDO DE VENDA NAO ENTREG.  -> PVNE
--     CUSTO: ~620-780 ms por linha (a SALDO_INICIAL e PIPELINED e varre
--     notas/pedidos). ~1.042 linhas => ~11 min. OK para carga inicial;
--     se virar rotina, materializar numa tabela do ERP.
-- ---------------------------------------------------------------------
SELECT
  D.CODI_EMP   AS CODI_EMP,
  PSV.CODI_PSV AS CODI_PSV,
  PSV.DESC_PSV AS DESC_PSV,
  COALESCE(PR.QTE1_PRO,0) AS QTE1_PRO,   -- NOT NULL no destino
  COALESCE(PR.QTE2_PRO,0) AS QTE2_PRO,   -- NOT NULL no destino
  0            AS VALOR_UNIT,
  S.EF, S.RDE, S.PCNR, S.PVNE,
  0 AS CF, 0 AS VF, 0 AS MT,
  (S.EF + S.PCNR - S.PVNE + S.RDE) AS DISPONIVEL
FROM PRODSERV PSV
  LEFT JOIN CADEMP C   ON 1=1
  LEFT JOIN PRODUTO PR ON (PSV.CODI_PSV = PR.CODI_PSV)
  LEFT JOIN DADOSPRO D ON (D.CODI_PSV = PSV.CODI_PSV AND D.CODI_EMP = C.CODI_EMP)
  CROSS JOIN LATERAL (
    SELECT
      COALESCE((SELECT * FROM TABLE(SALDO_INICIAL(D.CODI_EMP,1,PSV.CODI_PSV,CURRENT_DATE,0))),0) AS EF,
      COALESCE((SELECT * FROM TABLE(SALDO_INICIAL(D.CODI_EMP,9,PSV.CODI_PSV,CURRENT_DATE,0))),0) AS RDE,
      COALESCE((SELECT * FROM TABLE(SALDO_INICIAL(D.CODI_EMP,2,PSV.CODI_PSV,CURRENT_DATE,0))),0) AS PCNR,
      COALESCE((SELECT * FROM TABLE(SALDO_INICIAL(D.CODI_EMP,3,PSV.CODI_PSV,CURRENT_DATE,0))),0) AS PVNE
    FROM DUAL
  ) S
WHERE PSV.SITU_PSV = 'A'
  AND D.CODI_EMP = 2            -- so a loja 2 (ALTAMIRA)
  AND PSV.CODI_GPR NOT IN (50,51,52,53);
