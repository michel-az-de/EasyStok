import sys, subprocess
sys.argv=[sys.argv[0]]
import sim
nomes = sys.stdin.read().split() if not sys.stdin.isatty() else []
q = """select c."ContatoNome", to_char(m."EnviadaEm",'HH24:MI:SS'), m."Direcao", m."Autor", m."Status", replace(m."Texto",E'\n',' / ')
 from atendimento_mensagens m join atendimento_conversas c on c."Id"=m."ConversaId" %s order by c."IniciadaEm", m."EnviadaEm" """
w = "where c.\"ContatoNome\" in (%s)" % ",".join("'%s'" % n for n in nomes) if nomes else ""
cur=None
for l in sim.sql(q % w).split("\n"):
    nome,hora,dire,autor,st,txt = l.split("|",5)
    if nome!=cur: print(f"\n### {nome}"); cur=nome
    quem = {"1":"CLIENTE"}.get(dire) or {"1":"?", "2":"AGENTE","3":"HUMANO","4":"SISTEMA"}.get(autor,autor)
    if dire=="1": quem="CLIENTE"
    if "Seja bem-vindo" in txt: txt="[primeira-resposta padrão]"
    print(f"  {hora} {quem:8} st={st}: {txt[:500]}")
