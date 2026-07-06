import pytest
from unittest.mock import MagicMock, patch
from api_writer import ApiWriter, ApiAuthError, ApiError


@pytest.fixture
def config():
    cfg = MagicMock()
    cfg.acelera_api_url = "https://app.teste.com.br/"
    cfg.acelera_api_key = "key123"
    cfg.batch_size = 3
    cfg.api_timeout_seconds = 10
    return cfg


@pytest.fixture
def writer(config):
    return ApiWriter(config)


def _resp(status, data=None, text=""):
    r = MagicMock()
    r.status_code = status
    r.content = b"x" if data is not None else b""
    r.json.return_value = data or {}
    r.text = text
    return r


def test_base_url_trailing_slash_stripped(writer):
    assert writer.base_url == "https://app.teste.com.br"


def test_post_items_batches_by_size(writer):
    rows = [{"codi_tra": str(i)} for i in range(7)]  # batch_size=3 -> 3 chunks
    with patch("api_writer.requests.post") as post:
        post.return_value = _resp(200, {"data": {"recebidos": 3, "gravados": 3}})
        result = writer.post_items("clientes", rows)

    assert post.call_count == 3
    # url e header corretos
    args, kwargs = post.call_args
    assert args[0] == "https://app.teste.com.br/api/v1/integracao/base/clientes"
    assert kwargs["headers"]["X-Api-Key"] == "key123"
    assert result["saved"] == 9  # 3 por chunk (mock fixo)
    assert result["errors"] == []


def test_post_items_empty_does_not_call(writer):
    with patch("api_writer.requests.post") as post:
        result = writer.post_items("clientes", [])
    post.assert_not_called()
    assert result == {"sent": 0, "saved": 0, "errors": []}


def test_post_items_raises_on_auth(writer):
    with patch("api_writer.requests.post") as post:
        post.return_value = _resp(401, {"error": {"code": "unauthenticated"}})
        with pytest.raises(ApiAuthError):
            writer.post_items("clientes", [{"codi_tra": "1"}])


def test_post_items_collects_non_auth_error(writer):
    with patch("api_writer.requests.post") as post:
        post.return_value = _resp(422, {"error": {"message": "campo-chave ausente"}})
        result = writer.post_items("clientes", [{"codi_tra": "1"}])
    assert result["saved"] == 0
    assert len(result["errors"]) == 1
    assert "campo-chave" in result["errors"][0]["error"]


def test_post_retries_once_on_5xx(writer):
    with patch("api_writer.requests.post") as post, patch("api_writer.time.sleep") as sleep:
        post.side_effect = [_resp(503, text="down"), _resp(200, {"data": {"recebidos": 1, "gravados": 1}})]
        result = writer.post_items("clientes", [{"codi_tra": "1"}])
    sleep.assert_called_once_with(5)
    assert result["saved"] == 1


def test_post_tabelas_preco_sends_header_and_items(writer):
    headers = [{"erp_id": 1, "nome_tab": "T"}]
    items = [{"erp_id": 1, "codi_pro": "P1"}, {"erp_id": 1, "codi_pro": "P2"}]
    with patch("api_writer.requests.post") as post:
        post.return_value = _resp(200, {"data": {
            "tabelas": {"recebidos": 1, "gravados": 1},
            "itens": {"recebidos": 2, "gravados": 2, "orfaos": 0},
        }})
        result = writer.post_tabelas_preco(headers, items)

    args, kwargs = post.call_args
    assert args[0].endswith("/integracao/base/tabelas-preco")
    assert kwargs["json"]["tabelas"] == headers
    assert "itens" in kwargs["json"]
    assert result["headers_saved"] == 1
    assert result["items_saved"] == 2
    assert result["orphans"] == 0


def test_post_tabelas_preco_without_items(writer):
    headers = [{"erp_id": 1, "nome_tab": "T"}]
    with patch("api_writer.requests.post") as post:
        post.return_value = _resp(200, {"data": {"tabelas": {"recebidos": 1, "gravados": 1}}})
        result = writer.post_tabelas_preco(headers, [])
    assert post.call_count == 1
    assert "itens" not in post.call_args.kwargs["json"]
    assert result["headers_saved"] == 1


def test_post_tabelas_preco_no_headers_is_error(writer):
    with patch("api_writer.requests.post") as post:
        result = writer.post_tabelas_preco([], [{"erp_id": 1, "codi_pro": "P1"}])
    post.assert_not_called()
    assert len(result["errors"]) == 1
