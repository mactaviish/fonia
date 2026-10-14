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
- O piloto que avisa "no ponto de espera da 33" ainda recebe "Repita.": a transferência Solo → Torre é uma tarefa própria.
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

### Problemas conhecidos da voz (a analisar depois do M3)

Achados no primeiro teste ao vivo; nenhum atrapalha o uso, mas o controlador soa pior do que precisa.

1. **"Repita." sai com uma leitura estranha** (o mantenedor ouviu "repida"). Frase muito curta: `xˌepˈitæ.`, 8 fonemas.
   - *Suspeita:* o Kokoro escolhe o vetor de estilo da voz pela **quantidade de fonemas** (a linha `n-1` da tabela de 510); para textos muito curtos essa linha tem pouco treino e a prosódia fica instável. Também pode ser o `t` entre vogais, que o modelo sonoriza.
   - *A testar:* (a) gerar "Repita." usando a linha de estilo de uma frase mais longa (por exemplo a de ~30 fonemas); (b) deixar um respiro antes (`... Repita.`) ou acrescentar contexto, como "PAPA ROMEO ALFA BRAVO CHARLIE, repita."; (c) conferir se a grafia de pronúncia (`repita` para `repíta`, `repítta`) muda o resultado; (d) comparar com a saída do espeak-ng só como régua (o fonetizador é nosso, a régua não vai para o repositório).
2. **Quase não há pausa entre "negativo" e "autorizado"** em "PAPA ROMEO ALFA BRAVO CHARLIE, negativo, autorizado táxi...". A vírgula está nos fonemas (token 3), mas o modelo a encolhe.
   - *A testar:* (a) falar a correção em **duas frases** ("PAPA ROMEO ALFA BRAVO CHARLIE, negativo." e depois "autorizado táxi...") com ~250 ms de silêncio entre elas, o que o `PhonemeChunker` e o `KokoroSynthesizer` já sabem fazer para textos longos (cuidado: cada pedaço curto cai no problema 1); (b) trocar a vírgula por ponto, ou por reticências; (c) pausa maior depois do indicativo e depois de "negativo" definida na fraseologia (uma `Utterance` com partes), sem mudar o texto mostrado.
   - Qualquer solução precisa valer para o resto da fraseologia (a frase inteira é uma só `Utterance`), então a decisão é de design: **pausas** ficam na camada de pronúncia/voz, não no texto do manual.


## M4: Voo completo em um aeroporto

- [ ] Posição Torre: alinhar, autorização de decolagem, autorização de pouso
- [x] Transferência de frequência entre posições (Solo → Torre) conforme o COM1 do simulador: quem responde vem da frequência do rádio (`FrequencyResolver`, `--com1`, `/com1`, COM1 ao vivo com `--sim`); o relato "no ponto de espera" faz o Solo mandar chamar a Torre. A Torre ainda não fala (avisos), vem nas próximas tarefas
- [ ] ATIS com METAR real (cache + fallback)
- [ ] Seleção de pista em uso pelo vento
- [ ] Configuração em `appsettings.json`; primeira versão utilizável por outras pessoas
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
