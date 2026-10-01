import sys, json, time, hashlib, hmac, urllib.request, urllib.error
sys.argv=[sys.argv[0]]
import sim
def post(corpo_bytes, sig):
    h={"content-type":"application/json"}
    if sig is not None: h["X-Hub-Signature-256"]=sig
    req=urllib.request.Request(sim.API+"/api/webhooks/whatsapp", data=corpo_bytes, method="POST", headers=h)
    try:
        with urllib.request.urlopen(req) as r: return r.status
    except urllib.error.HTTPError as e: return e.code
def corpo(msgid, tel, texto, phone="homol-001"):
    return json.dumps({"object":"whatsapp_business_account","entry":[{"id":"w","changes":[{"field":"messages","value":{"messaging_product":"whatsapp","metadata":{"display_phone_number":"5511900000000","phone_number_id":phone},"contacts":[{"profile":{"name":"Teste Seg"},"wa_id":tel}],"messages":[{"from":tel,"id":msgid,"timestamp":str(int(time.time())),"type":"text","text":{"body":texto}}]}}]}]},ensure_ascii=False).encode()
def sig(b): return "sha256="+hmac.new(sim.SEGREDO,b,hashlib.sha256).hexdigest()
n=lambda tel: sim.sql(f"select count(*) from atendimento_mensagens m join atendimento_conversas c on c.\"Id\"=m.\"ConversaId\" where c.\"ContatoIdExterno\" like '%{tel}%' and m.\"Direcao\"=1")
tel="5511933330001"
b=corpo("wamid.seg.1",tel,"oi teste seguranca")
print("assinatura inválida   ->", post(b,"sha256="+"0"*64), "msgs:", n(tel))
print("sem assinatura        ->", post(b,None), "msgs:", n(tel))
print("assinatura correta    ->", post(b,sig(b)), "msgs:", n(tel))
time.sleep(1)
print("replay mesmo wamid    ->", post(b,sig(b)), "msgs (deve seguir 1):", n(tel))
b2=corpo("wamid.seg.2","5511933330002","outro tenant","outro-phone-id")
print("phone_number_id alheio->", post(b2,sig(b2)), "msgs:", n("5511933330002"))
print("JSON quebrado         ->", post(b"{nope",sig(b"{nope")))
grande=corpo("wamid.seg.3","5511933330003","x"*200000)
print("texto 200 KB          ->", post(grande,sig(grande)), "msgs:", n("5511933330003"))
longo=corpo("wamid.seg.4","5511933330004","y"*5000)
print("texto 5000 chars      ->", post(longo,sig(longo)), "msgs:", n("5511933330004"))
# flood: 40 mensagens em 3 s do mesmo número
cods=[]
for i in range(40):
    bb=corpo(f"wamid.seg.f{i}","5511933330005",f"flood {i}"); cods.append(post(bb,sig(bb)))
from collections import Counter
print("flood 40 msgs         ->", dict(Counter(cods)), "msgs gravadas:", n("5511933330005"))
