"""Simulador de clientes da homologação (#1318). Fala com a API local homol-api (18510).
Uso: python sim.py wa <telefone> <nome> "texto" ["texto2" ...]    -> cliente WhatsApp (webhook assinado)
     python sim.py site <apelido> "texto" ...                      -> cliente do chat do site
     python sim.py btn <telefone> <nome> <id_botao> <titulo>       -> clique em botão
     python sim.py img <telefone> <nome> [legenda]                 -> imagem recebida
"""
import hashlib, hmac, json, os, subprocess, sys, time, urllib.error, urllib.request

API = "http://127.0.0.1:18510"
SEGREDO = open(os.path.expanduser("~/.easystok/meta-appsecret-teste-local")).read().strip().encode()
PSQL = ["docker", "exec", "homol-postgres", "psql", "-U", "postgres", "-d", "easystock", "-tAF", "|", "-c"]
SLUG = "baba-homol"
ESPERA = int(os.environ.get("ESPERA", "60"))


def sql(q):
    return subprocess.run(PSQL + [q], capture_output=True, text=True, encoding="utf-8").stdout.strip()


def webhook(tel, nome, msg):
    corpo = {"object": "whatsapp_business_account", "entry": [{"id": "waba-homol", "changes": [{"field": "messages", "value": {
        "messaging_product": "whatsapp", "metadata": {"display_phone_number": "5511900000000", "phone_number_id": "homol-001"},
        "contacts": [{"profile": {"name": nome}, "wa_id": tel}], "messages": [msg]}}]}]}
    bruto = json.dumps(corpo, ensure_ascii=False).encode()
    sig = "sha256=" + hmac.new(SEGREDO, bruto, hashlib.sha256).hexdigest()
    req = urllib.request.Request(API + "/api/webhooks/whatsapp", data=bruto, method="POST",
                                 headers={"content-type": "application/json", "X-Hub-Signature-256": sig})
    try:
        with urllib.request.urlopen(req) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code


def msg_texto(tel, texto):
    return {"from": tel, "id": f"wamid.cli.{int(time.time()*1000)}", "timestamp": str(int(time.time())), "type": "text", "text": {"body": texto}}


def saidas(marco, tel_like):
    q = f"""select to_char(m."EnviadaEm",'HH24:MI:SS'), m."Autor", m."Status", coalesce(m."Erro",''), replace(m."Texto",E'\n',' / ')
            from atendimento_mensagens m join atendimento_conversas c on c."Id"=m."ConversaId"
            where m."Direcao"=2 and m."EnviadaEm" > '{marco}' and c."ContatoIdExterno" like '%{tel_like}%' order by m."EnviadaEm" """
    return [l.split("|", 4) for l in sql(q).split("\n") if l.strip()]


def esperar(marco, tel, t0, quiet=10):
    vistas, ultima = 0, time.time()
    while time.time() - t0 < ESPERA:
        time.sleep(2)
        novas = saidas(marco, tel)
        for em, autor, status, erro, texto in novas[vistas:]:
            print(f"  [{time.time()-t0:5.1f}s] autor={autor} status={status}{' erro='+erro[:50] if erro else ''}: {texto[:600]}")
            ultima = time.time()
        vistas = len(novas)
        if vistas and time.time() - ultima > quiet:
            return vistas
    if not vistas:
        print("  (nenhuma saida em %ds)" % ESPERA)
    return vistas


def http(metodo, caminho, corpo=None, token=None):
    req = urllib.request.Request(API + caminho, method=metodo, data=json.dumps(corpo).encode() if corpo is not None else None,
                                 headers={"content-type": "application/json", **({"X-Chat-Token": token} if token else {})})
    with urllib.request.urlopen(req) as r:
        return json.load(r)


if __name__ == "__main__":
    modo = sys.argv[1]
    if modo == "wa":
        tel, nome, falas = sys.argv[2], sys.argv[3], sys.argv[4:]
        for fala in falas:
            marco = sql("select now()")
            print(f"\nCLIENTE[{nome}]: {fala}")
            t0 = time.time()
            print("  webhook HTTP", webhook(tel, nome, msg_texto(tel, fala)))
            esperar(marco, tel, t0)
    elif modo == "btn":
        tel, nome, bid, titulo = sys.argv[2:6]
        marco = sql("select now()")
        m = {"from": tel, "id": f"wamid.btn.{int(time.time()*1000)}", "timestamp": str(int(time.time())), "type": "interactive",
             "interactive": {"type": "button_reply", "button_reply": {"id": bid, "title": titulo}}}
        print("webhook HTTP", webhook(tel, nome, m)); esperar(marco, tel, time.time())
    elif modo == "img":
        tel, nome = sys.argv[2:4]
        m = {"from": tel, "id": f"wamid.img.{int(time.time()*1000)}", "timestamp": str(int(time.time())), "type": "image",
             "image": {"id": "MEDIA-FAKE-1", "mime_type": "image/jpeg", "caption": sys.argv[4] if len(sys.argv) > 4 else ""}}
        marco = sql("select now()")
        print("webhook HTTP", webhook(tel, nome, m)); esperar(marco, tel, time.time())
    elif modo == "site":
        apelido, falas = sys.argv[2], sys.argv[3:]
        for f in ("modulo.atendimento", "atendimento.canal.chatsite"):
            sql(f"""insert into "TenantFeatureFlags" ("Id","EmpresaId","Feature","Ativo","AlteradoEm","AlteradoPor")
                    select gen_random_uuid(), e."Id", '{f}', true, now(), 'homol' from empresas e
                    where not exists (select 1 from "TenantFeatureFlags" x where x."EmpresaId"=e."Id" and x."Feature"='{f}')""")
        sessao = http("POST", f"/api/public/chat/{SLUG}/sessoes")["data"]
        token = sessao["token"]; print("sessao", sessao.get("sessaoId", ""), "(token guardado em sessao-%s.txt)" % apelido)
        open(f"sessao-{apelido}.txt", "w").write(token)
        visto = set()
        for fala in falas:
            print(f"\nSITE[{apelido}]: {fala}")
            t0 = time.time()
            http("POST", f"/api/public/chat/{SLUG}/mensagens", {"texto": fala}, token)
            quieto, achou = time.time(), False
            while time.time() - t0 < ESPERA:
                time.sleep(1.5)
                for m in http("GET", f"/api/public/chat/{SLUG}/mensagens", token=token)["data"]:
                    if not m["doVisitante"] and m["id"] not in visto:
                        visto.add(m["id"]); achou = True; quieto = time.time()
                        print(f"  [{time.time()-t0:5.1f}s] ATENDENTE: {m['texto'][:600]}")
                if achou and time.time() - quieto > 8:
                    break
            if not achou:
                print("  (sem resposta em %ds)" % ESPERA)
