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
| `Fonia.Audio` | Captura de microfone, push-to-talk, playback. | `net10.0-windows` |
| `Fonia.Speech` | Implementações de STT e TTS. | a definir (M3) |
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
```

Em um terminal, o estado é **sempre uma única linha, reescrita no lugar**. A largura da janela é lida a cada atualização, então redimensionar funciona, e a linha **se adapta**: usa o formato completo se couber e, senão, vai tirando o menos essencial (primeiro as frequências de standby, depois a altura AGL, depois o COM2, depois os rádios) e só então corta com `…`. Posição, altitude, velocidade, proa, solo/ar e o squawk são mantidos o máximo possível, e a linha nunca passa da largura (não quebra). Se a janela encolher **enquanto** uma linha mais larga já está na tela, o terminal a reflui em duas ou mais linhas; o app calcula quantas linhas ela passou a ocupar, volta o cursor à primeira, apaga todas e desenha a atualização ali, para continuar sendo uma única linha (as linhas apagadas ficam em branco, em vez de sobrar um pedaço da antiga). A tela é abstraída em `ITerminal`, e os testes usam uma tela simulada que faz esse reflow. Só quando a saída é redirecionada para arquivo cada snapshot vira uma linha (e aí sempre no formato completo). Além do estado, há as mensagens de conexão ("Conectando...", "Conexão com o simulador perdida; tentando reconectar...", "Reconectado."). Ctrl+C encerra com limpeza: a gravação fica válida e o adapter é desconectado. Códigos de saída: 0 normal, 1 erro durante a execução (arquivo inválido ou ilegível), 2 uso incorreto.

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

Captura de microfone com push-to-talk (tecla global configurável, depois botão do joystick) e reprodução, via NAudio. Sem lógica de ATC aqui. Efeito de rádio (filtro passa-banda, ruído leve) fica como etapa opcional de pós-processamento do áudio do TTS.

### 3. STT (`Fonia.Speech`)

Interface `ITranscriber` (`TranscribeAsync(AudioClip) -> Transcript`). Implementação a escolher no M3 após um spike com medição de latência e precisão em pt-BR. Dica: usar prompt inicial com vocabulário de aviação (indicativos, "QNH", "pista", "autorizado", "táxi") melhora a precisão em modelos Whisper.

### 4. NLU (`Fonia.Core`)

Entrada: `Transcript` + `FlightContext`. Saída: `Intent` (`record`).

`Intent` é um `record` abstrato com `Callsign` normalizado (ex.: "PRABC") e subtipos selados. No M0 existem `RequestTaxi`, `Readback`, `SayAgain` e `Unknown`; o conjunto cresce com os marcos (previstos: `RequestStartup`, `RequestPushback`, `RequestTakeoff`, `ReportPosition`, `RequestLanding`).

`FlightContext` é o estado por voo: indicativo, `FlightPhase`, aeroporto atual (ICAO) e a instrução ainda pendente de readback. O contexto do aeroporto (`AirportContext`) só entra no M2, junto com os dados do OurAirports.

Estratégia em camadas:
1. **Regras/regex + normalização** (números, alfabeto fonético, "cinco zero" etc.). Rápido, offline, previsível. É o ponto de partida.
2. **LLM opcional** (local ou API) só para interpretar fala fora do padrão. Sempre devolve JSON desserializado com `System.Text.Json` e validado; se inválido, cai em `Unknown`.

O NLU **não** decide o que o ATC responde.

### 5. Motor de ATC (`Fonia.Core`)

Núcleo determinístico. Máquina de estados por voo e por posição (Solo, Torre, Aproximação, Centro).

- Entrada: `Intent` + `AircraftState` + `AirportContext` (pistas, pista em uso, METAR, tráfego).
- Saída: `AtcDecision` (`record`): uma `AtcInstruction` (dados, nunca texto; no M0: `TaxiClearance` e `RequestReadbackCorrection`) e o `FlightContext` resultante.
- Verifica readback do piloto e pede correção quando necessário.
- Deve ser 100% testável sem áudio e sem simulador.

### 6. Fraseologia (`Fonia.Core`)

Templates por idioma e por tipo de instrução: `AtcDecision` + idioma → texto. Números, frequências e indicativos são formatados pela camada de fraseologia (ex.: "um dois sete decimal oito" vs. "cento e vinte e sete oito"). Referências oficiais ficam em `docs/phraseology/`.

### 7. TTS (`Fonia.Speech`)

Interface `ISynthesizer` (`SynthesizeAsync(string text, Voice voice) -> AudioClip`). Implementação a escolher no M3 (candidato: Piper). Vozes diferentes por posição (Solo, Torre, etc.) para dar identidade.

### 8. Dados (`Fonia.Data`)

- Aeroportos, pistas e frequências: OurAirports (domínio público).
- METAR/TAF: aviationweather.gov, com cache e fallback para clima do simulador.
- Plano de voo (futuro): API do SimBrief.

## Fluxo de uma interação (exemplo)

1. Piloto aperta PTT e diz: "Torre de Curitiba, PR-ABC, solicito táxi para a pista 15".
2. STT gera o texto.
3. NLU extrai `RequestTaxi(Runway: "15", Callsign: "PRABC")`.
4. Motor ATC consulta o estado (aeronave em solo, no pátio, pista 15 em uso?) e decide `TaxiClearance(Route: ..., HoldingPoint: ...)`.
5. Fraseologia monta: "PR-ABC, Curitiba Solo, táxi autorizado para o ponto de espera da pista 15 via táxi Alfa. QNH 1017."
6. TTS fala; o motor aguarda o readback.

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
- **CI (M0).** Linux em todo PR e push na `main` (build, test, `dotnet format --verify-no-changes`); Windows (build, test) só em push na `main` e manualmente, por causa do custo dos minutos em repositório privado.
- **Licença do projeto: Apache-2.0 (2026-10-12).** Open-source, com patente explícita e compatível com dependências MIT/BSD/Apache. Consequência: dependências e modelos de voz/IA precisam ter licença permissiva e compatível (sem GPL/AGPL nem restrição de uso comercial); informar a licença ao propor uma dependência nova.
- **SimConnect (M1, 2026-10-12):** SimConnect.NET (MIT) atrás de `ISimConnectSession`, a confirmar no spike; `Fonia.Sim.Msfs` e `Fonia.App` em `net10.0`. A redistribuição do `SimConnect.dll` é um risco aberto para o M4. Ver `docs/decisions/simconnect.md`.

## Decisões em aberto

- Stack de STT e TTS locais em .NET (spike no M3).
- Como representar o gráfico de táxi (OurAirports não tem taxiways). Opção inicial: instruções simplificadas ("táxi para o ponto de espera da pista X") sem rota detalhada.
- Interface do usuário: começar por console; depois WPF, WinUI 3 ou Avalonia.
