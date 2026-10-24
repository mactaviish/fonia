# Roadmap

Cada marco termina com testes passando e algo que você consegue demonstrar. Marcos são pequenos de propósito: bom para trabalhar com o Claude Code, uma tarefa de cada vez.

## M0: Fundação

- [x] Repositório (por enquanto privado; licença Apache-2.0), `Fonia.sln`, `Directory.Build.props` (Nullable, analyzers, `EnableWindowsTargeting`)
- [x] Projetos iniciais: `Fonia.Core`, `Fonia.Sim.Mock`, `Fonia.Core.Tests`
- [x] `CLAUDE.md`, `docs/ARCHITECTURE.md`, este roadmap
- [x] Modelos (`record`): `AircraftState`, `Intent`, `AtcDecision`, `FlightContext`
- [x] Interface `ISimAdapter` + `MockSimAdapter` (estado programável)
- [x] CI (GitHub Actions): `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes` em Linux e Windows

**Concluído em 2026-10-12.** CI verde em Linux e Windows, mock com estados programáveis e sequências de teste.

**Pronto quando:** `dotnet test` passa no CI nos dois sistemas e o mock produz estados de teste.

## M1: Ler o MSFS 2020

- [x] Avaliar e escolher a abordagem de SimConnect (SDK oficial ou wrapper): SimConnect.NET, registrado em `docs/decisions/simconnect.md`. A redistribuição da DLL nativa fica como risco aberto para o M4 (instalador)
- [x] `MsfsSimAdapter`: posição, altitudes, velocidade, proa, em solo, COM1/COM2, transponder, matrícula e tipo da aeronave
- [x] Aplicativo de console que imprime o estado em tempo real (`fonia`)
- [x] Gravador de telemetria (salva sessão em arquivo) e replay via `MockSimAdapter`
- [x] Reconexão automática se o simulador for aberto depois do app (e se for fechado e reaberto)

**Concluído em 2026-10-12**, validado pelo mantenedor com o MSFS 2020: o app aberto antes e depois do simulador, o MSFS fechado e reaberto com o app rodando, recarga de voo pelos menus, mudanças de rádio e squawk e encerramento com Ctrl+C.

**Pronto quando:** você abre o MSFS 2020, roda o app e vê os dados corretos mudando; uma sessão gravada pode ser reproduzida sem o simulador.

## M2: ATC por texto (Solo)

- [x] Notas de fraseologia (MCA 100-16, edição 2025) em `docs/phraseology/` e exemplos dos docs corrigidos
- [x] Carregar um aeroporto (OurAirports): pistas e frequências, e `data/atc-names.csv` com o nome falado dos aeroportos controlados (`Fonia.Data`)
- [x] Quem responde, como no aeroporto real: Solo, senão Torre, senão AFIS (só informação), senão nenhum serviço
- [x] NLU por regras, tolerante a frases fora do padrão, para: `RequestStartup`, `RequestPushback`, `RequestTaxi`, `Readback`, `SayAgain`
- [x] Máquina de estados do Solo (`AtcEngine`)
- [x] Templates de fraseologia pt-BR para essas instruções (números, letras e indicativos como o manual manda)
- [x] Console (`fonia --atc <ICAO>`): você digita a fala do piloto, o app responde em texto, usando o estado real (ou mockado) do simulador

**Concluído em 2026-10-13.** No terminal (`fonia --atc <ICAO>`), o ciclo partida → pushback → táxi com readbacks funciona em qualquer aeroporto do Brasil dos dados (5.722), com o órgão que responde como no aeroporto real, e foi exercitado pelo mantenedor digitando frases livres e telegráficas.

**Pronto quando:** no terminal, você faz o ciclo partida → pushback → táxi com readbacks, e os testes cobrem os caminhos felizes e erros comuns (readback errado, pista inexistente).

**Pendências conhecidas do M2** (nenhuma bloqueia o marco):

