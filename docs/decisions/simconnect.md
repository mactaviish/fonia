# Decisão: como falar com o SimConnect (M1)

- **Status:** proposta aprovada em 2026-10-12; spike concluído em 2026-10-12 (resultados abaixo). A reconexão **não** é resolvida pela biblioteca; o algoritmo próprio foi prototipado e validado nos dois cenários (MSFS fecha/reabre; app abre antes do MSFS). Falta só validar o adapter real (tarefa 10).
- **Escopo:** MSFS 2020 no M1. MSFS 2024 é M5.

## Contexto

O `Fonia.Sim.Msfs` precisa ler a telemetria do MSFS pelo SimConnect. O projeto é Apache-2.0 e o `CLAUDE.md` exige licenças permissivas e nenhuma DLL proprietária versionada sem verificar a licença. O desenvolvimento acontece sem o simulador (Linux/CI); só o mantenedor consegue rodar o MSFS.

## Opções avaliadas

| Opção | Licença | Observações |
|---|---|---|
| **A. SimConnect.NET** 0.2.2 (BARS) | MIT | Wrapper gerenciado, `net8.0`/`net9.0`, sem dependências NuGet, publicado em 2026-07-23. **Beta** (a API pode mudar). Inclui o `SimConnect.dll` nativo da Microsoft (win-x64, 85 KB) em `runtimes/win-x64/native`. Detecta MSFS 2020/2024. |
| B. DLLs do SDK oficial, por caminho configurável | EULA do SDK | Nada no repositório. O usuário final não tem o SDK, então a distribuição fica em aberto. |
| C. CTrue.FsConnect 1.4.0 | MIT (com aviso de binários da Microsoft) | Só `.NET Framework 4.6.1`; embute as DLLs do SDK 0.10.0 de 2020. **Descartada.** |
| D. P/Invoke próprio sobre `SimConnect.dll` | nossa | Controle total, ~10 funções. Sem poder rodar o MSFS aqui, erros de marshaling só aparecem após validação manual. **Plano B.** |
| CsSimConnect, ASimFrame | MIT / arquivo | Uso muito baixo (centenas de downloads). Descartadas. |

## Decisão

Usar a **opção A (SimConnect.NET)** atrás de uma interface interna `ISimConnectSession` em `Fonia.Sim.Msfs`. Toda a lógica do adapter (mapeamento de unidades, estado, reconexão) fica contra essa interface, testada com uma sessão falsa; só a implementação real toca a biblioteca. Assim, trocar para a opção D, se o spike mostrar problema, não afeta o resto.

Consequências para o repositório:

- `Fonia.Sim.Msfs` e `Fonia.App` passam a `net10.0` (não `-windows`). Nada neles usa API exclusiva do Windows; só a DLL nativa é Windows em tempo de execução. Assim compilam e são testados no CI Linux, e o app roda no Linux com replay/mock.
- Os testes do adapter ficam em `Fonia.Sim.Msfs.Tests`, que referencia `Fonia.Sim.Msfs` (exceção à regra "testes referenciam só Core e Mock") e usa apenas a sessão falsa.
- Bibliotecas de simulador continuam permitidas só em `Fonia.Sim.*` (teste de arquitetura existente).

## Licenciamento da DLL nativa (risco aberto)

