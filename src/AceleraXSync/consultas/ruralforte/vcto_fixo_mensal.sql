-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- vcto_fixo_mensal  ->  VCTOFIXOMENSAL: regra de VENCIMENTO FIXO tipo mensal (uma linha
-- por condicao; migracao 095 do app). DVEN = dia do vencimento, QPAR = parcelas,
-- PVEN = 'S' -> 1o vencimento sempre no mes seguinte. INIC/FINA (dias da faixa) so'
-- validam o cadastro no ERP. Na Rural todas as condicoes fixas sao SAFRA: estas linhas
-- existem mas nao sao usadas (o app so' le a mensal quando PERI_VFX = 'M').
SELECT TO_CHAR(m.COND_CON) AS COND_CON,
       m.INIC_VFM,
       m.FINA_VFM,
       m.DVEN_VFM,
       m.QPAR_VFM,
       m.PVEN_VFM,
       TO_CHAR(m.CODI_TDO) AS CODI_TDO
FROM VCTOFIXOMENSAL m
INNER JOIN CONDICAO c ON c.COND_CON = m.COND_CON
WHERE c.TIPO_CON = 'R';