- Confirmar com o mantenedor duas frases que o MCA não traz e que estão como proposta: pedir o indicativo de novo e o AFIS dizendo que só informa (`TODO(fraseologia)` em `docs/phraseology/solo-pt-br.md`).
- Uma fala com dois pedidos ("quero táxi, pushback") responde só ao primeiro (o pushback); o segundo precisa ser repetido.
- O ajuste de altímetro (QNH) não é dito no táxi: falta fonte de pressão.
- Telefonia de empresas: só FAB, GLO e TAM, os exemplos do manual.

## M3: Voz

- [x] **Spike:** comparar opções de STT e TTS locais em .NET (precisão em pt-BR, latência, tamanho, licença, facilidade de empacotar) e registrar a decisão em `docs/` (`docs/decisions/speech.md`). STT: Whisper.net `small` + prompt; TTS: Kokoro-82M (ONNX) com camada de pronúncia e fonetizador pt-BR próprios
- [x] Captura de áudio + push-to-talk (`Fonia.Audio`): `fonia --mic-test <arquivo.wav>` grava uma fala com a tecla PTT
- [x] `ITranscriber` com a opção escolhida (modelo pequeno, prompt de aviação): `fonia --transcribe <arquivo.wav>`
- [x] Camada de pronúncia e fonetizador pt-BR próprio (`Pronunciation`, `PortuguesePhonemizer`), sem espeak-ng
- [x] `ISynthesizer` com voz pt-BR (Kokoro, voz configurável, padrão aleatória) e efeito de rádio configurável (desligado, leve, **média** por padrão, forte): `fonia --speak "<texto>"`
- [x] Integrar ao pipeline do M2: `fonia --atc <ICAO> --voice` (PTT, reconhecimento, NLU, motor, fraseologia, voz do controlador e alto-falante)
- [x] Download assistido dos modelos de voz (`fonia --download-models`, pergunta ao faltar modelo, SHA-256 fixado, retomada) e `THIRD-PARTY-NOTICES.txt`; tarefa adicionada em 2026-10-14 depois do primeiro teste ao vivo, que bateu no modelo ausente. O instalador do M4 vai usar o mesmo downloader
- [x] Medir e registrar latência por etapa: reconhecimento ~2,1 s (gargalo), voz 0,17 a 0,9 s, rádio e regras menos de 20 ms, total 2,3 a 3,2 s na CPU (`docs/decisions/speech.md`); falta repetir com o simulador aberto e com `--stt-gpu`

**Pronto quando:** você aperta PTT, fala "solicito táxi" e ouve a resposta do Solo, sem digitar nada.

**Validado em 2026-10-14** pelo mantenedor, ao vivo: PTT, reconhecimento, motor, voz e alto-falante funcionando. Falta só registrar os tempos de uma sessão real (item de latência acima).

### Problemas da voz achados no teste ao vivo (resolvidos em 2026-10-14)

Dois achados do primeiro teste ao vivo, resolvidos escolhendo **de ouvido** entre variantes geradas pelo mesmo modelo e fonetizador do app (o mantenedor ouviu, as variantes e as medidas ficaram em `docs/decisions/speech.md`):

1. **"Repita." sai com uma leitura estranha** ("repida"): a frase de 9 fonemas é curta demais para o modelo. Escolhida a variante com o indicativo: o controlador diz **"PAPA ROMEO ALFA BRAVO CHARLIE, repita."** (`PortuguesePhraseology`). O manual traz só "REPITA"; o indicativo na frente é a forma de chamar a aeronave, e as variantes de estilo e de pronúncia não resolveram.
2. **Quase não havia pausa entre "negativo" e "autorizado":** a vírgula dá ~230 ms. Escolhidas as **reticências** (~340 ms): a camada de pronúncia (`Pronunciation.Respell`) fala a vírgula depois de "negativo" como três pontos, **só na voz**, o texto do manual e o da tela não mudam. A lista de palavras que pedem pausa maior (`LongPauseAfter`) é fácil de ampliar.

## M4: Voo completo em um aeroporto

