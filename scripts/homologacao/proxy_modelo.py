"""Proxy de medição para o teste do agente: repassa POST /v1/messages à Fireworks e grava, por chamada,
latência, tokens (entrada, saída, cache) e ferramentas pedidas. Nunca grava cabeçalhos (a chave)."""
import json, sys, time, urllib.error, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DESTINO = "https://api.fireworks.ai/inference"
LOG = sys.argv[1] if len(sys.argv) > 1 else "medicoes-homol.jsonl"


class Proxy(BaseHTTPRequestHandler):
    def do_POST(self):
        corpo = self.rfile.read(int(self.headers.get("content-length", 0)))
        cab = {k: v for k, v in self.headers.items() if k.lower() in ("authorization", "x-api-key", "content-type", "anthropic-version")}
        req = urllib.request.Request(DESTINO + self.path, data=corpo, headers=cab, method="POST")
        t0 = time.time()
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                status, resp = r.status, r.read()
        except urllib.error.HTTPError as e:
            status, resp = e.code, e.read()
        ms = round((time.time() - t0) * 1000)
        reg = {"ts": time.strftime("%Y-%m-%dT%H:%M:%S"), "status": status, "ms": ms}
        try:
            env = json.loads(corpo)
            reg["mensagens"] = len(env.get("messages", []))
            reg["ferramentas_disponiveis"] = len(env.get("tools", []))
            j = json.loads(resp)
            u = j.get("usage", {})
            reg.update(modelo=j.get("model"), stop=j.get("stop_reason"),
                       entrada=u.get("input_tokens", 0), saida=u.get("output_tokens", 0),
                       cache_lido=u.get("cache_read_input_tokens", 0) or 0,
                       ferramentas=[b["name"] for b in j.get("content", []) if b.get("type") == "tool_use"])
        except Exception as ex:
            reg["erro_parse"] = str(ex)[:120]
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps(reg, ensure_ascii=False) + "\n")
        self.send_response(status)
        self.send_header("content-type", "application/json")
        self.send_header("content-length", str(len(resp)))
        self.end_headers()
        self.wfile.write(resp)

    def log_message(self, *a):
        pass


ThreadingHTTPServer(("0.0.0.0", 18610), Proxy).serve_forever()
