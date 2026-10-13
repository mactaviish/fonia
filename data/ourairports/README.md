# Dados do OurAirports (recorte do Brasil)

Aeroportos, pistas e frequências de rádio do Brasil, do [OurAirports](https://ourairports.com/data/), embutidos no app para ele funcionar offline.

- **Licença:** domínio público. O OurAirports não garante a exatidão dos dados e **pede crédito**, sem exigir; o crédito está no README do projeto. Não use estes dados para navegação real.
- **Origem e data do recorte:** [`snapshot.txt`](snapshot.txt).
- **O que fica:** aeroportos com `iso_country` BR do tipo grande, médio ou pequeno (sem heliportos e sem aeródromos fechados), e as pistas e frequências deles, só com as colunas que o app lê.

| Arquivo | Conteúdo |
|---|---|
| `br-airports.csv` | aeroportos (código, nome, tipo, posição, elevação, município, códigos ICAO/IATA) |
| `br-runways.csv` | pistas, com as duas cabeceiras, comprimento, superfície, iluminação e se está fechada |
| `br-airport-frequencies.csv` | frequências (Solo, Torre, Aproximação, ATIS, AFIS, ...) em MHz |

## Como atualizar

Na raiz do repositório:

```bash
dotnet run tools/update-ourairports.cs
```

O programa baixa os três arquivos do GitHub do OurAirports, aplica o filtro acima e regrava os CSVs ordenados por código (uma atualização aparece no git como um diff legível). Para usar arquivos que você já baixou: `dotnet run tools/update-ourairports.cs -- --from <pasta>`.

Depois de atualizar, rode os testes: `EmbeddedDataTests` confere que nada se perdeu (aeroportos controlados, faixa de frequências, coordenadas dentro do Brasil) e que todo aeroporto com Solo ou Torre continua tendo um nome em [`../atc-names.csv`](../atc-names.csv).

## Limites dos dados

- **Sem taxiways.** O OurAirports não traz a malha de táxi; por isso as instruções de táxi do M2 vão "para o ponto de espera da pista X", sem rota.
- **O rumo da pista quase nunca vem** (97% das pistas do Brasil). O app deriva do número da pista ("15" → ~150°, já magnético).
- **O município erra o nome falado** dos aeroportos mais usados; esse nome vem de [`../atc-names.csv`](../atc-names.csv).