- [x] Posição Torre: alinhar, autorização de decolagem, autorização de pouso. A fraseologia está catalogada em `docs/phraseology/torre-pt-br.md` (MCA 100-16/2025, Art. 126 a 134). **Feito:** alinhar e decolar, com cotejamento exigido (`LineUpAndWait`, `TakeoffClearance`, `SpokenText.Wind`). Fala proativa pela telemetria: "decolado aos MM" (`FlightEventDetector`, `AirborneNotice`). Circuito e pouso: autorização para o circuito, reportes de perna do vento, base e final, autorização de pouso, arremetida (`CircuitClearance`, `LandingClearance`). Depois do pouso: "no solo aos MM, após livrar a pista chame Solo ..." pela telemetria (`TouchedDown`, `LandedNotice`), "pista livre" e "taxie para o pátio". **Validado em voo (2026-10-18):** o voo completo (acionamento, táxi, alinhar e decolar, circuito, pouso e volta ao pátio) correu como esperado, com um único "repita". **Fica para depois, fora do M4:** o "chame o Controle ..." depois da decolagem e a aproximação direta (M5, Aproximação / Centro), e o mapa do aeródromo, com taxiways e travessia de pista (M6).
- [x] Transferência de frequência entre posições (Solo → Torre) conforme o COM1 do simulador: quem responde vem da frequência do rádio (`FrequencyResolver`, `--com1`, `/com1`, COM1 ao vivo com `--sim`); o relato "no ponto de espera" faz o Solo mandar chamar a Torre.
- [x] ATIS com METAR real (cache + fallback). **Feito:** METAR real (`aviationweather.gov`, cache de 30 min, guardado por até 3 h sem rede, `--no-metar`), com vento e ajuste de altímetro nas falas. Informações de partida / condições do aeródromo (Art. 121 e 122): pista, vento, ajuste de altímetro, temperatura, visibilidade e hora certa. O **texto do ATIS** (ICA 100-37, Art. 768 a 777; `docs/phraseology/atis-pt-br.md`) a partir do METAR: nuvens, tempo presente, CAVOK, designador pela hora. Na frequência de ATIS do aeroporto (COM1) o texto toca em laço contínuo (que segue mesmo sem ninguém sintonizado), com voz e efeito de rádio próprios (`AtisBroadcast`), e corta ao sintonizar outra; o piloto que diz "com informação Mike" ouve o QNH e, se a letra estiver velha, a letra em vigor (`ReportAtis`, `AtisReceived`). **Fica para depois:** o vento do simulador como fallback, o ATIS em inglês, chegada e saída separados, e os itens sem dado (aproximações esperadas, nível de transição, condições de pista)
- [x] Seleção de pista em uso pelo vento (`AirportContext.WithWeather`; `--runway` manda)
- [x] Configuração em `appsettings.json` (`%APPDATA%\Fonia`, `--config`, `--init-config`, variáveis `FONIA_*`) e abertura sem argumentos que pergunta o aeroporto e conversa por voz (ver ARCHITECTURE 8b). Falta a primeira versão utilizável por outras pessoas: publicação, instalador e documentação de instalação, abaixo
- [x] Publicação (`tools/publish.ps1`: self-contained win-x64, 156 MB / zip de 61 MB, sem as bibliotecas de outros sistemas), instalador Inno Setup (`installer/fonia.iss`, sem administrador, oferece baixar os modelos) e workflow de release com tag (`release.yml`, rascunho com SHA-256). **O instalador não foi compilado fora do CI** (o Inno Setup não está nesta máquina), e a decisão sobre o `SimConnect.dll` no pacote segue aberta (`docs/decisions/simconnect.md`)
- [x] Documentação de instalação (`docs/INSTALL.md`)

**Pronto quando:** você faz um voo local completo (partida → táxi → decolagem → circuito → pouso → táxi) falando com o app.

**Concluído em 2026-10-18:** o voo completo foi validado pelo mantenedor no MSFS (Solo, Torre, circuito e pouso) e o ATIS foi ouvido e aprovado por ele (português e inglês, voz fixa). Fica para outros marcos: o "chame o Controle ..." e a aproximação direta (M5) e as rotas de táxi pelo mapa do aeródromo (M6).

## M5: Expansão

