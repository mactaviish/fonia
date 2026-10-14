# Arquitetura

## Visão geral

O app é um pipeline de voz com um núcleo determinístico de ATC. A telemetria do simulador alimenta o contexto; a fala do piloto é transformada em intenção; o motor de ATC escolhe a resposta; a resposta é montada por templates e falada por TTS.

```
┌────────────┐   ┌─────┐   ┌─────┐   ┌────────────┐   ┌─────────────┐   ┌─────┐
│ Mic (PTT)  │ → │ STT │ → │ NLU │ → │ Motor ATC  │ → │ Fraseologia │ → │ TTS │ → alto-falante
└────────────┘   └─────┘   └─────┘   └─────┬──────┘   └─────────────┘   └─────┘
                                           │
                                  ┌────────┴────────┐
                                  │   ISimAdapter   │  (MSFS / X-Plane / Mock)
                                  └─────────────────┘
```

## Projetos da solução

| Projeto | Responsabilidade | Alvo |
|---|---|---|
| `Fonia.Core` | Modelos, interfaces, NLU por regras, motor ATC, fraseologia. Sem dependência de SO ou simulador. | `net10.0` |
| `Fonia.Data` | Aeroportos (OurAirports), METAR/TAF, cache. | `net10.0` |
| `Fonia.Sim.Mock` | `MockSimAdapter`, gravação e replay de telemetria. | `net10.0` |
| `Fonia.Sim.Msfs` | `MsfsSimAdapter` (SimConnect). Compila em qualquer SO; só executa com o MSFS no Windows. | `net10.0` |
| `Fonia.Audio` | Microfone (NAudio/waveIn), push-to-talk por tecla, playback. Compila em qualquer SO; só executa no Windows (mesma ideia do `Fonia.Sim.Msfs`). | `net10.0` |
| `Fonia.Speech` | Implementações de STT (Whisper.net) e de TTS (Kokoro em ONNX Runtime). Compila em qualquer SO. | `net10.0` |
| `Fonia.App` | Executável e composição de dependências (console primeiro, UI depois). | `net10.0` (reavaliar quando houver UI) |

Dependências sempre apontam para o Core. O Core não referencia nenhum outro projeto.

## Componentes

### 1. ISimAdapter (`Fonia.Core` + `Fonia.Sim.*`)

Interface única entre o app e qualquer simulador.

```csharp
public interface ISimAdapter : IAsyncDisposable
{
    event EventHandler<SimConnectionStateChangedEventArgs>? ConnectionStateChanged;
    SimConnectionState ConnectionState { get; }     // Disconnected / Connecting / Connected
    AircraftState? LatestState { get; }             // null até o 1º snapshot após conectar
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    IAsyncEnumerable<AircraftState> StreamAsync(CancellationToken ct);
}

// Capacidade opcional: o app testa `adapter is IComRadioControl`.
public interface IComRadioControl
{
    Task SetComFrequencyAsync(ComRadio radio, Frequency frequency, CancellationToken ct);
}
```

Contrato (documentado no XML do código e verificado pelos testes do `MockSimAdapter`):

- **Queda de conexão não é desconexão.** Depois de `ConnectAsync`, o adapter é dono da conexão: se o simulador fechar ou reiniciar, o estado vai de `Connected` para `Connecting`, o adapter tenta de novo **sem limite** (o MSFS pode levar minutos para abrir) e, quando o simulador volta, passa a `Connected`. Os streams **continuam abertos** o tempo todo; `LatestState` mantém o último snapshot. Só `DisconnectAsync`, `DisposeAsync` ou o cancelamento do próprio token do stream encerram os streams. O par de estados do evento diz o que houve: `Connected → Connecting` é queda; `Connected → Disconnected` é desconexão deliberada.
- `ConnectAsync` completa quando a primeira conexão é estabelecida, tenta até conseguir e lança `OperationCanceledException` se o token for cancelado antes. É no-op se já está conectado ou reconectando.
- `ConnectAsync`, `DisconnectAsync` e `DisposeAsync` são idempotentes; `DisposeAsync` implica desconectar, inclusive durante uma reconexão.
- `ConnectionStateChanged` é disparado uma vez por mudança, em ordem, e **fora de qualquer lock interno** (o handler pode chamar o adapter de outra thread sem deadlock). Uma conexão nova passa por `Connecting` antes de `Connected`.
- Cada chamada a `StreamAsync` cria uma assinatura independente, então vários consumidores não tiram itens uns dos outros. Um consumidor lento pode perder snapshots intermediários (só o mais recente importa), mas nunca trava o adapter nem faz a memória crescer. O stream termina sem exceção.
- Adapters só publicam snapshots quando o simulador reporta dados válidos (ex.: voo carregado); quem consome nunca vê os valores provisórios que alguns simuladores informam durante o carregamento.
- Escrever no simulador (ex.: sintonizar o COM) é uma capacidade separada (`IComRadioControl`), em vez de um método "opcional" na interface principal.

`AircraftState` (`record`) contém: timestamp, latitude, longitude, altitude (MSL e AGL, em pés), velocidade no solo (nós), proa magnética, em solo (bool), COM1/COM2 (`RadioState?`: ativo e standby; `null` quando a aeronave não tem o rádio ou ele não informa uma frequência válida), transponder, matrícula e tipo de aeronave. As unidades fazem parte dos nomes das propriedades. Frequências são `Frequency`, um `readonly record struct` em kHz inteiros (118,275 MHz = 118275), para evitar comparação de `double` e representar canais de 8,33 kHz.

