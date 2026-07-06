"""
Motor de geração de Tabela de Preço (CABTAB + TABELA + histórico + INDVALOR).

PORTADO de appAgroinsumos/backend/app/modules/cadastros/gerar_tabela/service.py.
Standalone: recebe uma conexão cx_Oracle/oracledb e trabalha com dicts (sem FastAPI/pydantic).

⚠️ A fórmula de precificação é a MESMA do appAgroinsumos. Se mudar lá, replicar aqui.
"""
from datetime import date, timedelta

# Alvo da geração
MOEDAS_ALVO = ("R$", "US$", "BARTER")
CONDICOES = ("VISTA", "PRAZO")

# Imposto de venda (planilha Parametros!J9): entra no preço junto da margem
_IMPOSTO_VENDA = 0.00925          # 0,925%

# origem -> código exigido pela integração
ORIGEM_COD = {"R$": 1, "US$": 2}

# Segmento (APEX_GRUPO.desc_gpr) -> sufixo das colunas de parâmetro em APEX_GER_TABELAS
_MAP_SEG = {
    "01-DEFENSIVOS":     "def",
    "02-SEMENTES":       "sem",
    "03-ESPECIALIDADES": "esp",
    "04-FERTILIZANTES":  "fert",
}


class ErroNegocio(Exception):
    pass


def _f(v):
    return float(v) if v is not None else None


# ── Núcleo da precificação ──────────────────────────────────────────
def _conv_moeda(valor, origem, moeda_alvo, cambio):
    if moeda_alvo == "R$":
        return valor if origem == "R$" else valor * cambio
    if moeda_alvo == "US$":
        return valor if origem == "US$" else valor / cambio
    raise ErroNegocio(f"Conversão de moeda inválida: {moeda_alvo}")


def _fator_correcao(meses, origem, correc_rs, antec_rs, correc_uss, antec_uss):
    if origem == "R$":
        taxa = correc_rs if meses >= 0 else antec_rs
    else:
        taxa = correc_uss if meses >= 0 else antec_uss
    return 1 + meses * ((taxa or 0) / 100.0)


def _preco_item(origem, custo, adicional, margem_pct, meses,
                correc_rs, antec_rs, correc_uss, antec_uss,
                moeda_alvo, cambio, rt_reais, rt_dolar):
    """Retorna (basi_tab, cust_tab, margem_efetiva) na moeda-alvo."""
    custo = custo or 0
    adicional = adicional or 0
    divisor = 1 - (margem_pct or 0) / 100.0 - _IMPOSTO_VENDA
    marg_ef = round((margem_pct or 0) + _IMPOSTO_VENDA * 100, 6)

    if moeda_alvo == "BARTER":
        rt = rt_reais if origem == "R$" else rt_dolar
        if not rt:
            raise ErroNegocio(f"Informe a RT para produtos com origem {origem}.")
        novo_custo = (custo + adicional) / rt
        basi = novo_custo / divisor if divisor > 0 else novo_custo
        return round(basi, 6), round(novo_custo, 6), marg_ef

    fator = _fator_correcao(meses, origem, correc_rs, antec_rs, correc_uss, antec_uss)
    custo_eq = custo * fator + adicional
    venda = custo_eq / divisor if divisor > 0 else custo_eq
    basi = _conv_moeda(venda, origem, moeda_alvo, cambio)
    cust = _conv_moeda(custo_eq, origem, moeda_alvo, cambio)
    return round(basi, 6), round(cust, 6), marg_ef


# ── Carga da versão / itens ─────────────────────────────────────────
_QUERY_VERSAO = """
SELECT G.ID, G.NOVA_VERSAO, G.ID_TAB_DEST, CIC.DESC_CIC,
       G.DATA_BASE, G.VENC_MAX, G.PTAX_VISTA, G.PTAX_PRAZO,
       G.CORREC_RS, G.ANTEC_RS, G.CORREC_USS, G.ANTEC_USS,
       G.MARGEM_DEF, G.FAIXA_INI_DEF, G.FAIXA_FIM_DEF,
       G.MARGEM_SEM, G.FAIXA_INI_SEM, G.FAIXA_FIM_SEM,
       G.MARGEM_ESP, G.FAIXA_INI_ESP, G.FAIXA_FIM_ESP,
       G.MARGEM_FERT, G.FAIXA_INI_FERT, G.FAIXA_FIM_FERT,
       G.DATA_FINALIZACAO
FROM   APEX_GER_TABELAS G
INNER  JOIN CICLO CIC ON CIC.CODI_CIC = G.CODI_CIC
WHERE  G.ID = :id
"""

