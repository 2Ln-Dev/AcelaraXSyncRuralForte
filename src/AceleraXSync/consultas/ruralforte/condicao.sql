-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- condicao  ->  CONDICAO + INDEXADOR. TIPO_CON = 'R' (recebimento/venda); o HUB usa
-- 'P' para as condicoes de pagamento das notas de entrada.
--
-- PRAZ_CON ('0030;0060;0090;') = parcelamento: o app gera as parcelas do pedido a
-- partir dele (migracao 077 do app). OPER/TIRE/DANT/JATV vao como texto (colunas text
-- no app). JURO_CON NAO vai: no ERP e' S/N e no app a coluna e' numeric.
-- CODI_CON (conta) e CODI_TDO (tipo de documento) vao para o pre-financeiro do pedido
-- (pedidoVendaParcelasList) — colunas conta_con/tdoc_con, migracao 086 do app.
-- VEFX_CON = 'S' (vencimento fixo) + PERI_VFX (VENCIMENTOFIXO: 'S' safra / 'M' mensal):
-- o app ignora o PRAZ_CON e usa vcto_fixo_safra/vcto_fixo_mensal (migracao 095).
-- FPGT_CON = formas de pagamento vinculadas (IFORMASPAGAMENTO.FPGT_IFP, '03;10'): a DLL
-- recusa forma fora do vinculo (RFPG003). Vazio = sem restricao. O app escolhe uma
-- vinculada e compativel com OPER_CON (migracao 097 do app). Oracle 11.2 nao tem
-- LISTAGG(DISTINCT): o DISTINCT vai na subconsulta.
SELECT con.COND_CON, con.DESC_CON, ind.CODI_IND, ind.DESC_IND, con.STAT_CON,
       con.TIPO_CON,
       con.PRAZ_CON,
       TO_CHAR(con.OPER_CON) AS OPER_CON,
       TO_CHAR(con.TIRE_CON) AS TIRE_CON,
       TO_CHAR(con.DANT_CON) AS DANT_CON,
       TO_CHAR(con.JATV_CON) AS JATV_CON,
       TO_CHAR(con.CODI_CON) AS CODI_CON,
       TO_CHAR(con.CODI_TDO) AS CODI_TDO,
       con.VEFX_CON,
       vfx.PERI_VFX,
       (SELECT LISTAGG(f.FPGT, ';') WITHIN GROUP (ORDER BY f.FPGT)
          FROM (SELECT DISTINCT ifp.COND_CON,
                       LPAD(TRIM(TO_CHAR(ifp.FPGT_IFP)), 2, '0') AS FPGT
                  FROM IFORMASPAGAMENTO ifp) f
         WHERE f.COND_CON = con.COND_CON) AS FPGT_CON
FROM CONDICAO con
LEFT JOIN INDEXADOR ind ON ind.CODI_IND = con.CODI_IND
LEFT JOIN VENCIMENTOFIXO vfx ON vfx.COND_CON = con.COND_CON
WHERE con.TIPO_CON = 'R';
