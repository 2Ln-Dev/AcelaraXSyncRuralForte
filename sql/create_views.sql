-- Views de integração ERP → Supabase
-- Executar como AGROINSU
-- Referência: Integracao_ERP_Onda1_AceleraX.docx seção 5
-- codi_ten NÃO vem da view — é constante por execução injetado pelo sync (ver spec seção 4)

-- 5.0 Vendedores (pessoas que sao vendedores) - carrega antes de transac (FK codi_pes)
CREATE OR REPLACE VIEW V_AX_VENDEDOR AS
SELECT pes.codi_pes, pes.nome_pes, pes.situ_pes from pessoal pes
inner join vendedor v on v.codi_pes = pes.codi_pes;

-- 5.1 Clientes
-- DESC_CTC (categoria), CODI_CTC (codigo da categoria) e CODI_PES vem de
-- VDADOS_CLIENTE (INNER JOIN por CODI_TRA).
CREATE OR REPLACE VIEW V_AX_CLIENTES AS
SELECT t.CODI_TRA,
       t.RAZA_TRA,
       m.DESC_MUN,
       m.ESTA_MUN,
       v.DESC_CTC,
       v.CODI_CTC,
       v.CODI_PES
FROM TRANSAC t
INNER JOIN VDADOS_CLIENTE v ON v.CODI_TRA = t.CODI_TRA
LEFT JOIN MUNICIPIO m ON m.CODI_MUN = t.CODI_MUN;

-- 5.2 Propriedades
-- INNER JOIN VDADOS_CLIENTE: so propriedades cujo cliente existe em VDADOS_CLIENTE
-- (consistente com V_AX_CLIENTES, evita propriedade orfa de cliente nao sincronizado).
CREATE OR REPLACE VIEW V_AX_PROPRIEDADES AS
SELECT p.PROP_PRO,
       p.CODI_TRA,
       p.DESC_PRO,
       m.DESC_MUN,
       m.ESTA_MUN
FROM PROPRIED p
INNER JOIN VDADOS_CLIENTE v ON v.CODI_TRA = p.CODI_TRA
LEFT JOIN MUNICIPIO m ON m.CODI_MUN = p.CODI_MUN;

-- 5.3 Ciclos/Safra
-- DESC_CIC vem de APEX_CICLO.ABRE_CIC; SITUACAO vem de CICLO.SITU_CIC (join por CODI_CIC).
-- ANO_INI/ANO_FIM derivados de DATA_VENC/DATA_VENC_FIM (DATE) em APEX_CICLO.
CREATE OR REPLACE VIEW V_AX_CICLOS AS
SELECT c.CODI_CIC,
       c.ABRE_CIC                         AS DESC_CIC,
       EXTRACT(YEAR FROM c.DATA_VENC)     AS ANO_INI,
       EXTRACT(YEAR FROM c.DATA_VENC_FIM) AS ANO_FIM,
       cic.SITU_CIC                       AS SITUACAO
FROM APEX_CICLO c
INNER JOIN CICLO cic ON cic.CODI_CIC = c.CODI_CIC;

-- 5.4 Estoque
-- Agregado por (CODI_EMP, CODI_PSV, DESC_PSV, QTE1_PRO, QTE2_PRO, VALOR_UNIT);
-- soma EF/RDE/PCNR/PVNE/CF/VF/MT/DISPONIVEL entre ciclos. Sem CODI_CIC / grupo / subgrupo / unidade.
CREATE OR REPLACE VIEW V_AX_ESTOQUE AS
SELECT e.CODI_EMP,
       e.CODI_PSV_VINC          AS CODI_PSV,
       e.DESC_PSV_VINC          AS DESC_PSV,
       e.QTE1_PRO,
       e.QTE2_PRO,
       e.VALOR_UNIT,
       SUM(e.EF)         AS EF,
       SUM(e.RDE)        AS RDE,
       SUM(e.PCNR)       AS PCNR,
       SUM(e.PVNE)       AS PVNE,
       SUM(e.CF)         AS CF,
       SUM(e.VF)         AS VF,
       SUM(e.MT)         AS MT,
       SUM(e.DISPONIVEL) AS DISPONIVEL
FROM EST_CICLO_AF2 e
GROUP BY e.CODI_EMP,
         e.CODI_PSV_VINC,
         e.DESC_PSV_VINC,
         e.QTE1_PRO,
         e.QTE2_PRO,
         e.VALOR_UNIT;

-- 5.6 Grupos (novo modelo: uma linha por CHAVEGRUPO = grupo+subgrupo, com as duas descricoes)
-- CHAVEGRUPO = '{CODI_GPR}#{CODI_SBG}' (texto); DESC_GPR = grupo; DESC_SBG = subgrupo.
CREATE OR REPLACE VIEW V_AX_GRUPOS AS
SELECT CHAVEGRUPO,
       DESC_GPR,
       DESC_SBG
FROM APEX_GRUPO;

-- 5.6 Subgrupos: DESCONTINUADO. O subgrupo passou a viver dentro de V_AX_GRUPOS (DESC_SBG).

-- 5.5 Produtos
-- chavegrupo vem direto de APEX_GRUPO (join por CODI_GPR#CODI_SBG = CHAVEGRUPO).
-- INNER JOIN: produtos sem grupo correspondente sao excluidos.
-- Filtra apenas os 4 grupos alvo. SITU_PSV exposta para o destino.
CREATE OR REPLACE VIEW V_AX_PRODUTOS AS
SELECT ps.CODI_PSV,
       ps.DESC_PSV,
       ps.UNID_PSV,
       ps.COMP_PSV,
       g.CHAVEGRUPO,
       ps.SITU_PSV
FROM PRODSERV ps
INNER JOIN APEX_GRUPO g ON ps.CODI_GPR || '#' || ps.CODI_SBG = g.CHAVEGRUPO
WHERE g.DESC_GPR IN ('01-DEFENSIVOS', '03-ESPECIALIDADES', '02-SEMENTES', '04-FERTILIZANTES');

-- 5.7 Tabela de Preço — CABECALHO (V_AX_TABELA_PRECO) e ITENS (V_AX_ITABELA_PRECO) separados.
CREATE OR REPLACE VIEW V_AX_TABELA_PRECO AS
SELECT distinct ID_CTAB,
       NOME,
       CODI_CIC,

       case
          when SITU_CTAB = 'A' THEN 'U'
          when SITU_CTAB = 'I' THEN 'B'
          when SITU_CTAB = 'P' THEN 'B'
       END   SITU_CTAB
FROM VTABELA_SINC;

-- 5.7b Tabela de Preço — ITENS. ORIGEM = moeda (Reais/Dolar).
CREATE OR REPLACE VIEW V_AX_ITABELA_PRECO AS
SELECT  ID_CTAB, codi_psv, custo, origem
FROM VTABELA_SINC;

-- 5.8 Condicoes de pagamento (tipo 'R')
CREATE OR REPLACE VIEW V_AX_CONDICAO AS
select con.cond_con, con.desc_con, ind.codi_ind, ind.desc_ind, con.stat_con  from condicao con
left join indexador ind on ind.codi_ind = con.codi_ind
where con.tipo_con = 'R';
