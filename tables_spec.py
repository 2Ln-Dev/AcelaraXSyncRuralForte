"""
UPSERT keys and De/Para column mappings per spec.
"""

TABLES_SPEC = {
    "transac": {
        "oracle_view": "AGROINSU.V_AX_CLIENTES",
        "supabase_table": "transac",
        "upsert_key": ("codi_ten", "codi_tra"),
        "columns": {
            "CODI_TRA": "codi_tra",
            "RAZA_TRA": "raza_tra",
            "DESC_MUN": "desc_mun",
            "ESTA_MUN": "esta_mun",
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
        "upsert_key": ("codi_ten", "nome_csa"),
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
            "CODI_PSV_VINC": "codi_pro",
            "DESC_PSV_VINC": "desc_pro",
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
            "UNID_PSV": "unid_est",
            "CODI_GPR": "codi_gpr",
            "DESC_GPR": "desc_gpr",
            "CODI_SBG": "codi_sbg",
            "DESC_SBG": "desc_sbg",
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
            "CODI_GPR": "codi_gpr",
            "CODI_SBG": "codi_sbg",
        }
    },
    "grupo": {
        "oracle_view": "AGROINSU.V_AX_GRUPOS",
        "supabase_table": "grupo",
        "upsert_key": ("codi_ten", "codi_gpr"),
        "columns": {
            "CODI_GPR": "codi_gpr",
            "DESC_GPR": "desc_gpr",
        }
    },
    "subgrupo": {
        "oracle_view": "AGROINSU.V_AX_SUBGRUPOS",
        "supabase_table": "subgrupo",
        "upsert_key": ("codi_ten", "codi_sbg"),
        "columns": {
            "CODI_SBG": "codi_sbg",
            "DESC_SBG": "desc_sbg",
        }
    },
    "tabelas_preco": {
        "oracle_view": "AGROINSU.V_AX_TABELA_PRECO",
        "supabase_table": "tabelas_preco",
        "upsert_key": ("codi_ten", "nome_tab"),
        "is_header": True,
        "columns": {
            "ID_CTAB": "erp_id",
            "NOME": "nome_tab",
            "SITU_CTAB": "stat_tab",
            "ORIGEM": "orig_tab",
        }
    },
    "itens_tabela_preco": {
        "oracle_view": "AGROINSU.V_AX_TABELA_PRECO",
        "supabase_table": "itens_tabela_preco",
        "upsert_key": ("erp_id", "codi_pro"),
        "is_items": True,
        "columns": {
            "ID_CTAB": "erp_id",
            "CODI_PSV": "codi_pro",
            "CUSTO": "prrs_itp",
        }
    }
}

SYNC_ORDER = [
    "transac",
    "propried",
    "grupo",
    "subgrupo",
    "prodserv",
    "ciclos_safra",
    "estoque",
    "tabelas_preco",
    "itens_tabela_preco",
]