# Leva TODOS os produtos relacionados ao principal via VPRD_PRINCIPAL.
_QUERY_ITENS = """
SELECT VP.CODI_PSV, VP.DESC_PSV, AG.DESC_GPR, IV.ORIGEM, IV.CUSTO, IV.ADICIONAL
FROM   APEX_ITAB_VIG IV
INNER  JOIN VPRD_PRINCIPAL VP ON VP.CODI_PSV_PRI = IV.CODI_PSV
INNER  JOIN PRODSERV PSV ON PSV.CODI_PSV = IV.CODI_PSV
LEFT   JOIN APEX_GRUPO AG ON AG.CHAVEGRUPO = PSV.CODI_GPR || '#' || PSV.CODI_SBG
WHERE  IV.ID_CTAB = :id_ctab
ORDER  BY IV.ORIGEM, VP.DESC_PSV
"""


def _carrega_versao(id_ger, conn):
    cur = conn.cursor()
    cur.execute(_QUERY_VERSAO, {"id": id_ger})
    row = cur.fetchone()
    if not row:
        cur.close()
        raise ErroNegocio("Versão não encontrada.")
    cols = [d[0].lower() for d in cur.description]
    ver = {k: v for k, v in zip(cols, row)}
    if ver.get("data_finalizacao") is None:
        cur.close()
        raise ErroNegocio("Versão ainda não está totalmente liberada.")
    cur.execute(_QUERY_ITENS, {"id_ctab": ver["id_tab_dest"]})
    ver["itens"] = cur.fetchall()
    cur.close()
    return ver


def _param_segmento(ver, desc_gpr):
    seg = _MAP_SEG.get(desc_gpr or "")
    if not seg:
        return None, None, None
    return ver.get(f"margem_{seg}"), ver.get(f"faixa_ini_{seg}"), ver.get(f"faixa_fim_{seg}")


def _meses(ver):
    db, vm = ver.get("data_base"), ver.get("venc_max")
    if not db or not vm:
        return 0.0
    return (vm - db).days / 30.0


def _resolve_params(ver, p):
    if p["condicao"] not in CONDICOES:
        raise ErroNegocio(f"Condição inválida: {p['condicao']}")
    if p["moeda_alvo"] not in MOEDAS_ALVO:
        raise ErroNegocio(f"Moeda-alvo inválida: {p['moeda_alvo']}")
    cambio = p.get("cambio") or (ver["ptax_vista"] if p["condicao"] == "VISTA" else ver["ptax_prazo"])
    cambio = float(cambio) if cambio else None
    if p["moeda_alvo"] == "BARTER":
        if not p.get("commodity"):
            raise ErroNegocio("Selecione a commodity (indexador) para Barter.")
        if not p.get("rt_reais") and not p.get("rt_dolar"):
            raise ErroNegocio("Informe pelo menos uma RT (R$ ou US$) para Barter.")
        if cambio is None:
            raise ErroNegocio("Câmbio (dólar) não informado e sem PTAX na versão.")
    elif cambio is None:
        raise ErroNegocio("Câmbio (dólar) não informado e sem PTAX na versão.")
    return cambio, p.get("rt_reais"), p.get("rt_dolar")


def _indexador_desc(codi_ind, conn):
    if not codi_ind:
        return None
    cur = conn.cursor()
    cur.execute("SELECT DESC_IND FROM INDEXADOR WHERE CODI_IND = :i", {"i": codi_ind})
    r = cur.fetchone()
    cur.close()
    return r[0] if r else None


def _moeda_label(p, conn):
    if p["moeda_alvo"] in ("R$", "US$"):
        return p["moeda_alvo"]
    d = _indexador_desc(p.get("commodity"), conn) or "BARTER"
    return d.split("(")[0].strip() or "BARTER"


def _desc_tabela(ver, p, origem, moeda_label):
    base = f"{ver.get('nova_versao') or ''} {moeda_label} {p['condicao']} ORIG {origem}"
    return base.strip()[:40]


def _calcula(ver, cambio, rt_reais, rt_dolar, p):
    """Agrupa itens calculados por ORIGEM (R$/US$)."""
    meses = _meses(ver)
    grupos = {}
    vistos = {}
    for codi_psv, desc_psv, desc_gpr, origem, custo, adicional in ver["itens"]:
        if origem not in ("R$", "US$"):
            continue
        if p["moeda_alvo"] == "BARTER" and not (rt_reais if origem == "R$" else rt_dolar):
            continue
        if codi_psv in vistos.setdefault(origem, set()):
            continue
        vistos[origem].add(codi_psv)
        margem, _fi, _ff = _param_segmento(ver, desc_gpr)
        basi, cust, marg_ef = _preco_item(
            origem, _f(custo), _f(adicional), _f(margem), meses,
            ver["correc_rs"], ver["antec_rs"], ver["correc_uss"], ver["antec_uss"],
            p["moeda_alvo"], cambio, rt_reais, rt_dolar,
        )
        grupos.setdefault(origem, []).append({
            "codi_psv": codi_psv, "grupo": desc_gpr, "origem": origem,
            "margem": marg_ef, "cust_tab": cust, "basi_tab": basi,
        })
    return grupos


