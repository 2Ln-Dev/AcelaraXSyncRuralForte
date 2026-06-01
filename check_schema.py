"""Check codi_ten column capacity by attempting a controlled insert with a UUID-length value."""
import requests, os, json
from dotenv import load_dotenv
load_dotenv()

url = os.getenv('SUPABASE_URL')
key = os.getenv('SUPABASE_KEY')
headers = {
    'apikey': key,
    'Authorization': f'Bearer {key}',
    'Content-Type': 'application/json',
    'Prefer': 'return=minimal',
}

TEST_UUID = 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx'  # 36 chars

tables_minimal = {
    'tenants':             {'codi_ten': TEST_UUID, 'slug': '_test_', 'name': '_test_'},
    'transac':             {'codi_ten': TEST_UUID, 'codi_tra': '_TEST_'},
    'propried':            {'codi_ten': TEST_UUID, 'prop_pro': '_TEST_'},
    'ciclos_safra':        {'codi_ten': TEST_UUID, 'nome_csa': '_test_'},
    'estoque':             {'codi_ten': TEST_UUID, 'codi_pro': '_TEST_', 'codi_fil': 0},
    'prodserv':            {'codi_ten': TEST_UUID, 'codi_psv': '_TEST_'},
    'grupo':               {'codi_ten': TEST_UUID, 'codi_gpr': '_TEST_'},
    'subgrupo':            {'codi_ten': TEST_UUID, 'codi_sbg': '_TEST_'},
    'tabelas_preco':       {'codi_ten': TEST_UUID, 'nome_tab': '_test_'},
    'itens_tabela_preco':  {'codi_ten': TEST_UUID, 'codi_pro': '_TEST_', 'codi_tab': '_TEST_'},
}

print(f"Testing codi_ten with 36-char UUID string")
print(f"{'TABLE':<25} {'RESULT'}")
print('-' * 65)

for table, payload in tables_minimal.items():
    resp = requests.post(
        f'{url}/rest/v1/{table}',
        headers=headers,
        data=json.dumps(payload)
    )
    if resp.status_code in (200, 201):
        # Clean up immediately
        requests.delete(
            f"{url}/rest/v1/{table}?codi_ten=eq.{TEST_UUID}",
            headers=headers
        )
        print(f"{table:<25} OK — accepts UUID-length value")
    else:
        err = resp.json() if resp.text else {}
        msg = err.get('message', resp.text[:100])
        # Distinguish "too long" from other errors
        if 'too long' in msg.lower() or 'value too long' in msg.lower():
            print(f"{table:<25} FAIL — varchar too short for UUID: {msg[:60]}")
        else:
            print(f"{table:<25} other error (not length): {msg[:70]}")