- [x] Aproximação / Centro e handoffs (feito em 2026-10-18; o que a Torre diz depois da decolagem em aeroporto sem `APP` nos dados segue em aberto, `TODO(fraseologia)`). Resolve o "chame o Controle ..." depois da decolagem (Art. 127; hoje a Torre diz só "decolado aos MM") e a aproximação direta do Art. 129 ("autorizada aproximação direta ... reporte final"), que ficaram do M4. Precisa das frequências de Controle e Aproximação, que os dados de aeroportos quase não têm (pesquisa em [`decisions/approach-frequencies.md`](decisions/approach-frequencies.md), feita em 2026-10-18). Tarefas, uma por PR: (1) pesquisa [x]; (2) `ServiceUnit.Approach` e leitura dos tipos `APP*` [x]; (3) handoff depois da decolagem [x]; (4) NLU da aproximação direta [x]; (5) motor e fraseologia da aproximação direta [x]
- [ ] Tráfego de IA no rádio (outras aeronaves falando)
- [ ] Suporte ao MSFS 2024 (validar `MsfsSimAdapter`)
- [ ] `XPlaneSimAdapter` (X-Plane 11/12)
- [ ] Inglês (FAA/ICAO)
- [ ] NLU com LLM opcional para fala fora do padrão
- [ ] Integração com SimBrief (plano de voo)
- [ ] Interface gráfica (WPF, WinUI 3 ou Avalonia)
- [ ] **Casos não entendidos, para melhorar o app.** Quando o controlador diz "repita", "negativo" ou pede o indicativo, ou o usuário marca "isso foi errado" (o app entendeu mal sem saber), o caso é guardado: o texto que o reconhecedor escreveu, o contexto (fase, última fala do controlador, frequência), o que o NLU entendeu e o que o motor decidiu, e, só com o consentimento do usuário, o áudio. Ideia do mantenedor em 2026-10-15, vinda do teste de voo que levou o acerto dos cotejamentos de 89% para 97% (`docs/decisions/speech.md`): os usuários não serão os desenvolvedores, então os casos precisam chegar até nós.
  - **Etapa 1, local (feita em 2026-10-15):** `%LOCALAPPDATA%\Fonia\cases`, um `.jsonl` por sessão e, com `--save-audio`, os WAVs ao lado, 30 dias e 200 MB; a tecla `ScrollLock` (ou `/errado`) marca o que o app entendeu mal; `fonia --cases` lista (ver ARCHITECTURE 14). **Falta** o spike (`tools/speech-spike.cs`) ler a pasta e medir o efeito de uma mudança sobre os casos reais. Fora do git.
  - **Etapa 2, envio:** opt-in explícito (desligado por padrão), com o usuário vendo o que será enviado antes, um identificador aleatório por instalação (sem nome nem e-mail) e a opção de apagar. A **voz é dado pessoal** (LGPD): finalidade e consentimento claros no primeiro uso, texto e contexto separados do áudio (o texto vale sozinho). **Destino a decidir:** repositório de dados privado, issue automática (só texto) ou um endpoint próprio; áudio não vai para um repositório público.
  - **Etapa 3, retorno:** os casos revisados viram frases de teste e entram no conjunto de avaliação (`phrases-voo.txt`).

## M6: Solo realista (o mapa do aeródromo)

Ideia do mantenedor em 2026-10-15, vinda do voo de teste em SBCT: o Solo manda "táxi para o ponto de espera da pista 15" sem dizer a taxiway, e a rota de Curitiba atravessa outra pista, o que exige autorização. **Decisão** (`docs/decisions/airport-layout.md`): o mapa vem do **próprio simulador** (SimConnect, API de instalações), guardado só no computador do usuário; **sem o mapa o app funciona como hoje**, dizendo a pista correta. Cada etapa que toca o simulador precisa de teste do mantenedor no MSFS (não dá para rodar aqui); os testes automáticos usam layouts sintéticos.

