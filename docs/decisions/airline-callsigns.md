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

1. **Tabela de telefonia** (`data/telephony.csv`, Data): só as empresas brasileiras em operação regular (curadoria minha, conferida pelo mantenedor), com a edição de origem. `SpokenText` passa a ler dela, no lugar do dicionário fixo. Sem a tabela, o comportamento de hoje.
2. **NLU do número do voo**: "Gol um dois três quatro", "Gol mil duzentos e trinta e quatro", "Gol doze trinta e quatro" viram `GLO1234`. O NLU já aceita a telefonia ("gol" é GLO); falta o número falado de formas diferentes.
3. **Leitura do simulador**: o MSFS tem `ATC AIRLINE` e `ATC FLIGHT NUMBER`. Se o voo tiver os dois, o indicativo é designador + número (com a telefonia da tabela); senão, a matrícula, como hoje. Sem simulador: `--callsign GLO1234` (já aceito) ou o `appsettings.json`.
4. **Cotejo e abreviação**: hoje o controle abrevia a matrícula depois do contato (as duas últimas letras). Para o voo, a regra a decidir com o mantenedor é dizer sempre o designador e o número inteiro.

## Perguntas para o mantenedor

1. "GOL" ou "GOL TRANSPORTE" na fala da Torre? (A recomendação é seguir o uso brasileiro, "Gol", e guardar o registro oficial na tabela como referência.)
2. A tabela deve cobrir só as empresas regulares brasileiras, ou também as estrangeiras que voam no Brasil (sem inventar telefonias: só entram as que estiverem no registro)?
