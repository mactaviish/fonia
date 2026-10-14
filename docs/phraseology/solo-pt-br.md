# Fraseologia do Solo (pt-BR)

Notas para o M2. Fonte principal: **MCA 100-16, edição 2025** (Portaria DECEA/DNOR1 nº 1.716/2025), citada por artigo. "VATSIM-BR" é a prática descrita no [Portal ATC da VATSIM Brasil](https://atc.vatsim.com.br/documentos/manuais/fraseologia-aeronautica/exemplos-comunicacao/). Veja as prioridades em [README.md](README.md).

Os exemplos usam a numeração do manual (TAM 3702, box 3). Nas frases do controlador, números e letras saem como em "Como falar" abaixo.

## Quem fala com quem (Art. 37)

O nome do órgão vem do indicativo da posição **seguido do nome da localidade**:

| Posição | Português |
|---|---|
| Controle de Solo | SOLO (nome) |
| Torre de Controle de Aeródromo | TORRE (nome) |
| Autorização de Tráfego | TRÁFEGO (nome) |
| Estação Aeronáutica | RÁDIO (nome) |
| Controle e Aproximação | CONTROLE (nome) |

O nome falado de cada aeroporto está em [`data/atc-names.csv`](../../data/atc-names.csv).

## Acionamento e pushback (Art. 123)

O piloto chama o órgão, diz o indicativo, a posição no pátio, o tipo de plano e o destino, e a solicitação.

| ID | Quem | Frase | Fonte |
|---|---|---|---|
| S-ACN-REQ | piloto | "Solo Galeão, TAM 3702, box 3, IFR para Brasília, solicita acionar." | Art. 123, I |
| S-PSH-REQ | piloto | "Solo Galeão, TAM 3250, box 3, IFR para Brasília, solicita pushback." | Art. 123, I |
| S-ACN-OK | controle | "TAM 3702, acionamento aprovado, chame pronto para o táxi." | Art. 123, II |
| S-ACN-WAIT | controle | "TAM 3702, aguarde para acionar." | Art. 123, II |
| S-ACN-DELAY | controle | "TAM 3702, aguarde 5 minutos para acionar, é o número 7 na sequência." | Art. 123, II |
| S-ACN-NEG | controle | "TAM 3702, negativo acionamento devido: (a) aeronave acidentada na pista em uso; (b) condições meteorológicas abaixo dos mínimos; ou (c) aeronave em emergência aproximando-se para pouso." | Art. 123, II |
| S-PSH-OK | controle | "TAM 3250, pushback aprovado, reporte pronto para o táxi." | Art. 123, II |
| S-PSH-WAIT | controle | "TAM 3250, aguarde para pushback devido a B747 taxiando à retaguarda." | Art. 123, II |
| S-PSH-DELAY | controle | "TAM 3250, aguarde 5 minutos para o pushback." | Art. 123, II |
| S-PSH-SIM | controle | "TAM 3250, realize pushback simultâneo com o B737 a sua direita." | Art. 123, II |
| S-PSH-FREE | controle | "TAM 3250, pushback a seu critério." | Art. 123, II |

**Prática (VATSIM-BR):** o pedido combinado e a resposta combinada, também com sentido da cauda ou local do pushback:

| ID | Quem | Frase |
|---|---|---|
| P-ACN-PSH-REQ | piloto | "Solo Brasília, GOL 1419, pátio 1, posição 14, IFR para Guarulhos, solicita autorização para pushback e acionamento." |
| P-ACN-PSH-OK | controle | "GOL 1419, Solo Brasília, autorizado pushback e acionamento, chame pronto para o táxi." |
| P-PSH-TAIL | controle | "AZUL 4187, Solo São Gonçalo, autorizado pushback e acionamento, cauda para a esquerda." |
| P-PSH-WHERE | controle | "VARIG 8898, Solo Confins, autorizado pushback e acionamento na taxiway TANGO." |
| P-ACN-ONLY | controle | "PR-IMO, Solo Marte, autorizado acionamento, chame pronto para o táxi." |

No fluxo com **Tráfego** (autorização de plano, no capítulo do Serviço de Controle de Aeródromo), o piloto pede "pronto para acionamento e pushback" ao Tráfego, que informa a primeira curva e manda chamar o Solo ("Chame Solo Brasília em 121,80"). Fora do M2.

## Táxi (Art. 125)

| ID | Quem | Frase | Fonte |
|---|---|---|---|
| S-TXI-REQ | piloto | "Solo Galeão, TAM 3702, solicita instruções de táxi." | Art. 125, I |
| S-TXI-OK | controle | "TAM 3702, taxie / autorizado táxi para o ponto de espera da pista 10, via taxiway "G", próximo do ponto de espera chame Torre 118,1." | Art. 125, II |
| S-TXI-OK-SHORT | controle | "TAM 3702, taxie / autorizado táxi para o ponto de espera da pista 10." (sem taxiway) | Art. 125, II |
| S-TXI-HOLD | controle | "…mantenha posição fora da pista 02." | Art. 125, II (d) |
| S-TXI-CROSS | controle | "…reporte para cruzamento da pista 02." | Art. 125, II (e) |
| S-TXI-STAND | controle | "PT IBW, informe box de estacionamento." | Art. 125, II |

A forma sem taxiway (S-TXI-OK-SHORT) é a que o M2 usa: os dados do OurAirports não têm taxiways, e há aeródromos que nem têm.

**Prática (VATSIM-BR):**

| ID | Quem | Frase |
|---|---|---|
| P-TXI-REQ | piloto | "Solo Brasília, GOL 1419, pronto para o táxi." |
| P-TXI-OK | controle | "GOL 1419, Solo Brasília, táxi autorizado para o ponto de espera da pista 29 esquerda via taxiways Lima 6, Kilo, Uniform e Zulu. Ajuste de altímetro, 1013." |
| P-TXI-HAND | piloto | "Solo Brasília, GOL 1419, no ponto de espera da pista 29 esquerda." → "GOL 1419, chame a Torre Brasília em 118,1." |

## Readback ("cotejamento")

**O que deve ser cotejado (Art. 12):** autorização da rota; autorizações e instruções para entrar, pousar, decolar, manter-se a certa distância, cruzar, **taxiar** e regressar em qualquer pista; pista em uso, ajuste de altímetro, código SSR, instruções de nível ou altitude, procedimentos, horários, nomes de fixos, matrícula, frequência, instruções de proa e velocidade, níveis de transição.

**Cotejamento incorreto (Art. 12, § 1º):** o controle diz **"negativo"** seguido da versão correta.

**Forma na prática (VATSIM-BR):** o piloto repete a instrução e **termina com o indicativo**: "Autorizado pushback e acionamento, chamará pronto para o táxi, GOL 1419." / "Táxi autorizado para o ponto de espera, pista 29 esquerda, … ajuste de altímetro 1013, GOL 1419."

**Cotejamento correto:** "PT CTA, cotejamento correto, chame pronto para o acionamento." (exemplo do Art. 123, no fluxo do Tráfego).

Palavras úteis (glossário): **CORRETO**, **CONFIRME**, **CIENTE** (recebi tudo; não usar como resposta a instrução que exija cotejamento), **NEGATIVO** (não / não autorizado / isto não está correto), **AFIRMO** (sim, concordo), **REPITA** (repita tudo ou parte da última transmissão), **REPITO**, **CORREÇÃO**, **AGUARDE**.

## Disciplina de rádio (Art. 8 e 9)

O manual manda **evitar** mensagens fora do padrão ("bom dia", "boa viagem", "okay", "um hum") e palavras vazias ("Ok", "ah é"), e evitar palavras que se confundem ("afirmativo" com "negativo"; por isso **AFIRMO**).

- **O controlador do Fonia não diz "bom dia"** nem "ok". (A prática VATSIM-BR cumprimenta no primeiro contato; o MCA não.)
- **O NLU entende o piloto que diz "bom dia", "ok", "afirmativo", "tá bom".**

## Como falar (a camada de fraseologia implementa isto)

**Algarismos (Art. 23, Tabela 2)**, sempre dígito a dígito: 0 **ZERO**, 1 **UNO** (**UMA** antes de palavra feminina), 2 **DOIS** (**DUAS**), 3 **TRÊS**, 4 **QUATRO**, 5 **CINCO**, 6 **MEIA**, 7 **SETE**, 8 **OITO**, 9 **NOVE**. Decimal: **DECIMAL** no lugar da vírgula (118,1 → "UNO UNO OITO DECIMAL UNO"). Milhar redondo: **MIL** ("CINCO MIL"). A distância de 6 NM é "meia dúzia de milhas".

**Alfabeto fonético (Art. 22):** Alfa, Bravo, Charlie, Delta, Echo, Foxtrot, Golf, Hotel, India, Juliett, Kilo, Lima, Mike, November, Oscar, Papa, Quebec, **Romeo**, Sierra, Tango, Uniform, Victor, Whiskey, X-Ray, Yankee, Zulu.

**Pista, altímetro, transponder (Art. 29 e 30):** "PISTA ZERO NOVE", "PISTA DOIS TRÊS ESQUERDA"; QNH 1017 → "UNO ZERO UNO SETE" (1000 hPa é "UNO MIL"); transponder 4321 → "QUATRO TRÊS DOIS UNO".

**Indicativos de aeronave (Art. 36):**

- matrícula: "PT AAP" → "PAPA TANGO ALFA ALFA PAPA";
- empresa + três últimos da matrícula: "GLO GOW" → "GOL GOLF OSCAR WHISKEY";
- empresa + voo: "TAM 3373" → "TAM TRÊS TRÊS SETE TRÊS" (não pode ser abreviado);
- Força Armada: "FAB 2114" → "FORÇA AÉREA DOIS UNO UNO QUATRO".

Depois de estabelecido o contato, a matrícula pode ser abreviada para **pelo menos os três últimos caracteres** ("PT ABC" → "ALFA BRAVO CHARLIE"). O **NLU deve aceitar** a matrícula completa, os três últimos caracteres, soletrados ou lidos como letras, e o indicativo do voo.

## Lacunas e pontos a decidir (`TODO(fraseologia)`)

- Tráfego (autorização de plano) fica para depois do M2.
- **Transferência para a Torre (M4, implementada):** o piloto **relata** o ponto de espera ("no ponto de espera da pista 15"; o "no/em" antes de "ponto" o distingue do cotejamento, que diz "para o ponto de espera") e o Solo responde **"PRABC, chame Torre Curitiba UNO UNO OITO DECIMAL CINCO CINCO."** (Art. 125, II, "chame Torre 118,1", com o nome da Torre como na prática VATSIM-BR, P-TXI-HAND). `TODO(fraseologia)`: o manual não lista a troca de frequência entre o que deve ser cotejado (Art. 12), então o app **não exige** o cotejamento dela; mas o piloto costuma repetir a frequência ("chamando Torre em 118,55, PRABC"), e o Solo **recebe em silêncio**, com ou sem indicativo e sem corrigir como foi dito (achado no teste ao vivo: antes o Solo respondia "Repita." a quem cotejava). Confirmar com o mantenedor.
- Em um aeroporto sem Torre o Solo não tem para onde passar o voo: o app mostra um aviso (`NoTowerAvailable`), não fala.
- Quem responde vem da **frequência do rádio** (COM1): o Solo na dele, a Torre na dela, ninguém em outra (aviso com as frequências do aeroporto). Enquanto a Torre não tem fraseologia no app (M4, próximas tarefas), na frequência dela o app mostra um aviso e não inventa frases.
- AFIS: o MCA tem uma seção própria (Serviço de Informação de Voo de Aeródromo); no M2 o app diz que não há serviço de controle ali.
- Vento e altitude/nível ditos em voz: o Solo do M2 não precisa deles (só pista, frequência e ajuste de altímetro); `SpokenText` ainda não os fala. `TODO(fraseologia)` quando a Torre entrar.
- Convidar a falar (`GoAhead`, quando o piloto só chama o Solo): **"PRABC, prossiga"** (indicativo + "prossiga"), como informou o mantenedor; o MCA não traz frase para o Solo. Falta a fonte oficial, se houver.
- Pedir o indicativo de novo (`RequestCallsign`): o MCA não traz frase para o Solo. **Proposta em uso, a confirmar:** "Estação chamando SOLO Curitiba, repita o indicativo." `TODO(fraseologia)`.
- AFIS (`NoControlService`): sem frase no MCA para dizer que só informa. **Proposta em uso, a confirmar:** "PRABC, RÁDIO Curitiba, sem serviço de controle neste aeródromo, apenas informações de voo." `TODO(fraseologia)`.
- Aeroporto sem serviço e aeroporto sem pista nos dados: ninguém fala; o app mostra um aviso (`UtteranceKind.Notice`), não é fraseologia.
- O controlador responde sem dizer o próprio órgão (como nos exemplos do MCA, "TAM 3702, acionamento aprovado…"); a prática VATSIM-BR diz ("GOL 1419, Solo Brasília, …"). Segue o MCA até o mantenedor decidir outra coisa.
- Ajuste de altímetro no táxi (prática VATSIM-BR) fica de fora até haver fonte de QNH. `TODO(fraseologia)`.
- Reboque de aeronave e movimento de veículos: Anexo III, fora do M2.
- Qual forma o controlador usa quando o manual dá duas ("taxie / autorizado táxi"): usar **"autorizado táxi"** até o mantenedor decidir outra coisa.
- Estilo de fala (MCA × VATSIM-BR): padrão **MCA 2025**; as variantes da prática ficam documentadas aqui e entendidas pelo NLU.