- [x] **L0a. Spike do mapa, feito em 2026-10-18** (resultados e o que mudou para o L0b em `docs/decisions/airport-layout.md`): `fonia --dump-layout <ICAO>`, rodado no MSFS 2020 em SBCT, SBGR, SBGO, SBPS, SBIH e SBSG. **L0b, feito em 2026-10-18:** o dump é convertido para o `AirportLayout` (`AirportLayoutConverter`; taxiways de tipo 4, ponto de espera ligado à pista por geometria), o comando grava também o mapa em `%LOCALAPPDATA%\Fonia\layouts\<ICAO>.json` e o `TaxiRouter` foi ajustado (ponto de espera por ligação, tolerância a trechos sem nome); o teste de rotas mostra 86 de 99 em SBGR e 0 em SBPS. O **cache usado pelo app** (carregar o mapa ao abrir) é do L2.
  - Plano original: `fonia --dump-layout <ICAO>` lê do simulador as pistas (cabeceiras), taxiways (com nome), pontos de espera estacionamentos **e as frequências de rádio do aeroporto** (filho `FREQUENCY` da entrada `AIRPORT`: ATIS, GROUND, TOWER, CLEARANCE, APPROACH, DEPARTURE, CENTER...) e grava um JSON em `%LOCALAPPDATA%\Fonia\layouts`. Responde: o que o SimConnect.NET expõe das instalações, o que muda no MSFS 2024, quanto custa pedir um aeroporto (SBCT, SBGR), o que vem de cenário de terceiros sem taxiway, como achar os pontos de espera. **Pronto quando** você roda em SBCT e o JSON tem as taxiways e as duas pistas com nome e posição. Também responde se as frequências do simulador para o Brasil prestam: comparar com o OurAirports em SBCT e em aeroportos com dado duvidoso (SBPS, SBIH, SBGO) antes de decidir a mesclagem.
- [x] **L1. Modelo e leitura** (Core, feito em 2026-10-18): `AirportLayout` (pistas com cabeceiras, grafo de táxi com nome, estacionamentos e as frequências), `AirportLayoutJson` (versão 1; leitura e gravação recusam grafo quebrado com `AirportLayoutFormatException`), layout sintético em `TestLayouts`. **O cache por aeroporto (arquivo) ficou para o L2**, onde passa a ser usado (como o `CachedMetarSource`, em `Fonia.Data`).
- [x] **L2. Fonte no simulador** (feito em 2026-10-18; ver `docs/decisions/airport-layout.md`): `IAirportLayoutSource` (Core), `MsfsAirportLayoutSource` (`Fonia.Sim.Msfs`), `CachedAirportLayoutSource` (`Fonia.Data`: o simulador primeiro, o arquivo `<ICAO>.json` como reserva), `AirportFrequencyMerge`, `--no-map`. O app pede o mapa ao abrir e ao seguir um voo novo; sem simulador usa o arquivo guardado, e sem nenhum dos dois funciona como sempre. O texto original da etapa: (`Fonia.Sim.Msfs`, `IAirportLayoutSource`): o adaptador do MSFS pede o layout do aeroporto, com reconexão e tempo limite; o mock devolve layouts de teste; o app pede ao abrir (e ao seguir um voo novo); traduz os tipos de frequência do simulador para os nossos (`DEPARTURE` e `APPROACH` viram Controle) e aplica a **regra de mesclagem** com as do OurAirports, que só se escreve depois do L0. Sem layout, nada muda.
- [x] **L3. Rota** (`TaxiRouter`, Core, lógica pura, feito em 2026-10-18; ver `docs/decisions/airport-layout.md`): menor caminho da posição da aeronave ao ponto de espera da pista em uso, com a lista de taxiways, as **travessias de pista** e os pontos de espera. **Pronto quando**, no layout sintético, a rota até a 15 passa pela taxiway e acusa a travessia da 11/29; e uma rota que não se acha (grafo cortado, nomes faltando) devolve "sem rota", nunca uma rota inventada.
- [ ] **L4. Fraseologia e estados do táxi** (L4a feita em 2026-10-18: "via taxiway ..." quando a rota não atravessa pista; falta a L4b, a travessia): "via taxiway A, B", "mantenha posição fora da pista 11", o relato no ponto de espera da travessia (distinto do ponto de espera da decolagem), a **autorização de cruzar** (a frase exata ainda não tem referência: `TODO(fraseologia)`, preciso do mantenedor) e o "pista livre" depois de cruzar; cotejamento do "mantenha posição". Sem rota, a frase de hoje.
  - **L4b em três tarefas (plano de 2026-10-18).** **L4b-1 (feita):** pista de pouso e pista de decolagem separadas (`AirportContext.ArrivalRunway`, `RunwayUseRule`, `data/runway-use.csv`, `--arrival-runway`; hoje só Guarulhos). **L4b-2 (feita em 2026-10-21):** a Torre manda cruzar quando a aeronave livra a pista de pouso (pela telemetria, `RunwayVacatedDetector`, ou pelo "pista livre" do piloto), se pelo menos 80% das rotas até os estacionamentos atravessam uma mesma pista (`TaxiRouter.CrossingToApron`): "cruze a pista X, após chame Solo em ...", com cotejamento (`CrossRunway`). Sem fase nova: a travessia fica na fase `Landed` e é dita uma vez. **L4b-3 (feita em 2026-10-21):** na saída, o Solo leva até o ponto de espera (como sempre) e passa à Torre; se a pista de decolagem fica atrás de outra pista e a aeronave está parada no ponto de espera dela (a até 75 m do nó), a Torre manda a travessia junto com o que vem depois (frases do mantenedor): sem ninguém na frente na fila, "cruze a pista X, alinhe e mantenha pista Y"; com alguém na frente, "cruze a pista X e mantenha antes da barra de parada". O cotejamento exige as duas coisas. O app não tem tráfego próprio: `AirportContext.AircraftAhead` é definido à mão (`/fila sim|nao`). Sem mapa ou sem posição, nada muda.
