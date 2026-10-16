# Fraseologia da Torre (pt-BR)

Notas para o M4. Fonte: **MCA 100-16, edição 2025** (Portaria DECEA/DNOR1 nº 1.716, de 28/04/2025, BCA nº 84 de 08/05/2025), **Capítulo IV, Seção IV, "Serviço de Controle de Aeródromo", Art. 121 a 140**, mais os artigos gerais que ela usa (Art. 25 horas, 29 altímetro, 31 vento, 47 a 50 regras da fraseologia). Lido no PDF da edição 2025, que o mantenedor baixou para consulta local; o arquivo **não** vai para o repositório (`.gitignore`), só estas notas, com o artigo de cada frase. A página oficial avisa que o manual muda em **26/11/2026**: conferir a edição vigente antes de mexer nos templates.

Os exemplos do manual usam indicativos como TAM 3250 e PT BFA; aqui o indicativo é `PRABC` ("PAPA ROMEO ALFA BRAVO CHARLIE", como em `solo-pt-br.md`). Números, letras, pistas e frequências saem como em "Como falar" no `solo-pt-br.md`; vento e hora estão em "Como falar vento e hora", abaixo.

Regra geral do manual (Art. 47, § 2º): a fraseologia vai sempre junto do indicativo da aeronave. **Art. 50:** a palavra **"decolagem"** (take-off) só é dita quando a aeronave está **autorizada a decolar** ou quando se **cancela** a autorização; em qualquer outro caso se diz "partida".

## Saída: o ponto de espera, o alinhamento e a decolagem (Art. 126, 127)

| ID | Quem | Frase | Fonte |
|---|---|---|---|
| T-DEP-REQ | piloto | "Torre Rio, TIB 5570, ponto de espera pista 17, pronto para partida." | Art. 126, I |
| T-DEP-IMMQ | controle | "TAM 3330, condições de partida imediata?" | Art. 126, II |
| T-DEP-LUW-NEG | controle | "GLO 1495, negativo, **alinhe e mantenha** devido a aeronave cruzando a pista." | Art. 126, II |
| T-DEP-LUW-COND | controle | "GLO 1256, após o Airbus (na final / na curta final) **alinhar e manter** atrás do Airbus." | Art. 49 (autorização condicional) |
| T-TKO-OK | controle | "PTB 1405, pista 09, **autorizada decolagem**, vento 095 graus, 10 kt." | Art. 126, II |
| T-TKO-OK2 | controle | "TAM 3256, pista 18, **autorizado decolar**, vento 095 graus, 10 kt." | Art. 126, II |
| T-TKO-IMM | controle | "TAM 3203, pista 09 esquerda, autorizado decolagem imediata, vento 095 graus, 10 kt." | Art. 126, II |
| T-TKO-IMM2 | controle | "ONE 6340, pista 15, decole imediatamente ou livre a pista." | Art. 126, II |
| T-TKO-CANCEL | controle | "PT OFN, mantenha a posição, cancele a decolagem, repito, cancele a decolagem." | Art. 126, II |
| T-TKO-STOP | controle | "TAM 3310, pare imediatamente." | Art. 126, II |
| T-AIR | controle | "TAM 3661, decolado aos 26, chame o Controle Rio, 119,0." | Art. 127 |

Pontos a notar:

- **A ordem da autorização de decolagem é: indicativo, pista, autorização, vento** (Art. 126, II: "designação da pista e autorização para decolagem; direção e velocidade do vento; instruções complementares").
- **"Alinhe e mantenha"** (no manual, o "line up and wait") aparece **só dentro de exemplos** (o negativo do Art. 126 e a autorização condicional do Art. 49): o manual **não traz um exemplo isolado** da instrução simples. A forma "PRABC, alinhe e mantenha, pista 15." é montada a partir desses exemplos. `TODO(fraseologia)`: confirmar com o mantenedor (em especial se a pista vai antes: "PRABC, pista 15, alinhe e mantenha."). **Não é "alinhe e aguarde"**, como se diz na prática de outros países.
- **Hora da decolagem (Art. 127):** "decolado aos 26" são **só os minutos**, dígito a dígito (Art. 25 e 23: "aos DOIS MEIA", porque o 6 é MEIA). O § 1º diz que, quando o DECEA autoriza, a hora **não é dada** em aeródromos específicos; o `TODO(fraseologia)` é decidir se o Fonia a diz.
- **"Chame o Controle Rio, 119,0":** a passagem seguinte é o órgão de Controle/Aproximação, com a frequência. O Fonia só tem a Torre e o Solo, então depois da decolagem o controlador **libera a frequência** (formulação a decidir, `TODO(fraseologia)`), ou chama o "Controle" se o aeroporto tiver Aproximação nos dados.
- **Complementos que o manual lista** (Art. 126, II, "instruções complementares"): "após a partida, curva à esquerda"; "observe máquinas à direita da pista". Ficam para quando houver dados de saída.

## Chegada: circuito, final e pouso (Art. 128 a 132)

