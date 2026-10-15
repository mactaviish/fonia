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

- [ ] Posição Torre: alinhar, autorização de decolagem, autorização de pouso. A fraseologia está catalogada em `docs/phraseology/torre-pt-br.md` (MCA 100-16/2025, Art. 126 a 134). **Feito:** alinhar e decolar, com cotejamento exigido (`LineUpAndWait`, `TakeoffClearance`, `SpokenText.Wind`). Fala proativa pela telemetria: "decolado aos MM" (`FlightEventDetector`, `AirborneNotice`). Circuito e pouso: autorização para o circuito, reportes de perna do vento, base e final, autorização de pouso, arremetida (`CircuitClearance`, `LandingClearance`). Depois do pouso: "no solo aos MM, após livrar a pista chame Solo ..." pela telemetria (`TouchedDown`, `LandedNotice`), "pista livre" e "taxie para o pátio". **Falta:** "chame o Controle ..." (sem dados de Controle), pátio e taxiway nos dados, aproximação direta
- [x] Transferência de frequência entre posições (Solo → Torre) conforme o COM1 do simulador: quem responde vem da frequência do rádio (`FrequencyResolver`, `--com1`, `/com1`, COM1 ao vivo com `--sim`); o relato "no ponto de espera" faz o Solo mandar chamar a Torre. A Torre ainda não fala (avisos), vem nas próximas tarefas
- [ ] ATIS com METAR real (cache + fallback). **Feito:** METAR real (`aviationweather.gov`, cache de 30 min, guardado por até 3 h sem rede, `--no-metar`), com vento e ajuste de altímetro nas falas. Informações de partida / condições do aeródromo (Art. 121 e 122): pista, vento, ajuste de altímetro, temperatura, visibilidade e hora certa. **Falta:** o texto falado do ATIS (precisa de fonte da fraseologia), o vento do simulador como fallback
- [x] Seleção de pista em uso pelo vento (`AirportContext.WithWeather`; `--runway` manda)
- [x] Configuração em `appsettings.json` (`%APPDATA%\Fonia`, `--config`, `--init-config`, variáveis `FONIA_*`) e abertura sem argumentos que pergunta o aeroporto e conversa por voz (ver ARCHITECTURE 8b). Falta a primeira versão utilizável por outras pessoas: publicação, instalador e documentação de instalação, abaixo
- [ ] Publicação (`dotnet publish` self-contained) e primeiro instalador Windows
- [ ] Documentação de instalação

**Pronto quando:** você faz um voo local completo (partida → táxi → decolagem → circuito → pouso → táxi) falando com o app.

## M5: Expansão

- [ ] Aproximação / Centro e handoffs
- [ ] Tráfego de IA no rádio (outras aeronaves falando)
- [ ] Suporte ao MSFS 2024 (validar `MsfsSimAdapter`)
- [ ] `XPlaneSimAdapter` (X-Plane 11/12)
- [ ] Inglês (FAA/ICAO)
- [ ] NLU com LLM opcional para fala fora do padrão
- [ ] Integração com SimBrief (plano de voo)
- [ ] Interface gráfica (WPF, WinUI 3 ou Avalonia)
- [ ] **Casos não entendidos, para melhorar o app.** Quando o controlador diz "repita", "negativo" ou pede o indicativo, ou o usuário marca "isso foi errado" (o app entendeu mal sem saber), o caso é guardado: o texto que o reconhecedor escreveu, o contexto (fase, última fala do controlador, frequência), o que o NLU entendeu e o que o motor decidiu, e, só com o consentimento do usuário, o áudio. Ideia do mantenedor em 2026-10-15, vinda do teste de voo que levou o acerto dos cotejamentos de 89% para 97% (`docs/decisions/speech.md`): os usuários não serão os desenvolvedores, então os casos precisam chegar até nós.
  - **Etapa 1, local:** `%LOCALAPPDATA%\Fonia\casos`, um `.jsonl` por sessão e os WAVs ao lado, com limite de tamanho e de idade; comando para marcar um erro; o spike (`tools/speech-spike.cs`) lê a pasta e mede o efeito de uma mudança sobre os casos reais. Fora do git.
  - **Etapa 2, envio:** opt-in explícito (desligado por padrão), com o usuário vendo o que será enviado antes, um identificador aleatório por instalação (sem nome nem e-mail) e a opção de apagar. A **voz é dado pessoal** (LGPD): finalidade e consentimento claros no primeiro uso, texto e contexto separados do áudio (o texto vale sozinho). **Destino a decidir:** repositório de dados privado, issue automática (só texto) ou um endpoint próprio; áudio não vai para um repositório público.
  - **Etapa 3, retorno:** os casos revisados viram frases de teste e entram no conjunto de avaliação (`phrases-voo.txt`).

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
