-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- transac (clientes)  ->  TRANSAC + CLIENTE + CATEGCLI + MUNICIPIO.
--
-- CODI_PES (vendedor do cliente) vem de VENDEDORCLI.COD1_PES, a carteira do ERP (como na
-- AgroSanta). NAO usar CLIENTE.COD1_PES (vazio nos 4.100 clientes) nem TRANSAC.CODI_PES
-- (operador que cadastrou). Antes vinha do vendedor predominante nos titulos (RECEBER), o
-- que trazia vendedor antigo/inativo. Cliente fora da VENDEDORCLI fica SEM vendedor (decisao
-- do cliente; sem fallback para os titulos). Sem filtro de filial: o subselect garante uma
-- linha por cliente (se houver mais de uma em VENDEDORCLI, fica o maior COD1_PES).
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
LEFT JOIN (SELECT codi_tra, MAX(cod1_pes) AS CODI_PES
             FROM VENDEDORCLI
            WHERE cod1_pes IS NOT NULL
            GROUP BY codi_tra) vt ON vt.codi_tra = t.CODI_TRA;
