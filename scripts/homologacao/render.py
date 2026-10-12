import matriz
linhas, tot, ap, geral = matriz.resumo()
resumo = "| Área | Passou | Parcial | Falhou | Não testável | Aplicáveis | Aderência |\n|---|---|---|---|---|---|---|\n"
for a, nome, p, h, f, n, apl, nota in linhas:
    resumo += f"| {nome} | {p} | {h} | {f} | {n} | {apl} | {nota:.0f}% |\n"
resumo += f"| **Total** | {tot['P']} | {tot['~']} | {tot['F']} | {tot['N']} | {ap} | **{geral:.0f}%** |\n"
simb = {"P":"passou","~":"parcial","F":"falhou","N":"não testável"}
mat = ""
for a, nome in matriz.AREAS.items():
    mat += f"\n### {nome}\n\n| # | Cenário | Resultado | Achado ou nota |\n|---|---|---|---|\n"
    for i, ar, c, r, o in matriz.M:
        if ar == a: mat += f"| {i} | {c} | {simb[r]} | {o} |\n"
s=open("spec_corpo.md",encoding="utf-8").read().replace("__GERAL__",f"{geral:.0f}").replace("__RESUMO__",resumo).replace("__MATRIZ__",mat)
out="/c/rep/.worktrees/EasyStok/homologacao-atendimento-1318/docs/plan/atendimento-whatsapp/13-homologacao-organica.md"
import os
open(os.path.expanduser("C:/rep/.worktrees/EasyStok/homologacao-atendimento-1318/docs/plan/atendimento-whatsapp/13-homologacao-organica.md"),"w",encoding="utf-8").write(s)
print(f"{geral:.1f}", len(s))
