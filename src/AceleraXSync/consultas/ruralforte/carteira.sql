-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- carteira  ->  itens de pedido de venda com saldo a entregar.
--
-- Regra de "pedido em aberto" = a do proprio HUB da Rural (docs/fluxo-caixa/sql/pedvenda.sql,
-- que substituiu a VPROVISAO_A_RECEBER do ERP):
--   SITU_PED <> '9', TFAT_PED IN ('1','3'), saldo = QTDE - QPER - QTDE_ENTR_PED_VEN > 0.
-- (A AgroSanta herdou outra regra da Agroinsumos: corte 2022, SITU_PED IN ('1','0','5'),
--  CODI_TOP <> 38. Nao se aplica aqui.)
-- QTDE_ENTR_PED_VEN roda UMA vez por item (inline view + NO_MERGE), como no HUB.
-- EF_GLOBAL / EF_LOJA = 0 (estoque do item na carteira fora do escopo, como na AgroSanta).
-- ESCOPO: filial 50 fora (ver empresa.sql) e grupos 49/50/71 fora (ver grupo.sql).
SELECT ORIGEM_ERP_ID, CODI_EMP, PEDIDO, CODI_TRA, PROP_PRO, CODI_PES, CODI_PSV, CODI_CIC,
       QTDE,
       (QTDE - SALDO)  AS ENTREGUE,
       SALDO,
       0 AS EF_GLOBAL,
       0 AS EF_LOJA
FROM (
  SELECT ORIGEM_ERP_ID, CODI_EMP, PEDIDO, CODI_TRA, PROP_PRO, CODI_PES, CODI_PSV, CODI_CIC,
         QTDE,
         ROUND(QTDE - QPER - QENT, 6) AS SALDO
  FROM (
    SELECT /*+ NO_MERGE */
      TO_CHAR(PED.CODI_EMP) || '|' || TO_CHAR(PED.PEDI_PED) || '|' ||
        TO_CHAR(PED.SERI_PED) || '|' || TO_CHAR(IPE.CODI_PSV) AS ORIGEM_ERP_ID,
      TO_CHAR(PED.CODI_EMP)  AS CODI_EMP,
      TO_CHAR(PED.PEDI_PED)  AS PEDIDO,
      TO_CHAR(PED.CODI_TRA)  AS CODI_TRA,
      TO_CHAR(PED.PROP_PRO)  AS PROP_PRO,
      TO_CHAR(PED.COD1_PES)  AS CODI_PES,
      TO_CHAR(IPE.CODI_PSV)  AS CODI_PSV,
      TO_CHAR(PED.CODI_CIC)  AS CODI_CIC,
      COALESCE(IPE.QTDE_IPE, 0) AS QTDE,
      COALESCE(IPE.QPER_IPE, 0) AS QPER,
      COALESCE((SELECT QENT FROM TABLE(QTDE_ENTR_PED_VEN(PED.CODI_EMP, PED.PEDI_PED, PED.SERI_PED, IPE.CODI_PSV))), 0) AS QENT
    FROM PEDIDO PED
    INNER JOIN IPEDIDO IPE ON IPE.CODI_EMP = PED.CODI_EMP
                          AND IPE.PEDI_PED = PED.PEDI_PED
                          AND IPE.SERI_PED = PED.SERI_PED
    INNER JOIN CLIENTE CL  ON CL.CODI_TRA = PED.CODI_TRA
    INNER JOIN PRODSERV PD ON PD.CODI_PSV = IPE.CODI_PSV
    WHERE PED.SITU_PED NOT IN ('9')
      AND PED.TFAT_PED IN ('1', '3')
      AND IPE.CODI_PSV IS NOT NULL
      AND PED.CODI_EMP <> 50
      AND PD.CODI_GPR NOT IN (49, 50, 71)
  )
)
WHERE SALDO > 0;
