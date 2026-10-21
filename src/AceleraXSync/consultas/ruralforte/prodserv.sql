-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- prodserv (produtos)  ->  PRODSERV. CHAVEGRUPO igual ao de grupo.sql.
-- Produto sem grupo ou sem subgrupo vai com CHAVEGRUPO NULL (no TREINO eram 10, que
-- sairiam como '#' / '55#', chave que nao existe em grupo). Linha sem descricao
-- (cadastro lixo, ex.: 0002890) fica de fora.
-- ESCOPO: grupos 49/50/71 fora (ver grupo.sql). Produto SEM grupo tambem fica fora
-- (NOT IN com NULL) — sao cadastros antigos, quase todos inativos.
--
-- LTOB_PRO (PRODUTO): controle de lote — 1=No Pedido, 2=No Faturamento, 4=Na Expedicao,
-- 9=Nao Obrigatorio (vazio/null = nao controla). O app avisa o vendedor quando '1' (lote
-- obrigatorio no pedido). LEFT JOIN: produto/servico sem linha em PRODUTO fica NULL.
-- QTDF_PRO (PRODUTO): 'S' = aceita quantidade fracionada (ex.: 7,8); senao o web/app
-- arredonda a quantidade do item.
SELECT ps.CODI_PSV,
       ps.DESC_PSV,
       ps.UNID_PSV,
       ps.COMP_PSV,
       CASE WHEN ps.CODI_GPR IS NOT NULL AND ps.CODI_SBG IS NOT NULL
            THEN ps.CODI_GPR || '#' || ps.CODI_SBG END AS CHAVEGRUPO,
       ps.SITU_PSV,
       prd.LTOB_PRO AS LTOB_PRO,
       prd.QTDF_PRO AS QTDF_PRO
FROM PRODSERV ps
LEFT JOIN PRODUTO prd ON prd.CODI_PSV = ps.CODI_PSV
WHERE ps.DESC_PSV IS NOT NULL
  AND ps.CODI_GPR NOT IN (49, 50, 71);