- [ ] **L5. Acompanhar pela telemetria**: onde a aeronave está na rota, quando chegou ao ponto de espera, quando saiu da pista, e a confirmação do que o piloto relata (como a decolagem e o pouso). Se a aeronave sai da rota, o controlador reorienta ou volta ao comportamento de hoje.
- [ ] **L6. Depois do pouso**: "taxie para o pátio 1 via taxiway D" (Art. 134) com a rota e o estacionamento do mapa, no lugar do "taxie para o pátio" de hoje; "pista livre" detectado pela posição.
- [ ] **L7. Depois**: partida de interseção, mais de um ponto de espera por pista, pistas paralelas (15L e 15R), sentido do táxi de volta, outros simuladores (X-Plane) e outras fontes (OSM/ODbL, só com o aval do mantenedor).
- [x] **L8. Praticar a rota sem o simulador** (feita em 2026-10-21): no modo de texto (`--atc`), `/em vaga 12`, `/em espera 15` e `/em pista 15` põem a aeronave parada, no solo, num ponto do mapa guardado (`PracticePlaces`), para treinar a rota por taxiway e a travessia sem o MSFS (por exemplo `/em espera 10R`, `/evento pousou` e `/evento livrou`). Com o simulador a posição vem dele e `/em` não faz nada; sem mapa guardado, avisa. Requisito geral em `CLAUDE.md` (princípio 3): o app funciona sem o simulador.
- [x] **L9. A Torre toma a iniciativa** (feita em 2026-10-21, decidida com o mantenedor): o piloto não precisa dizer "pronto"; com o mapa e a posição, `RunwayApproachDetector` (a menos de 100 m de um ponto de espera ou 150 m do eixo da pista de decolagem; repete a cada 5 s) gera `NearRunway` e o motor dá, se puder, a próxima instrução: o Solo passa à Torre, a Torre autoriza a travessia ou o alinhamento, e depois do alinhamento lido de volta a decolagem, mesmo sem a aeronave alinhada e parada. Com a pista livre, o "alinhe e mantenha" é pulado e a decolagem é autorizada direto; com a pista ocupada (`RunwayBusy`, `/fila pista`), alinha primeiro. Com alguém na frente (`AirportContext.AircraftAhead`, `/fila sim|nao`) fica em silêncio e fala sozinha ao liberar (`TrafficCleared`); se o piloto chamar, "aguarde" (`Standby`). Sem simulador: `/evento chegou`, e `/em espera 15` já gera o evento.