# ── Cabeçalho / cotação ─────────────────────────────────────────────
def _carrega_modelo(id_modelo, conn):
    if not id_modelo:
        return None
    cur = conn.cursor()
    cur.execute("SELECT * FROM CABTAB WHERE TABE_CTA = :modelo", {"modelo": id_modelo})
    row = cur.fetchone()
    if not row:
        cur.close()
        raise ErroNegocio(f"CABTAB modelo {id_modelo} não encontrada.")
    cols = [d[0] for d in cur.description]
    cur.close()
    return {c: v for c, v in zip(cols, row)}


def _valor_cotacao(p, cambio, rt_reais, rt_dolar):
    """INDVALOR.VLOR_VLR em R$: RT US$ × dólar; se só houver RT R$, usa a RT R$."""
    if p["moeda_alvo"] != "BARTER":
        return None
    if rt_dolar and cambio:
        return round(rt_dolar * cambio, 6)
    if rt_reais:
        return round(rt_reais, 6)
    return None


_DATA_VLR_INI = date(1900, 1, 1)
_DATA_VLR_FIM = date(1940, 12, 31)


def _proxima_data_vlr(codi_ind, conn):
    cur = conn.cursor()
    cur.execute(
        """SELECT DISTINCT TRUNC(DATA_VLR) FROM INDVALOR
           WHERE CODI_IND = :i AND DATA_VLR BETWEEN :ini AND :fim""",
        {"i": codi_ind, "ini": _DATA_VLR_INI, "fim": _DATA_VLR_FIM},
    )
    usados = {(r[0].date() if hasattr(r[0], "date") else r[0]) for r in cur.fetchall()}
    cur.close()
    d = _DATA_VLR_INI
    while d <= _DATA_VLR_FIM:
        if d not in usados:
            return d
        d += timedelta(days=1)
    raise ErroNegocio("Sem data livre entre 1900 e 1940 para a cotação do indexador.")


# ── Gravação ────────────────────────────────────────────────────────
_CABTAB_SKIP = {
    "TABE_CTA", "DESC_CTA", "DCAD_CTA", "VMAX_CTA", "DTBA_CTA", "DATA_VLR", "CODI_IND", "SITU_CTA",
    "OBSE_CTA", "DUMANUT", "DINSERT", "DTSYNCAPI", "DATAULTIMASINCRONIZACAOAPI", "DUMANUT_SYS",
    "CODIPESLANREG", "CODIPESMANUTREG",
}
_HORA_SQL = "TO_DSINTERVAL('0 ' || TO_CHAR(SYSDATE,'HH24:MI:SS'))"


def _cria_cabtab(ver, p, origem, modelo, data_vlr, moeda_label, conn):
    cur = conn.cursor()
    cur.execute("SELECT GEN_CABTAB_TABE_CTA.NEXTVAL FROM DUAL")
    new_tabe = int(cur.fetchone()[0])
    barter = p["moeda_alvo"] == "BARTER"
    overrides = {
        "TABE_CTA": new_tabe,
        "DESC_CTA": _desc_tabela(ver, p, origem, moeda_label),
        "VMAX_CTA": ver.get("venc_max"),
        "DTBA_CTA": ver.get("data_base"),
        "DATA_VLR": data_vlr if barter else modelo.get("DATA_VLR"),
        "CODI_IND": p.get("commodity") if barter else modelo.get("CODI_IND"),
        "SITU_CTA": "A",
        "FAPV_CTA": ver.get("correc_rs") if origem == "R$" else ver.get("correc_uss"),
        "FATV_CTA": ver.get("antec_rs") if origem == "R$" else ver.get("antec_uss"),
    }
    copia = {c: v for c, v in modelo.items() if c not in _CABTAB_SKIP and c not in overrides}
    dados = {**copia, **overrides}
    cols = list(dados.keys())
    col_sql = ", ".join(cols + ["DCAD_CTA"])
    val_sql = ", ".join([f":{c}" for c in cols] + ["SYSDATE"])
    cur.execute(f"INSERT INTO CABTAB ({col_sql}) VALUES ({val_sql})", {c: dados[c] for c in cols})
    cur.close()
    return new_tabe