Não encontrei declaração oficial da Microsoft ou da Asobo permitindo redistribuir o `SimConnect.dll` (há [dois pedidos](https://devsupport.flightsimulator.com/t/redistribution-rights-for-msfs-2024-native-simconnect-dll/18233) [no fórum oficial](https://devsupport.flightsimulator.com/t/redistribution-permission-for-msfs-2024-simconnect-dlls/18295) sem resposta). Programas conhecidos a distribuem, mas isso não prova permissão.

- **Desenvolvimento:** sem problema; nenhuma DLL da Microsoft é versionada neste repositório (ela vem pelo pacote NuGet, no cache local).
- **Distribuição (M4):** usar a opção A significa redistribuir a DLL que o pacote embute. Decidir antes do instalador entre (1) manter e documentar o risco, (2) excluir o ativo nativo e carregar a DLL do SDK/instalação do usuário por caminho configurável, ou (3) obter confirmação por escrito.
- Ação sugerida: perguntar no fórum de desenvolvedores do MSFS.

## Compatibilidade MSFS 2020 e 2024

A [documentação do SDK 2024](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_SDK.htm) indica uma mudança de tamanho do campo `ident` (heliportos) em estruturas de *facilities* (aeroportos). Para o M1, que só lê SimVars básicas, isso não importa; reavaliar no M5. Há também um [relato](https://devsupport.flightsimulator.com/t/simconnect-open-consistently-returns-e-fail-in-msfs-2024-1-7-35-0/18221) de `SimConnect_Open` falhando com E_FAIL no MSFS 2024 1.7.35.0, sem correção confirmada; não afeta o M1.

## Resultados do spike (2026-10-12)

Ambiente: Windows 11, .NET 10.0.12, SimConnect.NET 0.2.2, **MSFS 2020** (`IsMSFS2024=False`) com um Fenix A320, programa sem privilégio de administrador. Código em `spikes/SimConnectSpike`.

| Pergunta | Resultado |
|---|---|
| SimConnect.NET funciona em .NET 10? | **Sim.** Conectou na primeira tentativa, e o `SimConnect.dll` nativo foi copiado para a saída pelo pacote. |
| Atualização e custo | 1 atualização por segundo por grupo (`SimConnectPeriod.Second`). CPU média do spike de ~1,8% de um núcleo (rodada de 30 s, com escrita no console) e ~45 MB de memória. A rodada curta de 9 s deu 5,7% por causa do JIT na partida. |
| Posição, altitude, velocidade, proa, em solo | Lidos com as unidades `degrees`, `feet`, `knots`, `degrees`, `bool`. Valores coerentes depois de carregar o voo; confirmado em movimento (até 324 kt). |
| Matrícula | `ATC ID` devolve a matrícula (ex.: `PR-FRA`). Ela pode **mudar logo após o voo carregar** (apareceu antes um valor de livery); o adapter não deve guardá-la uma vez só. |
| Tipo da aeronave | Aeronave padrão (CJ4): `ATC MODEL='C25C'`, `ATC TYPE='Cessna'`. Fenix A320 (terceiros): `ATC MODEL` devolveu uma **chave de tradução** (`ATCCOM.AC_MODEL_A320.0.text`), não `A320`. Plano: extrair o trecho após `AC_MODEL_` e, se não houver, usar o texto bruto. |
| Frequências COM | **Usar a unidade `Hz`.** Em `Hz` os quatro valores batem exatamente com a leitura escalar (ex.: `122800000` Hz = 122,800 MHz = 122800 kHz), o que combina com `Frequency` em kHz inteiros. Em `MHz`, pelo caminho de struct da biblioteca, **COM1 veio errado** (122,000 em vez de 122,800; 124,000 em vez de 124,850) enquanto COM2 veio certo; a leitura escalar em `MHz` estava correta. **Causa encontrada na tarefa 7:** quando o tipo não é declarado, o SimConnect.NET o **infere pelo nome da variável**, e inferiu `Integer32` para os campos do COM1 (não para os do COM2). Um `double` em MHz lido como inteiro perde os decimais (122,8 vira 122); em Hz os valores são inteiros e sobreviveram por sorte. Por isso a sessão declara o tipo de **todos** os campos explicitamente. |
| Transponder | `TRANSPONDER CODE:1` em `bco16` devolveu `8192` (`0x2000` = squawk 2000), em `number` devolveu `2000`. **Plano:** usar `bco16` (unidade documentada) e decodificar os dígitos em hexadecimal. **Confirmado** com squawk 1234 (`0x1234` = 4660) no Cessna CJ4, e 7000. O estado (`TRANSPONDER STATE:1`) variou (0 no Fenix parado, 4 no CJ4 em voo). |
| Voo carregado ou não | Antes do voo carregar, os dados eram valores iniciais (lat ≈ 0,0004, lon ≈ 0,014, 9 ft). O evento de sistema `Sim` (dado 1 = simulação ativa) e `SIM DISABLED` indicam o estado. **O adapter não deve publicar snapshots até o voo estar ativo.** |
| Eventos de sistema | `Sim`, `SimStart`, `SimStop`, `Pause` e `FlightLoaded` foram aceitos; `Sim` e `Pause` entregam o valor atual logo na assinatura. |
| Elevação | Spike sem administrador conectou normalmente ao MSFS em execução. |

### Reconexão: a biblioteca não percebe o MSFS fechar

Teste do mantenedor (spike v1, 30 min): com o spike rodando, o MSFS foi fechado e aberto de novo, carregando outra aeronave (Cessna CJ4). Resultado:

- `IsConnected` ficou **`True` o tempo todo**; nenhum `ConnectionStatusChanged` nem `ErrorOccurred` foi disparado, mesmo com `AutoReconnectEnabled=true` (3 tentativas, 2 s). O `AutoReconnect` da biblioteca **não é confiável** para esse caso.
- As assinaturas simplesmente pararam de entregar dados. Pedidos novos passaram a falhar com `E_FAIL` (`-2147467259`), que é o sinal de conexão morta.
- Recarregar o voo **dentro do mesmo MSFS** (voltar ao menu e iniciar outro voo) funciona sem reconectar: os eventos `SimStop`/`Sim=0` e `SimStart`/`Sim=1` aparecem, as atualizações param durante o menu/carregamento (`+0`) e voltam sozinhas.
- Uma execução nova do spike, com o MSFS novo, conectou e leu normalmente (Cessna CJ4): `ATC MODEL='C25C'`, transponder `0x1234` = squawk 1234, COM1 130,850 MHz (`130850000` Hz), velocidade até 324 kt. Ou seja, um cliente **novo** conecta bem; o problema é só perceber que o antigo morreu.

### Reconexão: o que o spike v2 mostrou (2 rodadas com o mantenedor)

| Cenário | Resultado |
|---|---|
| **A.** MSFS fecha e reabre com o app rodando | A queda foi detectada em ~4 s pela sonda (`TestConnectionAsync`) falhando com `E_FAIL`. O app tentou reconectar a cada 2 s (50 tentativas) e **recuperou sozinho** depois que o MSFS voltou e carregou o voo (98 s até a primeira conexão; dados reais ao carregar o voo). |
| **B.** App abre antes do MSFS | `ConnectAsync` falha **na hora** (`E_FAIL`, poucos ms), então tentar a cada 2 s é barato. Conectou na 62ª tentativa (~2 min), antes do MSFS terminar de carregar, e recebeu dados quando o voo carregou, sem nenhuma ação do usuário. |
| Sinais de queda | A mensagem `Quit` **nunca chegou** (nem pelo `RawMessageReceived`), e a biblioteca não disparou nenhum evento de queda. **O único sinal confiável foi a sonda falhando com `E_FAIL`.** |

Três armadilhas que o log mostrou (e que o adapter precisa evitar):

1. **A sonda dá falso positivo por timeout.** Com o MSFS vivo mas no menu ou carregando (`Sim=0`), `TestConnectionAsync` expira (`A task was canceled`) porque não há aeronave do usuário para responder. O spike tratava isso como queda e recriava o cliente a cada ~12 s (sessões 4 a 13 do log A). **Regra:** só `E_FAIL` conta como conexão morta; timeout significa "simulador não está respondendo agora" e não derruba a sessão.
2. **O `ConnectAsync` tem sucesso antes de o MSFS estar pronto.** A mensagem `Open` (servidor pronto) chega segundos ou minutos depois (11 s na sessão 14; ~83 s no cenário B). Assinaturas criadas antes disso às vezes funcionam (sessão 14), mas outras vezes o servidor as rejeita com `UnrecognizedId` e elas nunca entregam dados (sessão 10). **Regra:** criar as assinaturas só **depois** de receber `Open`; se chegar `UnrecognizedId` mesmo assim, refazer a sessão.
3. **Durante o carregamento do voo os dados são valores iniciais** (lat ≈ 0, lon ≈ 0): publicar snapshots só com `Sim=1` e voo carregado.

**Algoritmo do `MsfsSimAdapter` (decidido):**

1. Tentar conectar **indefinidamente**: intervalo de 2 s, crescendo até ~10 s, sem desistir (o MSFS pode levar vários minutos para abrir). Estado `Connecting`.
2. Depois de conectar, **esperar a mensagem `Open`** e só então assinar os eventos de sistema (`Sim`, `Pause`) e os grupos de variáveis.
3. Sonda a cada ~3 s: `E_FAIL` duas vezes seguidas = conexão morta. Timeout é ignorado.
4. Conexão morta: encerrar assinaturas e cliente (com limite de 5 s, pois o `Dispose` pode travar), criar um **novo** `SimConnectClient` e voltar ao passo 1. `ConnectionState` volta a `Connecting`; os streams do adapter **continuam abertos** (ver tarefa 3).
5. Publicar `AircraftState` só enquanto `Sim=1`, com todos os grupos recebidos e a posição fora do valor provisório (|lat| e |lon| menores que 0,05°; o MSFS informa esse valor por ~10 s mesmo com `Sim=1`); ao voltar a `Sim=0` (menu), manter o último estado em `LatestState` e parar de publicar até a próxima carga. Implementado em `MsfsSnapshotAssembler`.
6. Desligar o `AutoReconnectEnabled` da biblioteca, para não competir com o nosso.

Detalhes extras que afetam o desenho:

- Cada assinatura de struct recebe seus dados por callback em threads da própria biblioteca; o adapter precisa agregar os grupos num único `AircraftState` com `lock`.
- O transponder do Fenix só respondeu depois do voo carregar. Com aeronave padrão (CJ4) tudo respondeu.

## Detalhes da biblioteca que a sessão real revelou (tarefa 7)

- **Os structs de leitura precisam ser `public`** (e os campos também). A biblioteca liga o tipo genérico da requisição com `dynamic`; com um struct `internal`, a primeira assinatura falha com `RuntimeBinderException: 'object' does not contain a definition for 'OffsetBytes'`. Só o `ToData()` é interno.
- **Declare o `SimConnectDataType` de cada campo**, pelo motivo acima.
- **`TestConnectionAsync` engole a exceção** e devolve só `false`; por isso a sessão faz a sonda com uma leitura simples e classifica a exceção (`E_FAIL` pelo `HResult` ou pelo texto `-2147467259`).

## Pendências

1. Validar o adapter real no Windows com o MSFS (tarefa 10 do M1): mesmo roteiro dos cenários A e B, mais um voo completo.
2. Investigar a causa do `UnrecognizedId` em assinaturas criadas antes do `Open` (a regra de esperar o `Open` evita o problema, mas a causa não foi isolada).
3. Medir o tempo entre o `Open` e o primeiro snapshot válido com o voo carregando, para ajustar a sonda e a espera.
