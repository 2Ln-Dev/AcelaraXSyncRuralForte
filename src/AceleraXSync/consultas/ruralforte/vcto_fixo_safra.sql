-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- vcto_fixo_safra  ->  VCTOFIXOSAFRA: faixas de VENCIMENTO FIXO tipo safra das condicoes
-- com CONDICAO.VEFX_CON = 'S' (migracao 095 do app; regra em lib/motor/vencimento-fixo.ts).
-- A emissao do pedido cai numa faixa (INIC..FINA) e o 1o vencimento e' o dia DVEN no
-- mes/ano MANO (MMYYYY numerico, ex.: 112026 = 11/2026); QPAR parcelas mensais.
-- Na Rural sao faixas 26 -> 25 cadastradas ate' 2030. So' as que ainda valem (fim a
-- partir do mes passado): o app so' le faixas de hoje em diante.
SELECT TO_CHAR(s.CODI_VFS) AS CODI_VFS,
       TO_CHAR(s.COND_CON) AS COND_CON,
       TO_CHAR(s.INIC_VFS, 'YYYY-MM-DD') AS INIC_VFS,
       TO_CHAR(s.FINA_VFS, 'YYYY-MM-DD') AS FINA_VFS,
       s.DVEN_VFS,
       s.QPAR_VFS,
       s.MANO_VFS,
       TO_CHAR(s.CODI_TDO) AS CODI_TDO
FROM VCTOFIXOSAFRA s
INNER JOIN CONDICAO c ON c.COND_CON = s.COND_CON
WHERE c.TIPO_CON = 'R'
  AND s.FINA_VFS >= ADD_MONTHS(TRUNC(SYSDATE), -1)
ORDER BY s.COND_CON, s.INIC_VFS;
