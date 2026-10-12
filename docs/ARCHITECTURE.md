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
| `Fonia.Sim.Msfs` | `MsfsSimAdapter` (SimConnect). | `net10.0-windows` |
| `Fonia.Audio` | Captura de microfone, push-to-talk, playback. | `net10.0-windows` |
| `Fonia.Speech` | Implementações de STT e TTS. | a definir (M3) |
| `Fonia.App` | Executável e composição de dependências (console primeiro, UI depois). | `net10.0-windows` |

Dependências sempre apontam para o Core. O Core não referencia nenhum outro projeto.

## Componentes

### 1. ISimAdapter (`Fonia.Core` + `Fonia.Sim.*`)

Interface única entre o app e qualquer simulador.

```csharp
public interface ISimAdapter : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync();
    AircraftState GetState();                       // último snapshot conhecido
    IAsyncEnumerable<AircraftState> StreamAsync(CancellationToken ct);
    Task SetComFrequencyAsync(int radio, double mhz, CancellationToken ct); // opcional
}
```

`AircraftState` (`record`) contém, no mínimo: latitude, longitude, altitude (MSL e AGL), velocidade no solo, proa, em solo (bool), COM1/COM2 ativo e standby, transponder, indicativo/matrícula, tipo de aeronave.

Implementações:

| Adapter | Status | Observações |
|---|---|---|
| `MockSimAdapter` | M0 | Estado programável e replay de telemetria gravada. Base de todos os testes. |
| `MsfsSimAdapter` | M1 | SimConnect. Mesmo adapter para MSFS 2020 e 2024, a validar. Só Windows. |
| `XPlaneSimAdapter` | futuro | UDP/datarefs. |

### 2. Áudio (`Fonia.Audio`)

Captura de microfone com push-to-talk (tecla global configurável, depois botão do joystick) e reprodução, via NAudio. Sem lógica de ATC aqui. Efeito de rádio (filtro passa-banda, ruído leve) fica como etapa opcional de pós-processamento do áudio do TTS.

### 3. STT (`Fonia.Speech`)

Interface `ITranscriber` (`TranscribeAsync(AudioClip) -> Transcript`). Implementação a escolher no M3 após um spike com medição de latência e precisão em pt-BR. Dica: usar prompt inicial com vocabulário de aviação (indicativos, "QNH", "pista", "autorizado", "táxi") melhora a precisão em modelos Whisper.

### 4. NLU (`Fonia.Core`)

Entrada: `Transcript` + `FlightContext`. Saída: `Intent` (`record`).

Exemplos de intenções: `RequestStartup`, `RequestPushback`, `RequestTaxi`, `RequestTakeoff`, `Readback`, `ReportPosition`, `RequestLanding`, `SayAgain`, `Unknown`.

Estratégia em camadas:
1. **Regras/regex + normalização** (números, alfabeto fonético, "cinco zero" etc.). Rápido, offline, previsível. É o ponto de partida.
2. **LLM opcional** (local ou API) só para interpretar fala fora do padrão. Sempre devolve JSON desserializado com `System.Text.Json` e validado; se inválido, cai em `Unknown`.

O NLU **não** decide o que o ATC responde.

### 5. Motor de ATC (`Fonia.Core`)

Núcleo determinístico. Máquina de estados por voo e por posição (Solo, Torre, Aproximação, Centro).

- Entrada: `Intent` + `AircraftState` + `AirportContext` (pistas, pista em uso, METAR, tráfego).
- Saída: `AtcDecision` (`record`): tipo de instrução, parâmetros (pista, rota de táxi, QNH, etc.) e o novo estado.
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

## Decisões em aberto

- Biblioteca/abordagem de SimConnect (SDK oficial vs. wrapper da comunidade) e licenciamento das DLLs.
- Stack de STT e TTS locais em .NET (spike no M3).
- Como representar o gráfico de táxi (OurAirports não tem taxiways). Opção inicial: instruções simplificadas ("táxi para o ponto de espera da pista X") sem rota detalhada.
- Interface do usuário: começar por console; depois WPF, WinUI 3 ou Avalonia.
- Biblioteca de asserções nos testes (Shouldly vs. FluentAssertions) e política de warnings.
- Modelo de licenciamento do projeto: fechado por enquanto; decidir depois entre manter proprietário ou abrir (MIT/Apache-2.0 vs. GPL). Até lá, preferir dependências com licenças permissivas para não limitar nenhuma das opções.
