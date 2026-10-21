# Indicativo de companhia aérea: de onde vem a telefonia

Decisão de pesquisa de 2026-10-21, para a feature "indicativo de companhia" (designador + número do voo, no lugar da matrícula). **Ainda sem código.**

## O problema

Na aviação comercial o indicativo é o designador da empresa mais o número do voo ("GLO1234"), e a telefonia (o que se fala no rádio) segue a empresa. Hoje a tabela `Telephony` do `SpokenText` tem só `FAB`, `GLO` e `TAM`. Preencher o resto de memória seria inventar, e o projeto não faz isso.

## Fontes avaliadas

| Fonte | Conteúdo | Licença / acesso | Veredito |
|---|---|---|---|
| **ICAO Doc 8585** (Designators for Aircraft Operating Agencies, Aeronautical Authorities and Services) | A fonte oficial: designador de três letras, telefonia e empresa. Edições trimestrais (jan, abr, jul, out) | Publicação paga da ICAO; os dados não podem ir ao repositório | Fonte de referência, não de cópia |
| **FAA JO 7340.2 ("Contractions"), capítulo 3** | Reproduz os designadores de três letras e as telefonias das empresas do mundo todo (empresa, país, telefonia, designador) | Publicação do governo dos EUA, domínio público; PDF aberto em faa.gov. Os acréscimos estrangeiros saem em avisos mensais ("foreign ICAO 3LTD additions") | **Escolhida** |
| OpenFlights `airlines.dat` | Empresas com IATA/ICAO/callsign | ODbL (copyleft de dados: exige repassar sob a mesma licença) e dados da Wikipédia | Descartada: licença incompatível com o princípio 6 sem aviso |
| ANAC | Só a designação de empresas para voar ao exterior; não publica a lista de telefonias | — | Sem a lista |

A edição usada: **JO 7340.2N com as mudanças 1 a 3 (17/04/2025)**, <https://www.faa.gov/documentLibrary/media/Order/7340.2N_Bsc_w_Chg_1_Chg_2_and_Chg_3_dtd_4_17_25_FINAL.pdf>. O capítulo 3 tem 193 linhas com "BRAZIL"; extraí 69 designadores brasileiros (a extração do PDF é desalinhada em algumas linhas, então a tabela final precisa de conferência à mão).

## O que a fonte mostrou

- **A telefonia registrada de GLO é "GOL TRANSPORTE"**, não "GOL". O `SpokenText` fala "GOL". No rádio brasileiro o uso é "Gol"; o registro é "GOL TRANSPORTE". **Decisão do mantenedor: qual das duas a Torre fala?**
- Outras telefonias registradas, conferidas no PDF: `AZU` "AZUL", `ONE` "OCEANAIR", `PTB` "PASSAREDO", `ACN` "AZUL CONECTA", `TTL` "TOTAL", `SID` "SIDERAL", `TAM` "TAM" (o nome da LATAM Brasil na telefonia), `MLI` "DEMAIS", `ABJ` "ABAETE".
- O registro tem entradas velhas ou estranhas (`LTG` "TAMCARGO" aparece ligada a outra empresa; empresas que já pararam). Não dá para copiar tudo sem curadoria.
- A atualização é contínua (avisos mensais), então a tabela do Fonia fica datada e precisa de data e edição de origem no próprio arquivo.

## Plano proposto (tarefas pequenas, uma branch cada)

1. **Tabela de telefonia** (`data/telephony.csv`, embutida no Core): empresas brasileiras e estrangeiras que voam no Brasil, com a edição de origem. `SpokenText` lê dela, no lugar do dicionário fixo. (Feita.)
2. **NLU do número do voo**: "Gol um dois três quatro", "Gol mil duzentos e trinta e quatro", "Gol doze trinta e quatro" viram `GLO1234`. O NLU já aceita a telefonia ("gol" é GLO); falta o número falado de formas diferentes.
3. **Leitura do simulador**: o MSFS tem `ATC AIRLINE` e `ATC FLIGHT NUMBER`. Se o voo tiver os dois, o indicativo é designador + número (com a telefonia da tabela); senão, a matrícula, como hoje. Sem simulador: `--callsign GLO1234` (já aceito) ou o `appsettings.json`.
4. **Cotejo e abreviação**: hoje o controle abrevia a matrícula depois do contato (as duas últimas letras). Para o voo, a regra a decidir com o mantenedor é dizer sempre o designador e o número inteiro.

## Decisões do mantenedor (2026-10-21)

1. A Torre fala **"Gol"**, não "GOL TRANSPORTE". O registro oficial fica na coluna `registered` de `data/telephony.csv`.
2. A tabela cobre as empresas brasileiras e as **estrangeiras que voam no Brasil**. Só entram as que estão no registro da FAA, com a telefonia lida do PDF (não de memória). Designadores em que as seções do registro divergem (`JES`, `FBZ`, `CLX`, `GTI`, `DHK`, `ABJ`) ficaram de fora até conferência.

## Tarefa 1 (feita): `data/telephony.csv`

`data/telephony.csv` (colunas `designator,spoken,registered,country,source,notes`) é embutido no `Fonia.Core` (`Telephony`) e lido pelo `SpokenText`: o indicativo "GLO1234" é dito "GOL UNO DOIS TRÊS QUATRO", e o NLU lê a telefonia falada, inclusive com mais de uma palavra ("air canada", "lan cargo"). A telefonia de FAB continua a do manual. Os nomes em inglês ou outras línguas ("AIRFRANS", "SPEEDBIRD") saem como o registro os escreve; como o TTS os pronuncia só se valida de ouvido. Atualizar a tabela é editar o CSV (a FAA publica os acréscimos todo mês).

## Pedido do mantenedor, ainda sem feature: estrangeiras em inglês

"Qualquer companhia aérea estrangeira o controle deve falar em inglês." Hoje só o ATIS tem inglês (`AtisEnglish`); o Solo e a Torre falam só português (`PortuguesePhraseology`) e não há fraseologia de controle em inglês. É uma **feature própria**: uma `EnglishPhraseology` (ICAO Doc 4444 e a prática do DECEA em inglês, nunca inventada, com fonte e `TODO(fraseologia)` onde faltar), a escolha da língua pelo voo (a coluna `country` de `data/telephony.csv` já separa Brasil do resto; falta decidir o caso de empresa brasileira que fala inglês e o de piloto estrangeiro de matrícula estrangeira), o NLU do piloto em inglês (o STT do Whisper `small` é multilíngue) e a voz do TTS em inglês. Entra no roadmap depois das tarefas 2 a 4 do indicativo.
