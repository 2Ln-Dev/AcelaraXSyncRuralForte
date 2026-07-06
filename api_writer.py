"""
Escreve os dados-base no AceleraX pela **API HTTP** (POST autenticado por
`X-Api-Key`), em vez de gravar direto no Supabase. Substitui o supabase_writer.

Diferenças em relação ao writer antigo:
- NÃO envia `codi_ten` — o tenant é resolvido no servidor a partir da API key.
- NÃO resolve `erp_id -> codi_tab` — o endpoint de tabela de preço faz isso.
- Um endpoint por tabela; a tabela de preço (header + itens) vai num só request.
"""
import time
import requests

RETRY_DELAY = 5

# entidade (chave do tables_spec) -> slug do endpoint
# POST {base}/api/v1/integracao/base/<slug>
ENTITY_TO_SLUG = {
    "vendedor": "vendedores",
    "condicao": "condicoes",
    "transac": "clientes",
    "propried": "propriedades",
    "grupo": "grupos",
    "prodserv": "produtos",
    "ciclos_safra": "ciclos",
    "estoque": "estoque",
}

# Tabela de preço é combinada (header + itens) num único endpoint.
SLUG_TABELAS_PRECO = "tabelas-preco"


class ApiAuthError(Exception):
    """Chave inválida/revogada (HTTP 401) — interrompe o sync."""
    pass


class ApiError(Exception):
    """Falha de gravação/rede num request (não-auth) — o sync segue e reporta."""
    pass


class ApiWriter:
    def __init__(self, config):
        self.base_url = config.acelera_api_url.rstrip("/")
        self.api_key = config.acelera_api_key
        self.batch_size = config.batch_size
        self.timeout = getattr(config, "api_timeout_seconds", 30)

    def connect(self):
        # HTTP é stateless — nada a abrir. Mantido por simetria com o main().
        pass

    def _headers(self):
        return {"X-Api-Key": self.api_key, "Content-Type": "application/json"}

    def _post(self, slug, payload):
        """POST num endpoint de base. Devolve o objeto `data` da resposta.
        Faz 1 retry (5s) em falha de rede/5xx/429. 401 -> ApiAuthError."""
        url = f"{self.base_url}/api/v1/integracao/base/{slug}"
        last = None
        for attempt in range(2):
            try:
                resp = requests.post(url, json=payload, headers=self._headers(), timeout=self.timeout)
            except requests.RequestException as e:
                last = e
                if attempt == 0:
                    time.sleep(RETRY_DELAY)
                    continue
                raise ApiError(f"Falha de rede em {slug}: {e}") from e

            if resp.status_code == 401:
                raise ApiAuthError(f"401 em {slug}: chave inválida ou revogada")

            if resp.status_code >= 500 or resp.status_code == 429:
                last = ApiError(f"HTTP {resp.status_code} em {slug}: {resp.text[:200]}")
                if attempt == 0:
                    time.sleep(RETRY_DELAY)
                    continue
                raise last

            data = resp.json() if resp.content else {}
            if resp.status_code >= 400:
                msg = ""
                if isinstance(data, dict):
                    msg = (data.get("error") or {}).get("message", "")
                raise ApiError(f"HTTP {resp.status_code} em {slug}: {msg or resp.text[:200]}")
            return (data or {}).get("data", {})

        raise ApiError(f"Falha em {slug}: {last}")

    def post_items(self, slug, rows):
        """Upsert em lote de uma entidade simples. Envia em chunks de batch_size.
        Erros não-auth de um chunk são coletados (o sync marca exit 1 e segue)."""
        sent = saved = 0
        errors = []
        for i in range(0, len(rows), self.batch_size):
            chunk = rows[i:i + self.batch_size]
            if not chunk:
                continue
            try:
                data = self._post(slug, {"items": chunk})
                sent += data.get("recebidos", len(chunk))
                saved += data.get("gravados", 0)
            except ApiError as e:
                errors.append({"batch_start": i, "error": str(e)})
        return {"sent": sent, "saved": saved, "errors": errors}

    def post_tabelas_preco(self, headers, items):
        """Header + itens da tabela de preço. O servidor resolve erp_id->codi_tab.
        Os headers (poucos) são reenviados em cada chunk de itens, pois o endpoint
        exige `tabelas` presente e os usa para resolver os itens."""
        if not headers:
            return {"headers_saved": 0, "items_saved": 0, "orphans": 0,
                    "errors": [{"batch_start": 0, "error": "sem headers de tabela de preço"}]}

        errors = []
        headers_saved = items_saved = orphans = 0

        if not items:
            try:
                data = self._post(SLUG_TABELAS_PRECO, {"tabelas": headers})
                headers_saved = (data.get("tabelas") or {}).get("gravados", 0)
            except ApiError as e:
                errors.append({"batch_start": 0, "error": str(e)})
            return {"headers_saved": headers_saved, "items_saved": 0, "orphans": 0, "errors": errors}

        for i in range(0, len(items), self.batch_size):
            chunk = items[i:i + self.batch_size]
            try:
                data = self._post(SLUG_TABELAS_PRECO, {"tabelas": headers, "itens": chunk})
                headers_saved = (data.get("tabelas") or {}).get("gravados", headers_saved)
                it = data.get("itens") or {}
                items_saved += it.get("gravados", 0)
                orphans += it.get("orfaos", 0)
            except ApiError as e:
                errors.append({"batch_start": i, "error": str(e)})
        return {"headers_saved": headers_saved, "items_saved": items_saved,
                "orphans": orphans, "errors": errors}