Implementações:

| Adapter | Status | Observações |
|---|---|---|
| `MockSimAdapter` | M0 | Estado programável (`SetState`/`Update`), sequências em memória (`PlayAsync`, com `TimeProvider` injetável), replay de gravações (`ReplayAsync`) e simulação de queda e volta da conexão (`SimulateConnectionLoss`/`RestoreConnection`). Gravação em arquivo (`TelemetryRecorder`) e replay (`ReplayAsync`) incluídos. Base de todos os testes. |
| `MsfsSimAdapter` | M1 | SimConnect. Mesmo adapter para MSFS 2020 e 2024, a validar. Só Windows. |
| `XPlaneSimAdapter` | futuro | UDP/datarefs. |

#### Do SimConnect ao `AircraftState` (`Fonia.Sim.Msfs`)

O adapter do MSFS lê cinco grupos de variáveis, cada um no seu ritmo (1 Hz), e o `MsfsSnapshotAssembler` junta tudo num `AircraftState`. As conversões ficam em `MsfsConversions`, todas com testes:

| Dado | Conversão |
|---|---|
| COM | lido em **Hz** e convertido para kHz inteiro; frequência zero, negativa ou absurda vira `null` (sem standby válido, usa a ativa) |
| Transponder | unidade `bco16`: `0x1234` é o squawk 1234; dígito acima de 7 ou valor fora de 4 dígitos vira 0 |
| Tipo da aeronave | `ATC MODEL` (`C25C`), ou o trecho de uma chave de tradução (`ATCCOM.AC_MODEL_A320.0.text` vira `A320`), ou o título se o modelo vier vazio |
| Timestamp | relógio injetado (`TimeProvider`), não o do simulador |

**Quando publicar.** Um snapshot só sai se a simulação está rodando (evento de sistema `Sim`), todos os grupos chegaram desde a última mudança do `Sim`, e a posição não é o valor provisório que o MSFS informa enquanto o voo carrega (a menos de 0,05° de 0°, 0°; ele continua provisório por ~10 s depois de o `Sim` indicar que a simulação rodou). O movimento é o "batimento": todo movimento válido é publicado; a mudança de qualquer outro grupo (frequência, squawk, matrícula) é publicada na hora; repetições sem mudança não geram snapshot. Cada mudança do `Sim` (menu, recarga do voo) descarta os grupos recebidos, para nunca misturar valores de um voo com os de outro.

#### Conexão com o MSFS (`MsfsSimAdapter`)

Depois de `ConnectAsync`, uma tarefa de fundo (o supervisor) é dona da conexão e segue o algoritmo validado no spike (`docs/decisions/simconnect.md`):

1. **Conectar sem desistir.** Cria uma sessão (`ISimConnectSession`); se o MSFS não está lá, espera **2 s, depois 4, 8 e 10 s** (teto) e tenta de novo, indefinidamente. A espera volta a 2 s depois de uma conexão bem-sucedida.
2. **Esperar o MSFS ficar pronto** (mensagem `Open`) e só então assinar o evento `Sim` e os grupos de variáveis.
3. **Monitorar:** a cada 3 s uma sonda. Só `Dead` (`E_FAIL`) conta, e **duas seguidas** reconstroem a conexão; `NotResponding` (menu, carregamento) nunca derruba, e qualquer resposta que não seja `Dead` zera a contagem. Uma falha reportada pela própria sessão (`OnFault`) reconstrói na hora.
4. **Perda:** descarta a sessão (sem deixar o `Dispose` travar), zera o montador de snapshots, estado `Connecting`, nova sessão. Os streams ficam abertos e o `LatestState` é mantido. Dados que uma sessão descartada ainda entregue são ignorados.
5. `DisconnectAsync`/`DisposeAsync` param o supervisor, encerram os streams e levam a `Disconnected`.

Os tempos são `MsfsAdapterOptions` (padrões acima). Streams e `LatestState` vêm do `AircraftStateBroadcaster` do Core, o mesmo do mock. A parte que fala com a biblioteca é a `SimConnectNetSession`; o resto é testado com uma sessão falsa e um relógio injetável, sem MSFS. Testes ao vivo (`FONIA_LIVE_MSFS=1`) cobrem a sessão e o adapter contra um MSFS real. Ainda não implementa `IComRadioControl` (sintonizar o rádio pelo app chega com a transferência de frequência, no M4).

#### Aplicativo de console (`Fonia.App`)

`fonia` mostra o estado da aeronave em tempo real, vindo de qualquer `ISimAdapter`, e é a forma de ver o M1 funcionando:

```
fonia                                   # lê o MSFS 2020 (reconecta sozinho)
fonia --sim mock                        # um taxi simulado, sem simulador
fonia --record sessao.jsonl             # mostra e grava a sessão
fonia --replay sessao.jsonl             # reproduz uma gravação, sem simulador (--speed 2, --speed max)
fonia --duration 30 --log-level debug   # encerra sozinho; logs na saída de erro
fonia --atc SBCT --callsign PR-ABC      # ATC por texto (M2): sem simulador
```

