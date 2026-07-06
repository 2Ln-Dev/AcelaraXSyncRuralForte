"""
API de integração — Gerar Tabela de Preço (acelera-x-sync).

POST /gerar-tabela  → recebe os parâmetros do cabeçalho, checa idempotência em
APEX_GERTAB_LOG (ID_GER+MOEDA_ALVO+COMMODITY+CONDICAO+RT_REAIS+RT_DOLAR); se já
existe, devolve os códigos existentes; senão gera (CABTAB+TABELA+histórico+INDVALOR),
loga e devolve os códigos. Origem R$=1, US$=2.

Sem autenticação (uso em rede interna/VPN).

Rodar:
  uvicorn gerar_tabela_api:app --host 0.0.0.0 --port 8100
"""
import os

from dotenv import load_dotenv
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

import gerar_tabela_pricing as gt

load_dotenv(".env", override=True)

try:
    import cx_Oracle as _ora
except ModuleNotFoundError:
    import oracledb as _ora
    try:
        _ora.init_oracle_client()
    except Exception:
        pass


def _connect():
    dsn = (
        f"(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)"
        f"(HOST={os.environ['ORACLE_HOST']})(PORT={os.environ.get('ORACLE_PORT','1521')})))"
        f"(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME={os.environ['ORACLE_SID']})))"
    )
    return _ora.connect(
        user=os.environ["ORACLE_USER"],
        password=os.environ["ORACLE_PASSWORD"],
        dsn=dsn,
    )


app = FastAPI(title="AceleraX — Gerar Tabela de Preço", version="1.0.0")


class GerarTabelaIn(BaseModel):
    id_ger: int = Field(..., description="APEX_GER_TABELAS.ID (versão liberada)")
    condicao: str = Field(..., description="VISTA ou PRAZO")
    moeda_alvo: str = Field(..., description="R$, US$ ou BARTER")
    commodity: int | None = Field(None, description="CODI_IND (obrigatório p/ BARTER)")
    rt_reais: float | None = None
    rt_dolar: float | None = None
    cambio: float | None = Field(None, description="Default: PTAX da condição na versão")
    id_modelo: int = Field(..., description="TABE_CTA da CABTAB modelo")
    origem_chamada: str | None = Field(None, description="Identificação de quem chamou (log)")


_Q_IDEMP = """
SELECT ORIGEM, ORIGEM_COD, TABE_CTA, QTD_ITENS
FROM   AGROINSU.APEX_GERTAB_LOG
WHERE  ID_GER = :id_ger
  AND  MOEDA_ALVO = :moeda
  AND  NVL(COMMODITY,0) = NVL(:commodity,0)
  AND  CONDICAO = :condicao
  AND  NVL(RT_REAIS,0) = NVL(:rt_reais,0)
  AND  NVL(RT_DOLAR,0) = NVL(:rt_dolar,0)
  AND  STATUS = 'GERADA'
ORDER BY ORIGEM_COD
"""


def _busca_existente(conn, p):
    cur = conn.cursor()
    cur.execute(_Q_IDEMP, {
        "id_ger": p["id_ger"], "moeda": p["moeda_alvo"], "commodity": p.get("commodity"),
        "condicao": p["condicao"], "rt_reais": p.get("rt_reais"), "rt_dolar": p.get("rt_dolar"),
    })
    rows = cur.fetchall()
    cur.close()
    return [
        {"origem": r[0], "origem_cod": int(r[1]), "tabe_cta": int(r[2]),
         "qtd_itens": int(r[3]) if r[3] is not None else None}
        for r in rows
    ]


def _grava_log(conn, p, cambio, geradas):
    cur = conn.cursor()
    cur.execute("SELECT NVL(MAX(ID),0) FROM AGROINSU.APEX_GERTAB_LOG")
    base = int(cur.fetchone()[0])
    rows = []
    for i, g in enumerate(geradas):
        rows.append({
            "id": base + i + 1, "id_ger": p["id_ger"], "ciclo": p.get("_ciclo"),
            "moeda": p["moeda_alvo"], "commodity": p.get("commodity"), "condicao": p["condicao"],
            "origem": g["origem"], "origem_cod": g["origem_cod"],
            "rt_reais": p.get("rt_reais"), "rt_dolar": p.get("rt_dolar"), "cambio": cambio,
            "tabe": g["tabe_cta"], "qtd": g["qtd_itens"], "chamada": p.get("origem_chamada"),
        })
    cur.executemany(
        """
        INSERT INTO AGROINSU.APEX_GERTAB_LOG
            (ID, DATA_HORA, ID_GER, CICLO, MOEDA_ALVO, COMMODITY, CONDICAO,
             ORIGEM, ORIGEM_COD, RT_REAIS, RT_DOLAR, CAMBIO, TABE_CTA, QTD_ITENS,
             STATUS, ORIGEM_CHAMADA)
        VALUES
            (:id, SYSTIMESTAMP, :id_ger, :ciclo, :moeda, :commodity, :condicao,
             :origem, :origem_cod, :rt_reais, :rt_dolar, :cambio, :tabe, :qtd,
             'GERADA', :chamada)
        """,
        rows,
    )
    cur.close()


def _resposta(geradas, status):
    """Monta a resposta com o mapa reais(1)/dolar(2)."""
    por_cod = {g["origem_cod"]: g["tabe_cta"] for g in geradas}
    return {
        "status": status,                 # GERADA | REAPROVEITADA
        "reais": por_cod.get(1),          # origem R$ = 1
        "dolar": por_cod.get(2),          # origem US$ = 2
        "geradas": geradas,
    }


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/gerar-tabela")
def gerar_tabela(dados: GerarTabelaIn):
    p = dados.model_dump()
    conn = _connect()
    try:
        # 1) idempotência
        existentes = _busca_existente(conn, p)
        if existentes:
            return _resposta(existentes, "REAPROVEITADA")

        # 2) gera + loga (mesma transação)
        try:
            geradas, cambio, ciclo = gt.gerar(conn, p)
        except gt.ErroNegocio as e:
            conn.rollback()
            raise HTTPException(status_code=400, detail=str(e))

        p["_ciclo"] = ciclo
        _grava_log(conn, p, cambio, geradas)
        conn.commit()
        return _resposta(geradas, "GERADA")
    finally:
        conn.close()
