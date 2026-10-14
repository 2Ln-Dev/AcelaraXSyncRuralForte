-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- condicoes_categoria  ->  CATEGCLICOND: quais condicoes de pagamento cada categoria de
-- cliente (CLIENTE.CODI_CTC) pode usar. No app (condicao_categoria) a lista de uma
-- categoria SUBSTITUI a anterior; categoria sem linha aqui = sem restricao.
-- So' condicoes de venda (TIPO_CON = 'R'), as mesmas da carga de condicoes.
-- ORDER BY CODI_CTC: as linhas de uma categoria ficam no mesmo lote (a substituicao
-- e' por categoria dentro de cada envio).
SELECT TO_CHAR(x.CODI_CTC) AS CODI_CTC,
       TO_CHAR(x.COND_CON) AS COND_CON
FROM CATEGCLICOND x
INNER JOIN CONDICAO c ON c.COND_CON = x.COND_CON
WHERE c.TIPO_CON = 'R'
ORDER BY x.CODI_CTC, x.COND_CON;