**Modo ATC (`--atc <ICAO>`, M2).** `AtcConsole` junta o que vem depois do STT: lê a fala do piloto digitada, passa pelo `RuleBasedNlu`, `AtcEngine` e `PortuguesePhraseology`, e mostra a resposta com o órgão que fala ("SOLO Curitiba: …"; o texto falado, em si, não leva o nome do órgão). Tudo entra pelo construtor (catálogo, NLU, motor, fraseologia, teclado e tela), então os testes rodam sem console e sem simulador. O aeroporto vem do `AirportCatalog` embutido; `--runway` escolhe a pista em uso. O indicativo vem de `--callsign` (sem simulador) ou, sem ele, da matrícula da aeronave no simulador escolhido por `--sim` (`SimCallsignReader` conecta, espera o primeiro estado por até 10 s e desconecta; só a matrícula é usada no M2). `Fonia.App` referencia `Fonia.Data`: é a raiz de composição, o Core continua sem referências.

Em um terminal, o estado é **um bloco que é reescrito sempre no mesmo lugar**, sem criar linha nova a cada atualização. A informação é **sempre completa**: os campos (hora, aeronave, posição, altitude, velocidade, proa, solo/ar, COM1, COM2, squawk) são distribuídos pelas linhas que a largura da janela exigir, sem partir um campo ao meio e sem esconder nenhum. Em janela larga é uma linha; em janela estreita, duas ou três. A largura é lida a cada atualização. Se a janela mudar de tamanho com o bloco já na tela, o terminal reflui as linhas antigas; o app calcula quantas linhas da tela o bloco antigo passou a ocupar, volta o cursor à primeira, apaga todas e desenha o novo ali (as linhas apagadas ficam em branco, em vez de sobrar um pedaço da antiga). A tela é abstraída em `ITerminal`, e os testes usam uma tela simulada que faz esse reflow. Só quando a saída é redirecionada para arquivo cada snapshot vira uma linha. Além do estado, há as mensagens de conexão ("Conectando...", "Conexão com o simulador perdida; tentando reconectar...", "Reconectado."). Ctrl+C encerra com limpeza: a gravação fica válida e o adapter é desconectado. Códigos de saída: 0 normal, 1 erro durante a execução (arquivo inválido ou ilegível), 2 uso incorreto.

É um executável `net10.0` sem identificador de runtime: roda no Linux com `--sim mock` e `--replay`, e a DLL nativa do SimConnect fica em `runtimes/win-x64/native`, usada só pela fonte `msfs`. A composição é manual (sem Generic Host) para abrir rápido e consumir pouco, e a linha de comando é lida à mão. Logs com `Microsoft.Extensions.Logging.Console`, enviados para a saída de erro. A lógica está em `ArgumentParser`, `StateFormatter` e `AppRunner`, testados sem console e sem simulador; `Program.cs` só liga as peças e trata o Ctrl+C. Configuração por `appsettings.json` fica para o M4.

#### Gravação de telemetria (`Fonia.Sim.Mock`)

`TelemetryRecorder` grava o que qualquer `ISimAdapter` publica (inclusive o do MSFS) em um arquivo, e `TelemetryReader` lê de volta, para reproduzir uma sessão sem o simulador. O formato é **JSON Lines**: UTF-8 sem BOM, finais de linha `
`, uma linha de cabeçalho e uma linha por `AircraftState`, com cada linha gravada e descarregada na hora (uma gravação interrompida por queda de energia ou crash mantém tudo até a última linha completa).

```
{"format":"fonia-telemetry","version":1}
{"timestamp":"2026-10-12T12:00:00+00:00","latitudeDegrees":-25.5285,"longitudeDegrees":-49.1758,"altitudeMslFeet":2988,"altitudeAglFeet":0,"groundSpeedKnots":0,"headingMagneticDegrees":150,"onGround":true,"com1":{"active":121900,"standby":118100},"com2":{"active":121500,"standby":121500},"transponderCode":1200,"registration":"PR-ABC","aircraftType":"C172"}
```

- Frequências são kHz inteiros. Nomes em camelCase; as unidades estão nos nomes.
- **Versionamento:** `version` só sobe quando uma mudança no snapshot **não** é aditiva. Campos novos opcionais não mudam a versão: o leitor ignora propriedades que não conhece. Um arquivo com outra versão é recusado com uma mensagem clara (`TelemetryFormatException`, com o número da linha), assim como cabeçalho ausente ou snapshot malformado ou incompleto.
- **Replay:** `MockSimAdapter.ReplayAsync(arquivo, velocidade, ct)` reproduz uma gravação no ritmo original: o primeiro snapshot sai na hora e cada seguinte espera a diferença entre os `timestamp` dividida pela velocidade (1 = tempo real, 2 = dobro, `double.PositiveInfinity` = sem esperar). Timestamps iguais ou que voltam no tempo não esperam. Um arquivo inválido falha com `TelemetryFormatException` depois de reproduzir a parte válida. O relógio é injetável (`TimeProvider`), então os testes de ritmo não dormem.
- A gravação termina sem erro ao cancelar o token ou quando o adapter encerra o stream, e sobrevive a quedas de conexão. A serialização é gerada em compilação (`System.Text.Json` com source generation), sem reflexão.

### 2. Áudio (`Fonia.Audio`)

Captura de microfone com push-to-talk e reprodução, via NAudio (`NAudio.WinMM`, MIT). Sem lógica de ATC aqui. O efeito de rádio (filtro passa-banda, saturação, chiado, clique de PTT, com intensidade configurável) é um módulo `RadioEffect` aplicado ao áudio do TTS; ver `docs/decisions/speech.md`.