| ID | Quem | Frase | Fonte |
|---|---|---|---|
| T-ARR-REQ | piloto | "Torre Brasília, TAM 3282, 5 milhas ao sul do aeródromo, 3.500 pés, instruções para pouso." | Art. 128 |
| T-ARR-CIRC | controle | "TAM 3282, autorizado para o circuito de tráfego, pista 10, vento 110 graus/12 kt, ajuste do altímetro ou QNH 1015, reporte perna do vento." | Art. 129 |
| T-ARR-STRAIGHT | controle | "PTMAD, autorizada aproximação direta, pista 11, vento 150 graus, 15 kt, QNH 1013. Reporte final." | Art. 129 |
| T-ARR-RHAND | controle | "PT BFA, faça aproximação pela direita, pista 02, vento 040 graus/10 kt, ajuste do altímetro ou QNH 1014, reporte perna base." | Art. 129 |
| T-DWN-REP | piloto | "Torre São José, PT BFA, perna do vento, pista 35." | Art. 130, I |
| T-DWN-INS | controle | "PT BFA, reporte perna base." / "é o número 2 para pouso, siga B737 na final, reporte na perna base." | Art. 130, II |
| T-BASE-REP | piloto | "Torre São José, PT BFA, perna base." | Art. 131, I |
| T-BASE-INS | controle | "PT BFA, reporte na final." | Art. 131, II |
| T-FIN-REP | piloto | "Torre São José, PT BFA, final." (também: "na final, condições visuais"; "final longa") | Art. 132, I |
| T-LDG-OK | controle | "TAM 3702, avistado, pista 09, **pouso autorizado**, vento 095 graus/12 kt." | Art. 132, II |
| T-LDG-OK2 | controle | "GLO 1671, pista 15, pouso autorizado, vento 120 graus/10 kt." / "PT BFA, pista 20 esquerda, autorizado pouso, vento 280°/10 kt." | Art. 132, II |
| T-LDG-NUM2 | controle | "PT VHC, prossiga na aproximação, é número 2 para pouso." | Art. 132, II |
| T-LDG-TG | controle | "PT JSA, pista 25, autorizado toque e arremetida." / "PT EEA, faça pouso completo." | Art. 132, II |
| T-LDG-GA | controle | "PT GIM, arremeta e circule." / "PT LOV, arremeta em frente." | Art. 132, II |
| T-GA-REP | piloto | "Torre Brasília, PT CTA, iniciando procedimento de aproximação perdida." → "Ciente, PT CTA, chame Controle Brasília 119,20." | Art. 136 |

Observações da pista (Art. 132, II, "instruções complementares"): "pista molhada", "pista empoçada", "geada", "cautela quanto à esteira de turbulência", "observe bando de aves nas vizinhanças do aeródromo" etc. Fora do M4 até haver fonte para elas.

## Depois do pouso (Art. 133, 134)

| ID | Quem | Frase | Fonte |
|---|---|---|---|
| T-VAC | controle | "TAM 3320, no solo aos 02, após livrar a pista chame Solo (nome) 121,9." | Art. 133 |
| T-VAC-NOTE | (regra) | O piloto só muda para o Solo **depois de livrar a pista**, a menos que o controlador diga o contrário. | Art. 133, NOTA |
| T-VAC-TAXI | controle | "TAM 3320, no solo aos 10, taxie via primeira interseção à direita, reporte pista livre." | Art. 133 |
| T-VAC-REP | piloto | "Solo Porto Alegre, TAM 3320, livrando a pista na interseção F." | Art. 134, I |
| T-VAC-GND | controle (Solo) | "TAM 3320, taxie para o pátio 1 via taxiway D." | Art. 134, II |

## Como falar vento e hora (o que o `SpokenText` ainda não faz)

- **Vento (Art. 31, tabela 9):** a palavra **VENTO** + a direção em **três algarismos** + **GRAUS**, depois a velocidade e **NÓ/NÓS**, tudo **dígito a dígito**: vento 220°/8 kt é **"VENTO DOIS DOIS ZERO GRAUS, OITO NÓS"**. Com rajadas: "VENTO DOIS DOIS ZERO GRAUS, CINCO ZERO NÓS, RAJADAS DE SETE ZERO NÓS". **Calmo** (menos de 1 nó): "vento calmo". Velocidade de 100 nós ou mais: "vento superior a 99 nós".
  Os exemplos do manual escrevem "vento 095 graus, 10 kt" na forma escrita; **falados**, seguem o Art. 31.
- **Ajuste de altímetro (Art. 29, já dito pelo app quando há METAR):** cada dígito separado, "AJUSTE DE ALTÍMETRO (ou QNH) UNO ZERO UNO SETE"; 1000 hPa é "UNO MIL" (já implementado em `SpokenText.Qnh`).
- **Hora (Art. 25):** só os **minutos**, dígito a dígito ("aos DOIS MEIA", o 6 é MEIA); a hora completa só quando houver risco de confusão.

