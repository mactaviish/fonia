# Decisão: como falar com o SimConnect (M1)

- **Status:** proposta aprovada em 2026-10-12; **a confirmar no spike** (tarefa 2 do M1).
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

## Pendências a confirmar no spike

1. SimConnect.NET 0.2.x conecta e lê as variáveis em .NET 10 (a biblioteca declara `net8.0`/`net9.0`).
2. Unidades e tipos reais das variáveis (principalmente frequência COM, 8,33 kHz e código do transponder).
3. Taxa de atualização e consumo de CPU aceitáveis (o app roda junto com o simulador).
4. Como a biblioteca sinaliza o fechamento do MSFS (necessário para a reconexão).