A lógica que não depende do Windows fica no **Core** (`Fonia.Core.Audio`), testada no Linux, e o que fala com o sistema fica em `Fonia.Audio`:

- `IAudioCapture` (microfone, PCM 16 kHz mono 16 bits, o formato do reconhecedor, já entregue pelo Windows) e `IPushToTalk` (`Pressed`/`Released`). `NAudioCapture` e `KeyboardPushToTalk` são as implementações reais.
- **Push-to-talk por polling** (`PolledPushToTalk`): pergunta ao Windows a cada ~15 ms se a tecla (ou botão lateral do mouse) está apertada, via `GetAsyncKeyState`. Não usa hook, não precisa de administrador e enxerga a tecla com o simulador em foco. Tecla padrão: `RControl`. Botão de joystick fica para depois.
- `SpeechRecorder`: junta o áudio entre apertar e soltar o PTT e entrega uma `RecordedSpeech`. O microfone fica aberto o tempo todo (sem atraso de abertura do dispositivo) e guarda um *pre-roll* de 200 ms para não cortar a primeira sílaba. Descarta apertos de menos de 0,3 s e encerra falas de mais de 30 s.
- `WavEncoder` salva uma fala como WAV (usado pelo `--mic-test`).

### 3. STT (`Fonia.Speech`)

Decidido no spike do M3 (`docs/decisions/speech.md`): **Whisper.net** (MIT) com o modelo `ggml-small.bin` (MIT, 466 MB), local e offline.

- **Core** (`Fonia.Core.Speech`, sem dependência de SO): `ITranscriber` (`TranscribeAsync(RecordedSpeech, TranscriptionContext, ct) -> Transcript`), `WhisperPromptBuilder`, `PcmConverter` (PCM 16 bits para `float`). O `WavDecoder`, em `Fonia.Core.Audio`, lê WAV.
- **Prompt de vocabulário** (`WhisperPromptBuilder`): vocabulário de aviação, a estação e o **indicativo falado em alfabeto fonético** ("Papa Romeo Alfa Bravo Charlie"). Sem ele o Whisper escreve "Paparromeu"; com ele o NLU entendeu 20 de 20 frases gravadas (30% com o indicativo escrito "PRABC").
- **`Fonia.Speech`**: `WhisperTranscriber` carrega o modelo na primeira chamada e o mantém; recria o processador só quando o prompt muda. Atende uma fala por vez. Descarta anotações que não são fala ("[BLANK_AUDIO]", "[Música]").
- **CPU por padrão**, com metade dos núcleos (no máximo 8), porque o simulador precisa do resto: ~2 s por frase. **`--stt-gpu` (Vulkan)** é opcional e cai para a CPU se falhar: ~0,25 s por frase com o modelo já carregado, mas a carga inicial na GPU leva ~7 a 10 s, então a integração deve carregar o modelo na abertura do app.
- **Onde ficam os modelos e como chegam:** `ModelDirectories` define a ordem de busca: `models` ao lado do executável (onde uma distribuição que traga os modelos os põe), depois `%LOCALAPPDATA%\Fonia\models`, ou só a pasta de `--stt-dir`/`--tts-dir` quando o usuário a indica. Se faltar modelo, o app diz o que falta, quanto pesa e **pergunta antes de baixar** (só em terminal; sem terminal, apenas diz como). `fonia --download-models` baixa sem perguntar. Ver "Obtenção dos modelos" abaixo.

#### Obtenção dos modelos (`Fonia.Speech`)

- `ModelCatalog` lista os três arquivos (`ggml-small.bin`, `kokoro-v1.0.onnx`, `voices-v1.0.bin`) com URL do dono, tamanho e **SHA-256 fixados no código**. O hash do Whisper é o que o próprio Hugging Face publica para o arquivo; os do Kokoro (o GitHub não publica hash dos arquivos de release) foram calculados em dois downloads separados. Mudar de versão de modelo é mudar esta lista, e é uma decisão registrada.
- `ModelDownloader`: baixa para um arquivo `.part` e só o renomeia depois de conferir o SHA-256 (um arquivo errado nunca vira modelo e um `.part` corrompido não é retomado); **retoma** de onde parou (`Range`); confere o **espaço em disco** antes de pedir qualquer byte; desiste de uma conexão que fica **60 s sem enviar dados** (guardando o que veio); relata o progresso. Só usa `HttpClient`, sem pacote novo.
- No app (`ModelProvisioner`): lista o que falta, pergunta `[S/n]` em terminal, mostra uma linha de progresso reescrita (ou uma linha por décimo quando a saída não é um terminal) e explica cada falha (servidor, hash, disco, rede).
- Os modelos **não são redistribuídos** pelo projeto: vêm dos donos. `THIRD-PARTY-NOTICES.txt` dá o crédito e as licenças de tudo o que o app usa e baixa. O instalador do M4 vai chamar `fonia --download-models` no fim da instalação, reaproveitando este código, e uma versão "completa" com os modelos dentro cabe na mesma ordem de busca.

### 4. NLU (`Fonia.Core`)

Entrada: `Transcript` + `FlightContext`. Saída: `Intent` (`record`).

`Intent` é um `record` abstrato com `Callsign` normalizado (ex.: "PRABC") e subtipos selados. No M0 existem `RequestTaxi`, `Readback`, `SayAgain` e `Unknown`; o conjunto cresce com os marcos (previstos: `RequestStartup`, `RequestPushback`, `RequestTakeoff`, `ReportPosition`, `RequestLanding`).