**Pronto quando:** em Curitiba, o piloto pede táxi do pátio e ouve a rota completa, para no ponto de espera antes da pista que a rota atravessa, pede e recebe a autorização de cruzar, segue até a cabeceira da 15, e tudo isso funciona também sem o mapa (só com a pista correta).

## M7: Espaço aéreo (setores, TMA e CTR)

Fora do MVP, mas necessário para a aplicação: hoje o app só sabe de um aeroporto por vez, com as frequências dele, e não sabe em que espaço aéreo o voo está. A fonte é o **AIXM** da AISWEB, que tem as unidades de controle, os serviços por setor, os canais de rádio com frequência e os espaços aéreos com polígono; como ler o pacote e onde ele trava está em [`decisions/approach-frequencies.md`](decisions/approach-frequencies.md) §4b (pesquisa de 2026-10-24). Licença e atribuição são as de `data/aisweb/` (`CLAUDE.md`, princípio 6, e `NOTICE`). Cada tarefa, uma por PR; sem simulador o app usa os dados embutidos, como hoje.

- [ ] **A1. Dados: script do AIXM** (`tools/update-aisweb-aixm.cs`): baixa a emenda em vigor (`pub&type=AIXM`, zip de 56 MB, XML de 334 MB lido em fluxo) e grava em `data/aisweb/` CSVs pequenos: unidades e serviços (APP, ACC, TWR) com o setor, o nome falado e as frequências (sem o 121,5), e os espaços aéreos (TMA, CTR, ATZ...) com o polígono simplificado e os limites verticais. **Pronto quando** o CSV de SBCT diz "Controle Curitiba" com as frequências e o polígono da TMA, e o arquivo cabe em poucas centenas de KB (senão, só a região de interesse).
- [ ] **A2. Modelo e consulta** (Core, lógica pura): `Airspace` e `ControlUnit` como `record`s; dado uma posição e uma altitude, "em que CTR/TMA/setor estou" (ponto em polígono, esfera); dado um aeroporto, qual APP o atende (pelo `clientAirport` e, quando falta, pelo polígono). Testes com polígonos sintéticos. **Pronto quando** a consulta devolve o mesmo APP que os dados de hoje para os aeroportos que o OurAirports já tem, e "nenhum" onde não há dado, nunca um palpite.
- [ ] **A3. Setores do APP**: escolher a frequência do setor pela posição/altitude/rumo quando o APP tem vários; sem saber, o app não cita um setor (como hoje). Depende de como o DECEA descreve o setor (nome e polígono); pode ser que só a lista de frequências exista, e então fica na regra atual.
- [ ] **A4. Entrada e saída do espaço aéreo**: detectar pela telemetria a entrada na CTR/TMA e a saída; o controlador passa o voo ao órgão seguinte ("chame o Controle ...", "chame o Centro ...", Art. 127) e autoriza entrar na CTR quando o espaço exige. A fraseologia é `TODO(fraseologia)` até haver referência no MCA 100-16; perguntar ao mantenedor antes.
- [ ] **A5. Centro (ACC)**: o handoff do APP para o Centro e do Centro para o APP do destino, que hoje o app não faz (item em aberto do M5).
- [ ] **A6. Atualização**: a emenda muda a cada ciclo AIRAC (28 dias); a rotina de atualização do CSV vai junto com a dos demais de `data/aisweb/`.

## Backlog / ideias

- Sotaques e vozes diferentes por posição e aeroporto
- Voz de Natal: a `pm_santa` (hoje fora do `VoiceCatalog` porque não agradou no rádio) como voz especial de temporada, sorteada só em dezembro (data via `TimeProvider`) ou escolhida à mão com `--tts-voice`. Ouvir de novo com as pronúncias e o efeito de rádio antes de liberar
- Efeito de rádio realista (ruído, queda de sinal por distância)
- Emergências (MAYDAY/PAN-PAN)
- Integração com addons de aeroportos
- Atualização automática do app

## Nome do projeto

Nome escolhido: **Fonia** (Fonia.IA); namespace `Fonia`. Antes de publicar:

- [x] Escolher o nome definitivo
- [ ] Verificar disponibilidade (GitHub, NuGet, domínio, marcas existentes na área de simulação de voo)