def _grava_itens(new_tabe, itens, ver, conn):
    cur = conn.cursor()
    rows = []
    for it in itens:
        _m, faixa_ini, faixa_fim = _param_segmento(ver, it["grupo"])
        rows.append({
            "tabe": new_tabe, "psv": it["codi_psv"],
            "cust": it["cust_tab"], "marg": it["margem"], "basi": it["basi_tab"],
            "pdes": _f(faixa_ini), "pacr": _f(faixa_fim),
        })
    cur.executemany(
        """
        INSERT INTO TABELA
            (TABE_CTA, CODI_PSV, CUST_TAB, TCUS_TAB, MARG_TAB, BASI_TAB,
             ACRE_TAB, DESC_TAB, COMI_TAB, PDES_TAB, PACR_TAB, SITU_TAB)
        VALUES
            (:tabe, :psv, :cust, '4', :marg, :basi, 0, 0, 0, :pdes, :pacr, 'A')
        """,
        rows,
    )
    cur.close()


def _grava_historico(new_tabe, itens, conn):
    cur = conn.cursor()
    cur.execute("SELECT NVL(MAX(CTRL_HCT),0)+1 FROM HISTORICOCABTAB")
    ctrl_hct = int(cur.fetchone()[0])
    cur.execute(
        f"""INSERT INTO HISTORICOCABTAB (CTRL_HCT, TABE_CTA, DATA_HCT, HORA_HCT, ACAO_HCT)
            VALUES (:ctrl, :tabe, SYSDATE, {_HORA_SQL}, 'I')""",
        {"ctrl": ctrl_hct, "tabe": new_tabe},
    )
    cur.execute("SELECT NVL(MAX(CTRL_HTB),0) FROM HISTORICOTABELA")
    base = int(cur.fetchone()[0])
    rows = [
        {"ctrl": base + i + 1, "psv": it["codi_psv"], "tabe": new_tabe,
         "cust": it["cust_tab"], "marg": it["margem"], "basi": it["basi_tab"]}
        for i, it in enumerate(itens)
    ]
    cur.executemany(
        f"""INSERT INTO HISTORICOTABELA
            (CTRL_HTB, CODI_PSV, TABE_CTA, DATA_HTB, HORA_HTB,
             TCUS_TAB, CUST_TAB, MARG_TAB, BASI_TAB, COMI_TAB, SITU_TAB, ACAO_HTB)
            VALUES (:ctrl, :psv, :tabe, SYSDATE, {_HORA_SQL},
             '4', :cust, :marg, :basi, 0, 'A', 'I')""",
        rows,
    )
    cur.close()


def _grava_indvalor(codi_ind, data_vlr, valor, conn):
    cur = conn.cursor()
    cur.execute(
        """
        MERGE INTO INDVALOR d
        USING (SELECT CODI_EMP FROM CADEMP) e
          ON (d.CODI_IND = :ind AND d.CODI_EMP = e.CODI_EMP AND d.DATA_VLR = :dt)
        WHEN MATCHED THEN UPDATE SET d.VLOR_VLR = :vlr
        WHEN NOT MATCHED THEN
          INSERT (CODI_IND, CODI_EMP, DATA_VLR, VLOR_VLR)
          VALUES (:ind, e.CODI_EMP, :dt, :vlr)
        """,
        {"ind": codi_ind, "dt": data_vlr, "vlr": valor},
    )
    cur.close()


def gerar(conn, p):
    """Gera as tabelas (1 por origem). NÃO faz commit — quem chama controla a transação.
    Retorna (geradas, cambio, ciclo) onde geradas = lista de dicts
    {origem, origem_cod, tabe_cta, qtd_itens, descricao}."""
    ver = _carrega_versao(p["id_ger"], conn)
    cambio, rt_reais, rt_dolar = _resolve_params(ver, p)
    modelo = _carrega_modelo(p["id_modelo"], conn)
    if modelo is None:
        raise ErroNegocio("Informe a CABTAB modelo.")

    label = _moeda_label(p, conn)
    grupos = _calcula(ver, cambio, rt_reais, rt_dolar, p)
    if not grupos:
        raise ErroNegocio("Nenhum item com origem R$/US$ para gerar.")

    data_vlr = None
    if p["moeda_alvo"] == "BARTER":
        data_vlr = _proxima_data_vlr(p["commodity"], conn)
        valor = _valor_cotacao(p, cambio, rt_reais, rt_dolar)
        if valor is None:
            raise ErroNegocio("Não foi possível calcular o valor da cotação (verifique RT US$/dólar).")
        _grava_indvalor(p["commodity"], data_vlr, valor, conn)

    geradas = []
    for origem, itens in sorted(grupos.items()):
        new_tabe = _cria_cabtab(ver, p, origem, modelo, data_vlr, label, conn)
        _grava_itens(new_tabe, itens, ver, conn)
        _grava_historico(new_tabe, itens, conn)
        geradas.append({
            "origem": origem, "origem_cod": ORIGEM_COD[origem],
            "tabe_cta": new_tabe, "qtd_itens": len(itens),
            "descricao": _desc_tabela(ver, p, origem, label),
        })
    return geradas, cambio, ver.get("desc_cic")