`FlightContext` é o estado por voo: indicativo, `FlightPhase`, aeroporto atual (ICAO) e a instrução ainda pendente de readback. `LastInstruction` guarda a última instrução dada, para quando o piloto pede "repita". O aeroporto é o `AirportContext` (veja o motor, abaixo).

Estratégia em camadas:
1. **Regras/regex + normalização** (números, alfabeto fonético, "cinco zero" etc.). Rápido, offline, previsível. É o ponto de partida: `RuleBasedNlu` (`INlu.Interpret(texto, FlightContext)`) procura o indicativo (completo, soletrado ou pelas 3 últimas letras), as palavras de pedido (acionar/partida, pushback/empurrar, táxi), a pista e a posição no pátio onde estiverem na frase, e usa `PendingReadback` para distinguir o piloto que pede do que repete a instrução. Pedido combinado vira `RequestPushback(WithStartup: true)`. O piloto pode ser **telegráfico**: uma palavra basta se a informação está nela ("pushback", "abc taxi", "abc pronto", "acionar", "33" como readback do táxi); "pronto" sozinho é pedido de táxi, e sem pedido pendente um número solto não é readback.
2. **LLM opcional** (local ou API) só para interpretar fala fora do padrão. Sempre devolve JSON desserializado com `System.Text.Json` e validado; se inválido, cai em `Unknown`.

O NLU **não** decide o que o ATC responde.

**O NLU entende o piloto, não o manual.** A fraseologia padronizada descreve o que o *controlador* diz; o piloto fala como quiser: com "bom dia", "ok", fora de ordem, com palavras a mais, abreviando o indicativo ou dizendo "solicito táxi" em vez de "solicita instruções de táxi". Por isso o NLU não compara frases inteiras: ele procura a intenção e preenche os dados (indicativo, pista, posição, destino) mesmo que a frase fuja do padrão, e só devolve `Unknown` quando de fato não consegue, caso em que o controlador responde "PRABC, repita". As frases do manual e da prática (VATSIM-BR) entram como casos de teste, nunca como a única forma aceita.

### 5. Motor de ATC (`Fonia.Core`)

Núcleo determinístico. Máquina de estados por voo e por posição (Solo, Torre, Aproximação, Centro).

- Entrada: `Intent` + `AircraftState` + `AirportContext` (pistas, pista em uso, METAR, tráfego).
- Saída: `AtcDecision` (`record`): uma `AtcInstruction` (dados, nunca texto; no M0: `TaxiClearance` e `RequestReadbackCorrection`) e o `FlightContext` resultante.
- Verifica readback do piloto e pede correção quando necessário.

**No M2 (Solo):** `AtcEngine.Handle(Intent, FlightContext, AirportContext) -> AtcDecision`, sem estado próprio. O `AirportContext` (aeroporto, `ServiceUnit`, pista em uso) é fixo na sessão e define quem responde como no aeroporto real: tem Solo, responde o Solo; senão a Torre faz o mesmo papel; senão o AFIS só informa; senão não há serviço (`NoControlService`). A pista em uso é a pedida (`--runway`) ou, por padrão, a mais longa na cabeceira de menor número (sem vento nem METAR para escolher a outra); pistas fechadas ficam de fora. O controlador sempre aprova (sem atrasos nem sequenciamento) e o pedido de táxi recebe a pista em uso, qualquer que seja a pedida. Táxi, partida e pushback exigem readback (o Art. 12 lista o táxi; partida e pushback seguem a prática da VATSIM-BR): o do táxi precisa da pista certa, o dos outros precisa dizer o que foi aprovado ("acionamento", "pushback"). Readback certo não tem resposta (`NoResponse`), errado ou incompleto recebe "negativo" e a versão correta (`RequestReadbackCorrection`). `Unknown` vira `SayAgainRequest` ("PRABC, repita") e o pedido do piloto para repetir devolve a `LastInstruction`. **Indicativo:** um pedido (partida, pushback, táxi) sem o indicativo da aeronave, ou com outro, não é atendido: o controlador pede o indicativo (`RequestCallsign`) e guarda o pedido em `FlightContext.AwaitingCallsign`; repete o pedido até o piloto dizer o indicativo certo, e então o pedido guardado segue sem precisar ser refeito (o NLU entende só o indicativo como `CallsignOnly`). Quem chama só com o indicativo e nada guardado ouve `GoAhead`. O readback **precisa do indicativo** (o Art. 12 lista a matrícula entre o que se coteja e a prática termina o readback com ela): sem ele, o controlador responde "negativo" e repete a instrução, que continua pendente. Fases: `Parked`, `PushbackApproved`, `StartupApproved`, `Taxiing`; um pedido nunca faz a fase voltar.
- Deve ser 100% testável sem áudio e sem simulador.

### 6. Fraseologia (`Fonia.Core`)

