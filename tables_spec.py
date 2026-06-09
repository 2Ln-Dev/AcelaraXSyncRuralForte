"""
UPSERT keys and De/Para column mappings per spec.
"""

TABLES_SPEC = {
    "vendedor": {
        "oracle_view": "AGROINSU.V_AX_VENDEDOR",
        "supabase_table": "vendedor",
        "upsert_key": ("codi_ten", "codi_pes"),
        "columns": {
            "CODI_PES": "codi_pes",
            "NOME_PES": "nome_pes",
            "SITU_PES": "situ_pes",
        }
    },
    "condicao": {
        "oracle_view": "AGROINSU.V_AX_CONDICAO",
        "supabase_table": "condicao",
        "upsert_key": ("codi_ten", "cond_con"),
        "columns": {
            "COND_CON": "cond_con",
            "DESC_CON": "desc_con",
            "CODI_IND": "codi_ind",
            "DESC_IND": "desc_ind",
            "STAT_CON": "situ_con",
        }
    },
    "transac": {
        "oracle_view": "AGROINSU.V_AX_CLIENTES",
        "supabase_table": "transac",
        "upsert_key": ("codi_ten", "codi_tra"),
        "columns": {
            "CODI_TRA": "codi_tra",
            "RAZA_TRA": "raza_tra",
            "DESC_MUN": "desc_mun",
            "ESTA_MUN": "esta_mun",
            "CODI_PES": "codi_pes",
            "DESC_CTC": "desc_ctc",
            "CODI_CTC": "codi_ctc",
        }
    },
    "propried": {
        "oracle_view": "AGROINSU.V_AX_PROPRIEDADES",
        "supabase_table": "propried",
        "upsert_key": ("codi_ten", "prop_pro"),
        "columns": {
            "PROP_PRO": "prop_pro",
            "CODI_TRA": "codi_tra",
            "DESC_PRO": "desc_pro",
            "DESC_MUN": "desc_mun",
            "ESTA_MUN": "esta_mun",
        }
    },
    "ciclos_safra": {
        "oracle_view": "AGROINSU.V_AX_CICLOS",
        "supabase_table": "ciclos_safra",
        "upsert_key": ("codi_ten", "codi_cic"),
        "columns": {
            "CODI_CIC": "codi_cic",
            "DESC_CIC": "nome_csa",
            "ANO_INI": "anoi_csa",
            "ANO_FIM": "anof_csa",
            "SITUACAO": "stat_csa",
        }
    },
    "estoque": {
        "oracle_view": "AGROINSU.V_AX_ESTOQUE",
        "supabase_table": "estoque",
        "upsert_key": ("codi_ten", "codi_pro", "codi_fil"),
        "columns": {
            "CODI_EMP": "codi_fil",
            "CODI_PSV": "codi_pro",
            "DESC_PSV": "desc_pro",
            "QTE1_PRO": "qtd1_est",
            "QTE2_PRO": "qtd2_est",
            "VALOR_UNIT": "vlun_est",
            "EF": "esfi_est",
            "RDE": "rese_est",
            "PCNR": "pcnr_est",
            "PVNE": "pvne_est",
            "CF": "cofu_est",
            "VF": "vefu_est",
            "MT": "mtra_est",
            "DISPONIVEL": "esdi_est",
        }
    },
    "prodserv": {
        "oracle_view": "AGROINSU.V_AX_PRODUTOS",
        "supabase_table": "prodserv",
        "upsert_key": ("codi_ten", "codi_psv"),
        "columns": {
            "CODI_PSV": "codi_psv",
            "DESC_PSV": "desc_psv",
            "UNID_PSV": "unid_psv",
            "COMP_PSV": "comp_psv",
            # O vinculo grupo/segmento se faz por chavegrupo (tabela grupo ja existe
            # no Supabase). O motor resolve o segmento a partir de chavegrupo/grupo,
            # entao o sync NAO precisa mandar codi_gpr.
            "CHAVEGRUPO": "chavegrupo",
            "SITU_PSV": "situ_psv",
        }
    },
    "grupo": {
        "oracle_view": "AGROINSU.V_AX_GRUPOS",
        "supabase_table": "grupo",
        "upsert_key": ("codi_ten", "chavegrupo"),
        "columns": {
            "CHAVEGRUPO": "chavegrupo",
            "DESC_GPR": "desc_gpr",
            "DESC_SBG": "desc_sbg",
        }
    },
    "tabelas_preco": {
        "oracle_view": "AGROINSU.V_AX_TABELA_PRECO",
        "supabase_table": "tabelas_preco",
        "upsert_key": ("codi_ten", "erp_id"),
        "is_header": True,
        "columns": {
            "ID_CTAB": "erp_id",
            "NOME": "nome_tab",
            # Vinculo com a safra e' por codi_cic (chave natural do ERP, igual
            # fix_unique_keys.sql / Onda 1). O sync NAO resolve codi_csa.
            "CODI_CIC": "codi_cic",
            "SITU_CTAB": "stat_tab",
            # Parametros de ciclo/cambio (APEX_GER_TABELAS) — etapas 1-4 do motor.
            "PTAX_VISTA": "pxav_tab",
            "PTAX_PRAZO": "pxap_tab",
            "DATA_BASE": "dbas_tab",
            "VENC_MAX": "vmav_tab",
            "CORREC_RS": "corr_tab",
            "CORREC_USS": "coru_tab",
            "ANTEC_RS": "antr_tab",
            "ANTEC_USS": "antu_tab",
            # Margens por segmento (etapas 5 e 8 do motor) — de/para 4 grupos do
            # ERP -> 4 dos 5 segmentos do motor (confirmado com o Lucas):
            #   FERT (Fertilizantes) -> Adubos (adu)
            #   DEF  (Defensivos)    -> Quimico (qui)
            #   ESP  (Especialidades)-> Nutricao/Micro (nut)
            #   SEM  (Sementes)      -> Semente (sem)
            # Corretivos (cor) NAO e' usado no app -> marg/inim/fimm_cor ficam NULL.
            # Este de/para precisa ser consistente com lib/motor/segmentos.ts (app).
            "MARGEM_FERT": "marg_adu_tab",
            "FAIXA_INI_FERT": "inim_adu_tab",
            "FAIXA_FIM_FERT": "fimm_adu_tab",
            "MARGEM_DEF": "marg_qui_tab",
            "FAIXA_INI_DEF": "inim_qui_tab",
            "FAIXA_FIM_DEF": "fimm_qui_tab",
            "MARGEM_ESP": "marg_nut_tab",
            "FAIXA_INI_ESP": "inim_nut_tab",
            "FAIXA_FIM_ESP": "fimm_nut_tab",
            "MARGEM_SEM": "marg_sem_tab",
            "FAIXA_INI_SEM": "inim_sem_tab",
            "FAIXA_FIM_SEM": "fimm_sem_tab",
        }
    },
    "itens_tabela_preco": {
        "oracle_view": "AGROINSU.V_AX_ITABELA_PRECO",
        "supabase_table": "itens_tabela_preco",
        "upsert_key": ("codi_ten", "erp_id", "codi_pro"),
        "is_items": True,
        "header_table": "tabelas_preco",
        "link_from": "erp_id",
        "link_to": "codi_tab",
        "columns": {
            "ID_CTAB": "erp_id",
            "CODI_PSV": "codi_pro",
            # Insumos do motor (APEX_ITAB_VIG). CUSTO agora vai para cust_itp;
            # prrs_itp/prus_itp deixaram de vir do ERP (viram snapshot do app).
            "CUSTO": "cust_itp",
            "EMBALAGEM": "emba_itp",
            "ORIGEM": "orig_itp",
            "ADICIONAL": "adic_itp",
            "FABRICANTE": "fabr_itp",
        }
    }
}

SYNC_ORDER = [
    "vendedor",
    "transac",
    "propried",
    "grupo",
    "prodserv",
    "ciclos_safra",
    "estoque",
    "tabelas_preco",
    "itens_tabela_preco",
    "condicao",
]
