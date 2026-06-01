-- Views de integração ERP → Supabase
-- Executar como AGROINSU
-- Referência: Integracao_ERP_Onda1_AceleraX.docx seção 5
-- codi_ten NÃO vem da view — é constante por execução injetado pelo sync (ver spec seção 4)

-- 5.1 Clientes
CREATE OR REPLACE VIEW V_AX_CLIENTES AS
SELECT t.CODI_TRA,
       t.RAZA_TRA,
       m.DESC_MUN,
       m.ESTA_MUN
FROM TRANSAC t
LEFT JOIN MUNICIPIO m ON m.CODI_MUN = t.CODI_MUN;

-- 5.2 Propriedades
CREATE OR REPLACE VIEW V_AX_PROPRIEDADES AS
SELECT p.PROP_PRO,
       p.CODI_TRA,
       p.DESC_PRO,
       m.DESC_MUN,
       m.ESTA_MUN
FROM PROPRIED p
LEFT JOIN MUNICIPIO m ON m.CODI_MUN = p.CODI_MUN;

-- 5.3 Ciclos/Safra
-- APEX_CICLO tem DATA_VENC/DATA_VENC_FIM (DATE) e ABRE_CIC em vez de ANO_INI/ANO_FIM/SITUACAO
CREATE OR REPLACE VIEW V_AX_CICLOS AS
SELECT c.CODI_CIC,
       c.DESC_CIC,
       EXTRACT(YEAR FROM c.DATA_VENC)     AS ANO_INI,
       EXTRACT(YEAR FROM c.DATA_VENC_FIM) AS ANO_FIM,
       c.ABRE_CIC                         AS SITUACAO
FROM APEX_CICLO c;

-- 5.4 Estoque
CREATE OR REPLACE VIEW V_AX_ESTOQUE AS
SELECT CODI_EMP,
       CODI_PSV_VINC,
       DESC_PSV_VINC,
       QTE1_PRO,
       QTE2_PRO,
       VALOR_UNIT,
       EF,
       RDE,
       PCNR,
       PVNE,
       CF,
       VF,
       MT,
       DISPONIVEL,
       CODI_CIC
FROM EST_CICLO_AF2;

-- 5.6 Grupos
-- APEX_GRUPO: CHAVEGRUPO = '{CODI_GPR}#{CODI_SBG}', DESC_GPR = nome novo
CREATE OR REPLACE VIEW V_AX_GRUPOS AS
SELECT DISTINCT
       TO_NUMBER(SUBSTR(CHAVEGRUPO, 1, INSTR(CHAVEGRUPO, '#') - 1)) AS CODI_GPR,
       DESC_GPR
FROM APEX_GRUPO;

-- 5.6 Subgrupos
CREATE OR REPLACE VIEW V_AX_SUBGRUPOS AS
SELECT sg.CODI_SBG,
       sg.DESC_SBG
FROM SUBGRUPO sg;

-- 5.5 Produtos
CREATE OR REPLACE VIEW V_AX_PRODUTOS AS
SELECT ps.CODI_PSV,
       ps.DESC_PSV,
       ps.UNID_PSV,
       ps.COMP_PSV,
       ag.CODI_GPR,
       ag.DESC_GPR,
       ps.CODI_SBG
FROM PRODSERV ps
LEFT JOIN (
    SELECT DISTINCT
           TO_NUMBER(SUBSTR(CHAVEGRUPO, 1, INSTR(CHAVEGRUPO, '#') - 1)) AS CODI_GPR,
           DESC_GPR
    FROM APEX_GRUPO
) ag ON ag.CODI_GPR = ps.CODI_GPR
WHERE ps.SITU_PSV IS NULL OR ps.SITU_PSV <> 'I';

-- 5.7 Tabela de Preço (cabeçalho + itens numa view achatada)
CREATE OR REPLACE VIEW V_AX_TABELA_PRECO AS
SELECT ID_CTAB,
       NOME,
       CODI_CIC,
       CODI_PSV,
       SITU_CTAB,
       ORIGEM,
       CUSTO
FROM VTABELA_SINC;