Templates por idioma e por tipo de instrução: `AtcDecision` + idioma → texto. Números, frequências e indicativos são formatados pela camada de fraseologia como o MCA 100-16 manda: dígito a dígito, "UNO", "MEIA", "DECIMAL" (118,1 é "UNO UNO OITO DECIMAL UNO"), e o alfabeto fonético com "Romeo" (`SpokenText` em `Fonia.Core.Phraseology`; o caminho inverso, que lê números, letras e matrículas ditos pelo piloto de qualquer jeito, "uno uno oito", "quinze", "Romeu", "alfa bravo charlie", é o `SpokenCode`, sobre os tokens do `SpokenTokens`). `IPhraseology.Speak(AtcInstruction, PhraseContext)` devolve um `Utterance` (texto e `UtteranceKind`: `Radio`, que o TTS vai falar, ou `Notice`, aviso do app quando ninguém responderia no rádio) ou `null` quando o controlador não diz nada (readback certo); `PortuguesePhraseology` é a implementação pt-BR. Números, letras e indicativos saem em maiúsculas e as palavras da frase em minúsculas. O controlador segue o **MCA 100-16, edição 2025** (Portaria DECEA/DNOR1 nº 1.716/2025); as fontes, o catálogo de frases com o artigo de cada uma e as variantes da prática estão em [`docs/phraseology/`](phraseology/README.md). O nome falado de cada aeroporto ("Solo Galeão", "Torre Guarulhos") vem de [`data/atc-names.csv`](../data/atc-names.csv).

### 7. TTS (`Fonia.Speech`)

Interface `ISynthesizer` (`SynthesizeAsync(string text, Voice voice) -> AudioClip`). Decidido no spike do M3: **Kokoro-82M** (Apache-2.0) em ONNX Runtime, na CPU. Vozes diferentes por posição (Solo, Torre, etc.) para dar identidade, escolhidas pelo usuário ou aleatórias.

O texto da fraseologia **não é alterado** para a voz: antes do modelo, ele passa por duas etapas puras do Core (`Fonia.Core.Speech`):

1. `Pronunciation.Respell`: dá uma grafia brasileira às palavras que a ortografia leria errado ("Romeo" vira "romeu", "Charlie" vira "tchárli", "pushback" vira "púchibéqui").
2. `PortuguesePhonemizer`: transforma o texto em fonemas, pelas regras do português do Brasil, no alfabeto do Kokoro. Sem espeak-ng (GPL): as regras são do projeto. Detalhes e validação em `docs/decisions/speech.md`.

Depois disso, no `Fonia.Speech`, o `KokoroSynthesizer` numera os fonemas (`KokoroTokenizer`), escolhe a linha de estilo da voz (`NpzVoiceReader`) e roda o modelo no ONNX Runtime; o Core corta o silêncio (`SilenceTrimmer`) e divide textos longos (`PhonemeChunker`). As vozes aprovadas ficam no `VoiceCatalog` (`pf_dora`, `pm_alex`); o `VoiceSelector` sorteia uma por posição (mantendo-a na sessão) ou usa a que o usuário fixou. O `RadioEffect` (Core, aritmética pura) vem por último: filtro de banda de rádio (`RadioFilter`), saturação, chiado, clique de abertura do PTT e *squelch*, em quatro intensidades (desligado, leve, média por padrão, forte).

O modelo (`kokoro-v1.0.onnx`, 325 MB) e as vozes (`voices-v1.0.bin`, 28 MB) seguem a mesma regra do Whisper: procurados nas mesmas pastas e baixados do mesmo jeito (`--download-models`, ou a pergunta quando faltam).

### 8. Dados (`Fonia.Data`)

- Aeroportos, pistas e frequências: OurAirports (domínio público), em um **recorte do Brasil embutido no app** (`data/ourairports/`, ~0,8 MB, atualizado com `tools/update-ourairports.cs`). `AirportCatalog` lê os CSVs (leitor de CSV próprio, sem pacote) e entrega os modelos `Airport`/`Runway`/`AirportFrequency` do Core em ~50 ms; linha inválida não derruba a carga, vira um aviso com o número da linha. O rumo da pista, que quase nunca vem nos dados, é derivado do número do designador (`RunwayDesignator`). O nome falado de cada aeroporto vem de `data/atc-names.csv`.
- METAR/TAF: aviationweather.gov, com cache e fallback para clima do simulador.
- Plano de voo (futuro): API do SimBrief.

### 9. Laço de voz (`Fonia.Core.Pipeline`)

Junta as etapas anteriores, sem regra nova: a decisão é do motor e as palavras são dos templates.

- `VoiceExchange`: **uma fala** do piloto por todas as etapas. Reconhece (`ITranscriber`, com o indicativo e a posição no prompt), interpreta (`INlu`), decide (`IAtcEngine`), escreve (`IPhraseology`), fala (`ISynthesizer`, com a voz da posição escolhida pelo `VoiceSelector`) e aplica o `RadioEffect`. Devolve o texto ouvido, a decisão, a resposta e o áudio, com o **tempo de cada etapa** (`StageTimings`); o que é `Notice` (aviso do app) só é mostrado, não falado, e o readback certo não gera fala. Chama `onHeard` assim que o texto sai do reconhecedor, para a tela mostrá-lo antes de a resposta ficar pronta.
- `VoiceLoop`: o rádio do piloto. Escuta o PTT (`SpeechRecorder`), faz uma troca por vez e leva o estado do voo de uma troca para a outra. Como num rádio de verdade, o **canal fica ocupado** enquanto o controlador responde: o PTT apertado nessa hora é ignorado (`OnChannelBusy`). Uma falha em uma troca (um modelo que sumiu, um dispositivo) é avisada e o laço continua; parar (Ctrl+C) corta a fala e fecha o microfone e a tecla.
- `IVoiceLoopObserver`: o que a interface precisa saber (o que foi ouvido, a resposta, canal ocupado, fala curta, falha). O console mostra tudo em texto, além da voz.
- `IAudioPlayer` (`NAudioPlayer`, `WaveOutEvent`): toca a resposta e só termina quando ela acaba; cancelar corta o som.
- `IWarmUp`: o reconhecedor e a voz carregam os modelos **ao abrir o app** (com o indicativo e a posição reais), para a primeira fala não pagar a carga (~1 a 10 s, mais com GPU). Se um modelo faltar, o app avisa de onde baixar antes de abrir o microfone.

