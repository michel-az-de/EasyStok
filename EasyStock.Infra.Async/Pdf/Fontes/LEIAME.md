# Fontes do resumo do pedido (#2929)

O QuestPDF não escolhe o peso de uma fonte variável pelo nome da família: com `NunitoSans.ttf` (variável) o texto
caía na fonte padrão, e com nome próprio saía sempre em ExtraLight. Por isso a Nunito Sans vai aqui em três
instâncias estáticas, geradas do arquivo variável de `EasyStock.Api/wwwroot/impressao/fontes/NunitoSans.ttf`
(licença OFL, sem nome reservado; cópia em `NunitoSans-OFL.txt`).

| Arquivo | wght | wdth | opsz | YTLC |
|---|---|---|---|---|
| `NunitoSans-Regular.ttf` | 400 | 100 | 12 | 500 |
| `NunitoSans-SemiBold.ttf` | 600 | 100 | 12 | 500 |
| `NunitoSans-Bold.ttf` | 700 | 100 | 12 | 500 |

Para refazer (fontTools 4.63):

```
fonttools varLib.instancer NunitoSans.ttf wght=400 wdth=100 opsz=12 YTLC=500 -o NunitoSans-Regular.ttf
```

A Lora é cópia do arquivo variável da impressão (`EasyStock.Api/wwwroot/impressao/fontes/Lora.ttf`, licença em
`Lora-OFL.txt`): o QuestPDF a reconhece pelo nome e varia o peso. Fica copiada aqui, e não linkada, porque a imagem
do Worker não leva a pasta `EasyStock.Api`.
