# Decisão: como falar com o SimConnect (M1)

- **Status:** proposta aprovada em 2026-10-12; spike rodado em 2026-10-12 (resultados abaixo). **Falta** validar a reconexão com o MSFS fechando e abrindo.
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
| Posição, altitude, velocidade, proa, em solo | Lidos com as unidades `degrees`, `feet`, `knots`, `degrees`, `bool`. Valores coerentes depois de carregar o voo. |
| Matrícula | `ATC ID` devolve a matrícula (ex.: `PR-FRA`). Ela pode **mudar logo após o voo carregar** (apareceu antes um valor de livery); o adapter não deve guardá-la uma vez só. |
| Tipo da aeronave | `ATC TYPE` devolve o fabricante (`Airbus`). `ATC MODEL` devolveu uma **chave de tradução** (`ATCCOM.AC_MODEL_A320.0.text`), não `A320`. Plano: extrair o trecho após `AC_MODEL_` e, se não houver, usar o texto bruto. |
| Frequências COM | **Usar a unidade `Hz`.** Em `Hz` os quatro valores batem exatamente com a leitura escalar (ex.: `122800000` Hz = 122,800 MHz = 122800 kHz), o que combina com `Frequency` em kHz inteiros. Em `MHz`, pelo caminho de struct da biblioteca, **COM1 veio errado** (122,000 em vez de 122,800; 124,000 em vez de 124,850) enquanto COM2 veio certo; a leitura escalar em `MHz` estava correta. Causa não investigada (suspeita: biblioteca/definição de struct); evitar `MHz` em struct. |
| Transponder | `TRANSPONDER CODE:1` em `bco16` devolveu `8192` (`0x2000` = squawk 2000), em `number` devolveu `2000`. **Plano:** usar `bco16` (unidade documentada) e decodificar os dígitos em hexadecimal. Confirmar com outro squawk (ex.: 1234) antes de fechar. O estado (`TRANSPONDER STATE:1`) veio 0. |
| Voo carregado ou não | Antes do voo carregar, os dados eram valores iniciais (lat ≈ 0,0004, lon ≈ 0,014, 9 ft). O evento de sistema `Sim` (dado 1 = simulação ativa) e `SIM DISABLED` indicam o estado. **O adapter não deve publicar snapshots até o voo estar ativo.** |
| Eventos de sistema | `Sim`, `SimStart`, `SimStop`, `Pause` e `FlightLoaded` foram aceitos; `Sim` e `Pause` entregam o valor atual logo na assinatura. |
| Elevação | Spike sem administrador conectou normalmente ao MSFS em execução. |

Detalhes extras que afetam o desenho:

- Cada assinatura de struct recebe seus dados por callback em threads da própria biblioteca; o adapter precisa agregar os grupos num único `AircraftState` com `lock`.
- O Fenix A320 pode não expor todas as variáveis padrão (o transponder só passou a responder depois do voo carregar). Vale validar também com uma aeronave padrão.

## Pendências

1. **Reconexão** (tarefa 8): como a biblioteca sinaliza o fechamento do MSFS e se as assinaturas sobrevivem quando ele reabre. A biblioteca tem `AutoReconnectEnabled` (padrão de 3 tentativas, `ReconnectDelay` configurável), ainda não testado: exige fechar e abrir o MSFS com o spike rodando.
2. Confirmar a decodificação do transponder com outro squawk e o comportamento em movimento (taxi).
3. Repetir a leitura com uma aeronave padrão do MSFS.