No app, `fonia --atc <ICAO> --voice` usa tudo isso (`VoiceMode`), com as opções `--ptt-key`, `--mic-device`, `--audio-out`, `--stt-*`, `--tts-*` e `--radio-effect`.

### 10. Quem responde: a frequência do rádio (`Fonia.Core.Atc`)

Como num rádio de verdade, **quem responde é quem está na frequência sintonizada**, não o que o aeroporto tem de padrão:

- `FrequencyResolver.Apply(airport, com1)` devolve o `AirportContext` daquela fala: `Unit` = Solo, Torre ou AFIS se a frequência é a dele (do `Airport.Frequencies`), ou **ninguém** em qualquer outra (inclusive ATIS e Aproximação, que não atendem quem está no solo); `Tuned` guarda a frequência. Sem leitura do rádio (nulo), vale a unidade padrão do aeroporto, que é como o modo texto sem simulador sempre funcionou.
- De onde vem o rádio: `--com1 <MHz>` ou `/com1 <MHz>` na sessão de texto (o que o usuário digitou vence), ou o **COM1 do simulador, lido a cada fala** (`SimSession` mantém a conexão aberta durante a sessão; o indicativo vem da mesma conexão). O laço de voz lê o rádio a cada transmissão, então trocar de frequência no meio do voo muda quem responde na fala seguinte, e a posição (Solo ou Torre) tem voz própria.
- O motor continua **determinístico e sem estado**: recebe o contexto já resolvido. Em frequência de ninguém, qualquer fala recebe o aviso `NoOneOnFrequency` (com as frequências do aeroporto); a Torre de um aeroporto **com Solo** não faz o trabalho do Solo e diz onde chamá-lo (`CallTheSolo`, aviso, não rádio).
- **Passagem Solo → Torre:** o piloto relata o ponto de espera (`ReportHoldingShort`; o NLU o separa do cotejamento do táxi pelo "no/em ponto de espera") e o Solo responde `HandoffToTower` com a frequência da Torre ("chame Torre Curitiba UNO UNO OITO DECIMAL CINCO CINCO"); a fase vira `HandedToTower`. O cotejamento da troca é **opcional**: fica "pendente" só para o NLU ler o que o piloto disser em seguida como resposta, e o Solo a recebe em silêncio, sem checar a frequência dita (o reconhecedor erra números, e o manual não a exige). Não é detectado pela posição porque o modelo de pista ainda não tem coordenadas das cabeceiras.

### 11. A Torre: alinhar e decolar (`Fonia.Core.Atc`)

Depois da passagem do Solo (`HandedToTower`), o piloto sintoniza a frequência da Torre e o motor, com `airport.Unit == Tower`, conduz a saída (MCA 100-16/2025, Art. 126; catálogo em `docs/phraseology/torre-pt-br.md`):

```
RequestDeparture ("pronto para partida")  ->  LineUpAndWait (pista)   ->  fase LineUpCleared,   cotejamento exigido
ReportLinedUp ("alinhado")                ->  TakeoffClearance (pista) ->  fase TakeoffCleared,  cotejamento exigido
ReportTakingOff ("decolando")             ->  em silêncio
```

- A **pista** é a que o Solo autorizou no táxi (`FlightContext.AssignedRunway`), ou a pista em uso se não houver. "No ponto de espera" na frequência da Torre vale como pedido de partida; o piloto que já diz "alinhado" recebe a autorização de decolagem direto.
- O **NLU** separa o pedido de partida do cotejamento pelas palavras (quem repete diz "autorizado", "alinhe e mantenha" com a pista) e pela fase do voo: "partida" é a partida dos motores antes do táxi e a partida para decolar depois dele.
- **Cotejamento exigido, com o indicativo e a pista**, nas duas instruções (Art. 12: entrar na pista e decolar): sem a palavra ("alinh", "decol") ou com a pista errada, "negativo" e a versão certa.
- **Art. 50:** "decolagem" só aparece na autorização de decolagem (ou no seu cancelamento), nunca em "alinhe e mantenha".
- O **vento** vem do `AirportContext.Wind` e é dito no fim da autorização (`SpokenText.Wind`, Art. 31); sem fonte de vento (ainda não há METAR nem leitura do simulador) a frase sai sem ele.
- Na frequência do **Solo**, "pronto para partida", "alinhado" e "decolando" não são atendidos: o Solo passa o voo para a Torre, como no relato do ponto de espera.

## Fluxo de uma interação (exemplo)

