-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- transac (clientes)  ->  TRANSAC + CLIENTE + CATEGCLI + MUNICIPIO.
--
-- CODI_PES (vendedor do cliente): na Rural NAO ha vendedor no cadastro — CLIENTE.COD1_PES
-- esta vazio nos 4.100 clientes e TRANSAC.CODI_PES e' o operador que cadastrou. Mesma
-- regra do HUB (ClienteBiModule.cs): vendedor PREDOMINANTE nos titulos do cliente
-- (RECEBER.COD1_PES com mais titulos; desempate pelo vencimento mais recente).
-- VENDEDORCLI (usado na AgroSanta) nao e' referenciado em lugar nenhum do HUB.
SELECT t.CODI_TRA,
       t.RAZA_TRA,
       m.DESC_MUN,
       m.ESTA_MUN,
       cat.DESC_CTC,
       cl.CODI_CTC,
       vt.CODI_PES,
       NVL(t.SITU_TRA, 'A') AS SITU_TRA
FROM TRANSAC t
INNER JOIN CLIENTE cl  ON cl.CODI_TRA = t.CODI_TRA
LEFT JOIN CATEGCLI cat ON cat.CODI_CTC = cl.CODI_CTC
LEFT JOIN MUNICIPIO m  ON m.CODI_MUN = t.CODI_MUN
LEFT JOIN (SELECT codi_tra, MAX(cod1_pes) KEEP (DENSE_RANK LAST ORDER BY n, ult) AS CODI_PES
             FROM (SELECT cb.codi_tra, r.cod1_pes, COUNT(*) n, MAX(r.venc_rec) ult
                     FROM CABREC cb JOIN RECEBER r ON r.ctrl_cbr = cb.ctrl_cbr
                    WHERE r.cod1_pes IS NOT NULL
                    GROUP BY cb.codi_tra, r.cod1_pes)
            GROUP BY codi_tra) vt ON vt.codi_tra = t.CODI_TRA;
