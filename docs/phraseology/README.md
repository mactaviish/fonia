# Fraseologia: fontes e regras de uso

Esta pasta guarda **as nossas notas** sobre a fraseologia que o Fonia usa. Os manuais em si não ficam no repositório: citamos a seção e linkamos a fonte, para não redistribuir o PDF e para a referência não ficar velha em silêncio.

## Fontes

| Prioridade | Fonte | Para quê |
|---|---|---|
| **1. Oficial** | **MCA 100-16 "Fraseologia de Tráfego Aéreo", edição 2025**, Portaria DECEA/DNOR1 nº 1.716, de 28/04/2025 (em vigor desde 08/05/2025). Página oficial: <https://publicacoes.decea.mil.br/publicacao/mca-100-16> | O que o **controlador fala**, e as regras de dizer números, letras e indicativos. |
| 2. Internacional | ICAO Doc 4444 (PANS-ATM), cap. 12. O MCA 100-16/2025 incorpora a Emenda nº 11. | Quando o MCA for omisso. |
| 3. Prática | Portal ATC da VATSIM Brasil, "Exemplos de Comunicação": <https://atc.vatsim.com.br/documentos/manuais/fraseologia-aeronautica/exemplos-comunicacao/> | O que os **pilotos realmente dizem** e variantes de uso comum. É uma referência secundária ("prática comum"), não o manual do DECEA. |

Regra do projeto: o que o controlador **fala** segue o MCA 100-16 vigente. Onde o manual é omisso ou há variante de uso, anotamos a origem e decidimos com o mantenedor. Frase sem referência leva `// TODO(fraseologia)`.

## O que muda se o manual mudar

- **A edição 2025 é a vigente hoje.** A página oficial avisa que ela **"será alterada" em 26/11/2026** por uma nova edição. Quando isso acontecer, comparar a nova com esta e atualizar as notas e os templates.
- As edições de **2016** (Portarias 36/DGCEA e 231/DGCEA) e a de 2020 (Portaria 290/DGCEA) estão **revogadas**. Diferenças que importam, vistas na comparação 2016 → 2025: o alfabeto passou a "Romeo" (antes "Romeu"), a resposta de partida passou a "acionamento aprovado" (antes "livre acionar") e o **`pushback` entrou no manual** (Art. 123). Notas ou código baseados no PDF de 2016 estão errados nesses pontos.
- Como conferir a edição vigente: abrir a página oficial acima (o portal mostra "Em vigor em" e avisos de alteração futura), olhar a Portaria da primeira página e o "Revoga".

## O PDF do manual

Para ler o manual, baixe o PDF da edição vigente na página oficial e guarde **fora do git**: a raiz do repositório (`/*.pdf`) e `docs/fontes-locais/` estão no `.gitignore`. O projeto não redistribui o manual: as notas citam o artigo e a frase.

## Arquivos

- [`solo-pt-br.md`](solo-pt-br.md): catálogo da fraseologia do Solo usada no M2 (acionamento, pushback, táxi, readback), com a fonte de cada frase, e como falar números, letras, pistas e indicativos.
- [`torre-pt-br.md`](torre-pt-br.md): catálogo da fraseologia da Torre usada no M4 (saída: ponto de espera, alinhar, decolagem; chegada: circuito, final, pouso; depois do pouso), com artigo de cada frase, como falar vento e hora e o que fica de fora.
- [`atis-pt-br.md`](atis-pt-br.md): o ATIS (ICA 100-37, Art. 759 a 777): ordem dos itens, regras de vento, visibilidade, nuvens e designador, e o que o app ainda não diz.

## O que o NLU não deve herdar daqui

Este material descreve o que o **controlador** diz. O **piloto** fala como quiser: "bom dia", "ok", "solicito táxi", "pronto pra taxiar", fora de ordem, com palavras a mais. O NLU interpreta a intenção e os dados (indicativo, pista, posição, destino) em vez de comparar frases inteiras, e só cai em "não entendi" quando de fato não consegue (o controlador então diz "PRABC, repita").