1. Piloto aperta PTT e diz: "Solo Curitiba, PR-ABC, pátio 1, posição 3, solicita instruções de táxi".
2. STT gera o texto.
3. NLU extrai `RequestTaxi(Callsign: "PRABC")` (e a posição, se houver).
4. Motor ATC consulta o estado (aeronave em solo, no pátio, pista 15 em uso?) e decide `TaxiClearance(Runway: "15")`. Sem dados de taxiway, a instrução é a forma curta do manual, sem rota.
5. Fraseologia monta: "PR-ABC, autorizado táxi para o ponto de espera da pista 15." (falado: "PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da pista UNO CINCO").
6. TTS fala; o motor aguarda o cotejamento (readback): instruções de táxi devem ser repetidas (MCA 100-16, Art. 12), e se vierem erradas o controle diz "negativo" seguido da versão correta.

Quem responde depende do aeroporto, como na vida real: Solo se houver frequência de Solo; senão a Torre dá o acionamento e o táxi; em aeródromo só com AFIS ("Rádio") não há autorização, apenas informação; sem nenhum dos três, não há serviço.

## Execução e concorrência

O pipeline é assíncrono. Telemetria, entrada de áudio e saída de áudio rodam em tarefas separadas, comunicando-se por `Channel<T>` e `CancellationToken`. O motor de ATC processa um evento por vez (fila única), o que evita condições de corrida no estado do voo.

## Requisitos não funcionais

- **Latência alvo:** da soltura do PTT ao início da resposta falada, abaixo de ~3 s no caminho offline com regras.
- **Offline-first.**
- **Configuração** em `appsettings.json` (`Microsoft.Extensions.Configuration`): idioma, modelo de STT, voz, tecla PTT, caminho do SDK/simulador.
- **Logs** estruturados (`Microsoft.Extensions.Logging`) em todas as fronteiras de módulo, para depurar sessões no simulador.
- **Telemetria gravada:** capacidade de salvar e reproduzir sessões (estado + fala), para reproduzir bugs sem o simulador.
- **Distribuição:** `dotnet publish` self-contained em arquivo único, depois instalador (MSIX ou Inno Setup).

## Decisões tomadas

- **Linguagem: C# / .NET (2026-10-12).** Motivos: SimConnect oficial e nativo do ecossistema MSFS, instalação simples para usuários finais, UI desktop melhor e um único runtime. Contrapartida aceita: o ecossistema de STT/TTS local é menos maduro que o de Python, então o M3 começa com um spike comparando opções. Plano B, se necessário: manter STT/TTS atrás de `ITranscriber`/`ISynthesizer` e, se preciso, rodá-los em um processo auxiliar.
- **Testes e qualidade (M0, 2026-10-12).** xUnit v3 sobre Microsoft.Testing.Platform (exigido pelo `dotnet test` do SDK 10) com Shouldly (BSD-3); FluentAssertions foi descartado por ter licença comercial desde a v8. `TreatWarningsAsErrors` só em `src/`; testes mantêm os warnings visíveis. `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild` e SDK fixado em `global.json` (`rollForward: latestFeature`). Pacotes com versão central (`Directory.Packages.props`). Solução no formato `.sln` clássico.
- **Isolamento do Core verificado por teste.** `SolutionLayoutTests` falha se o `Fonia.Core` referenciar outro projeto da solução ou uma biblioteca de simulador, ou se uma biblioteca de simulador aparecer fora de `Fonia.Sim.*`.
- **CI.** Linux em todo PR e push na `main` (build, test, `dotnet format --verify-no-changes`), em `ci.yml`. O Windows (build, test) tem workflow próprio, `ci-windows.yml`, porque o GitHub filtra por caminho só no nível do workflow: roda em PRs que mexem em `src/`, `tests/` ou nas configurações de build, em todo push na `main` (exceto documentação) e manualmente. Windows custa o dobro dos minutos em repositório privado, e um teste de temporização que só falhou no runner mais lento do Windows mostrou que esperar o merge para vê-lo era caro.
- **Licença do projeto: Apache-2.0 (2026-10-12).** Open-source, com patente explícita e compatível com dependências MIT/BSD/Apache. Consequência: dependências e modelos de voz/IA precisam ter licença permissiva e compatível (sem GPL/AGPL nem restrição de uso comercial); informar a licença ao propor uma dependência nova.
- **SimConnect (M1, 2026-10-12):** SimConnect.NET (MIT) atrás de `ISimConnectSession`, a confirmar no spike; `Fonia.Sim.Msfs` e `Fonia.App` em `net10.0`. A redistribuição do `SimConnect.dll` é um risco aberto para o M4. Ver `docs/decisions/simconnect.md`.

- **Fraseologia (M2, 2026-10-13).** O controlador segue o MCA 100-16 em vigor; a prática da VATSIM Brasil é referência secundária (cobre o que o piloto de fato diz) e o NLU entende as duas. A primeira pesquisa usou as edições de 2016 por engano e concluiu, errado, que o manual não tinha `pushback`: a edição vigente (2025, Art. 123) tem. A página oficial avisa que o manual será alterado em 26/11/2026; ver `docs/phraseology/README.md`.
- **Nomes de órgãos** vêm de `data/atc-names.csv` (mantido à mão e revisado aos poucos), porque o município dos dados erra justamente nos aeroportos mais usados (SBGR "São Paulo", SBKP "Campinas", SBCF "Belo Horizonte").

## Decisões em aberto

- Stack de STT e TTS locais em .NET (spike no M3).
- Como representar o gráfico de táxi (OurAirports não tem taxiways). Opção inicial: instruções simplificadas ("táxi para o ponto de espera da pista X") sem rota detalhada.
- Interface do usuário: começar por console; depois WPF, WinUI 3 ou Avalonia.