## Fora do escopo (por ora)

- **Art. 121 e 122** (informações de partida, autorização ATC, ATIS): são do **Tráfego** (Clearance Delivery), com pista, vento, QNH, temperatura, visibilidade e hora. Entram com o ATIS e o METAR (M4, mais adiante).
- **Art. 135 a 138** (pistas paralelas, DPSI), **Art. 139 e 140** (sem rádio) e o **Anexo III** (veículos e reboque na área de manobras).
- **Condições de pista** e **instruções de saída/SID** complementares.

## Para o Fonia (implementado em 2026-10-14: alinhar e decolar)

- A Torre usa o **mesmo estilo do Solo**: responde sem dizer o próprio nome, como nos exemplos do manual; a posição aparece só quando ela chama ("chame Torre Curitiba ...").
- **Frases implementadas:** "PRABC, PISTA UNO CINCO, alinhe e mantenha." e "PRABC, PISTA UNO CINCO, autorizado decolagem." (com "VENTO ... GRAUS, ... NÓS" no fim quando houver vento), na ordem do Art. 126, II. Os dois `TODO(fraseologia)` seguem abertos: "alinhe e mantenha" é montado a partir dos exemplos do manual (a instrução simples não tem exemplo isolado), e o vento é omitido enquanto o app não tem de onde tirá-lo.
- **Cotejamento (Art. 12):** devem ser cotejadas as autorizações e instruções para **entrar, pousar, decolar, manter-se a certa distância, cruzar, taxiar e regressar em qualquer pista**. O Fonia cobra o cotejamento de **alinhe e mantenha**, da **autorização de decolagem**, do **circuito** e do **pouso**, mas pelo critério da prática (ver ARCHITECTURE, "Cotejamento"): o indicativo e a ação bastam, a pista é opcional e, se dita errada, é "negativo". Decisão do mantenedor em 2026-10-15: o piloto no ponto de espera não precisa repetir a frase inteira.
- **Implementado em 2026-10-14 (3b): "decolado aos MM."** A Torre fala sozinha quando a telemetria mostra a decolagem (`AirborneNotice`, ver ARCHITECTURE §12), só com os minutos em UTC. `TODO(fraseologia)`: o manual continua com "chame o Controle ..." (órgão seguinte), que os dados de aeroporto não têm, e o § 1º do Art. 127 deixa o DECEA dispensar a hora em alguns aeródromos.
- **Implementado em 2026-10-14 (circuito e pouso):** "autorizado para o circuito de tráfego, PISTA ..., reporte perna do vento." (Art. 129), "reporte perna base." (130), "reporte na final." (131), "PISTA ..., pouso autorizado." (132, com vento quando houver) e, para a arremetida do piloto, "ciente." (`TODO(fraseologia)`: o manual só mostra o Art. 136, que passa para o Controle). Os pedidos de reporte não são cotejados; o circuito e o pouso, sim. Ver ARCHITECTURE §13. Também com `TODO(fraseologia)`: o QNH do Art. 129 e a aproximação direta.
- **Implementado em 2026-10-14 (depois do pouso):** "PRABC, no solo aos ZERO DOIS, após livrar a pista chame Solo Curitiba UNO UNO OITO DECIMAL ZERO." (Art. 133, falada sozinha pela telemetria), "chame Solo ..." quando o piloto reporta pista livre à Torre e "taxie para o pátio." do Solo (Art. 134, II). `TODO(fraseologia)`: o manual diz "taxie para o pátio 1 via taxiway D", e os dados não têm pátio nem taxiway; sem Solo no aeroporto, só "no solo aos MM", pois o "taxie via ..., reporte pista livre" também pede taxiway.
- **Vento e ajuste de altímetro (METAR, 2026-10-15):** quando o METAR chega, o vento sai nas autorizações de decolagem, circuito e pouso (magnético, ao múltiplo de 10) e o "ajuste de altímetro UNO ZERO UNO TRÊS" no táxi e no circuito, na ordem do manual (vento, depois altímetro, depois o reporte). Dito por extenso, sem a sigla QNH, que a voz não lê. Ver ARCHITECTURE §8.
- **Ainda sem implementar:** o "chame o Controle ..." do Art. 127 (os dados não têm Controle) e a aproximação direta do Art. 129.
- **Palavras que o NLU do piloto entende** (ele fala como quiser; estas são as pistas): "pronto para partida", "pronto para decolar/decolagem", "solicito decolagem", "ponto de espera", "alinhado", "decolando", "iniciando a decolagem". Ficam para o item do circuito: "perna do vento", "base", "final", "no solo", "livrando a pista", "pista livre", "arremetendo", "toque e arremetida".
- **Telemetria no lugar da fala do piloto** (onde existir): decolagem (`OnGround` passa a falso e a velocidade sobe), pouso (volta ao solo) e pista livre (velocidade de táxi) podem confirmar o que o piloto reporta; o relato continua sendo o gatilho, porque as cabeceiras ainda não têm coordenadas nos dados.
