# Decisão: o mapa do aeródromo vem do simulador (taxiways, travessia de pista, pátio)

- **Status:** decidido em 2026-10-15. O modelo e o JSON (L1) estão no Core desde 2026-10-18; o resto ainda não (plano em `docs/ROADMAP.md`, marco M6).
- **Origem:** no voo de teste em SBCT o Solo mandou "táxi para o ponto de espera da pista UNO CINCO" sem dizer a taxiway, e a rota de Curitiba **atravessa outra pista** para chegar à 15, o que exige autorização do controle. O app não sabe disso, porque os dados de aeroportos (OurAirports) têm pistas e frequências, mas **nenhuma taxiway** (e o nosso recorte nem guarda as coordenadas das cabeceiras).

## Decisão

1. **A fonte do mapa é o próprio simulador.** O MSFS conhece as taxiways (com nome), os caminhos, os pontos de espera, os estacionamentos e as cabeceiras de cada aeroporto, inclusive de cenários de terceiros: é o que o piloto vê.
2. **Sem o mapa, o app funciona como hoje.** "O importante é dizer a pista correta": o controlador continua dizendo o ponto de espera da pista em uso, sem taxiway e sem travessia. O mapa só **acrescenta**, nunca é pré-requisito.
3. **Nada do mapa é redistribuído.** Ele é lido da instalação do usuário e guardado só no computador dele (`%LOCALAPPDATA%\Fonia\layouts\<ICAO>.json`). Isso evita a questão de licença das fontes abertas (o OpenStreetMap é ODbL, com compartilhamento igual) e dos dados do cenário. Fica registrado que o OSM (ODbL) e o apt.dat do X-Plane (licença a verificar) são fontes possíveis **no futuro**, só com o aval do mantenedor.
4. **Os testes não usam dados reais do simulador:** usam layouts sintéticos escritos à mão (um "aeroporto de Curitiba" de brinquedo: duas pistas, uma taxiway que cruza uma delas). Capturas reais ficam fora do git (`/tools/layouts/`), como as gravações de voz.

## Arquitetura

- **Core (`Fonia.Core.Airports`):** modelo `AirportLayout`, sem referência a simulador:
  - **pistas** com as duas cabeceiras (posição, rumo, largura, comprimento);
  - **grafo de táxi:** nós (posição, tipo: comum, ponto de espera, ...) e arestas (nome da taxiway, tipo: taxiway, pista, estacionamento);
  - **estacionamentos** (nome ou número, posição, tipo) e **pátios**;
  - **frequências de rádio** do aeroporto (reaproveita o `AirportFrequency`). O SimConnect as entrega no mesmo pedido de instalações: o filho `FREQUENCY` de `AIRPORT` (campos `TYPE`, `FREQUENCY` em Hz e `NAME`; tipos NONE, ATIS, MULTICOM, UNICOM, CTAF, GROUND, TOWER, CLEARANCE, APPROACH, DEPARTURE, CENTER, FSS, AWOS, ASOS, CPT, GCO, conforme o SDK). Têm `APPROACH`/`DEPARTURE`/`CENTER`, que o OurAirports não distingue, mas **não sabemos se são corretos para o Brasil**: o L0 compara com o OurAirports antes de qualquer regra de mesclagem. Vale a mesma decisão do mapa: lidas da instalação do usuário, guardadas só no computador dele.
- **Interface de capacidade** `IAirportLayoutSource` (em `Fonia.Core.Sim`), como a `IComRadioControl`: o adaptador do MSFS a implementa (`Fonia.Sim.Msfs`, a única que fala com SimConnect, princípio 2), o `MockSimAdapter` devolve layouts de teste, e o app a usa quando existe.
- **Leitura e cache em JSON** (`AirportLayoutJson`, no Core ou em `Fonia.Data`), para os testes e para não pedir ao simulador de novo a cada sessão.
- **Rota (`TaxiRouter`, Core, lógica pura):** do ponto onde a aeronave está (posição da telemetria) até o ponto de espera da pista em uso, por menor caminho no grafo, com custo alto para usar uma pista como taxiway. O resultado é a lista de taxiways ("A, depois B"), as **travessias de pista** (cada aresta de pista que a rota atravessa) e os **pontos de espera** onde parar.
- **Fraseologia (`PortuguesePhraseology`)** e **motor (`AtcEngine`):** só usam o que existir: com rota, "via taxiway A, B"; com travessia, "mantenha posição fora da pista 11"; sem layout, a frase de hoje.

## O roteador (L3, `TaxiRouter`)

`TaxiRouter.Find(layout, posição, pista)` devolve `TaxiRouteFound` (a rota) ou `NoTaxiRoute` (o motivo). Regras:

- **Origem:** o nó mais próximo da aeronave, a no máximo 150 m (`MaxDistanceToGraphMeters`); mais longe, `AircraftOffTheGraph`.
- **Destino:** o ponto de espera da cabeceira pedida = um nó `HoldShort` ao lado de um nó da pista, o mais próximo da cabeceira. O grafo não liga ponto de espera a pista; a ligação é inferida pela vizinhança. Sem nenhum, `NoHoldingPoint`.
- **Caminho:** Dijkstra pelo comprimento, com o grafo nos dois sentidos. Usar uma pista como taxiway custa 5.000 m a mais por trecho e uma taxiway sem nome, 3.000 m: prefere-se sempre dar a volta por um caminho que o controlador sabe dizer. Se o melhor caminho ainda usa taxiway sem nome, `UnnamedTaxiway`; se o grafo é cortado, `NotConnected`; sem nenhuma taxiway no layout, `NoTaxiways`; pista que o layout não tem, `UnknownRunway`.
- **Resultado:** os nós, as taxiways em ordem (cada uma uma vez seguida: "A, B"), as travessias (`RunwayCrossing`: a pista, o nó onde a rota chega nela, o ponto de espera antes dela, que pode ser desconhecido, e se a rota **segue** a pista em vez de só cruzá-la) e o comprimento. A pista em que a aeronave já está no começo não é travessia.
- **Limites conhecidos:** o ponto de espera da travessia só é conhecido quando o nó imediatamente antes da pista está marcado como `HoldShort` no grafo; senão a travessia vem com o ponto nulo (o L4 decide o que dizer, o L0 mostra o quanto isso acontece de verdade). O roteador foi escrito sobre o layout sintético e precisa ser conferido com o grafo real do simulador.

## L4a: a rota na autorização de táxi

`AirportContext.Layout` (opcional) leva o mapa ao motor; quem o preenche é o L2 (a fonte no simulador), e até lá só os testes o fazem. Quando o Solo (ou a Torre que faz o papel dele) autoriza o táxi e há mapa, posição da aeronave (`FlightContext.Aircraft`, no solo) e uma rota segura sem travessia de pista, a autorização leva `TaxiClearance.Taxiways` e a frase termina com "via taxiway ALFA, BRAVO". Em qualquer outro caso, a frase de hoje.

## Fraseologia (MCA 100-16/2025, Art. 125, II)

O manual lista como instruções complementares do táxi:

- "...para o ponto de espera da pista 10, **via taxiway "G"**, próximo do ponto de espera chame Torre 118,1";
- "via primeira interseção à direita", "via pista em uso", "siga Boeing 737 à sua frente", "taxie em frente", "taxie com cautela";
- "**mantenha posição fora da pista 02**" (o ponto de espera da travessia);
- "**reporte para cruzamento da pista 02**".

**Lacuna (`TODO(fraseologia)`):** a frase da **autorização de cruzar a pista** não aparece nesse artigo (só o "reporte para cruzamento"). Falta uma referência do mantenedor (manual, AIP-BR ou a prática da VATSIM-BR) antes de falar. O cotejamento do "mantenha posição" é parte do Art. 12 (instruções para manter-se fora da pista) e entra no mesmo critério do cotejamento por situação.

## Máquina de estados do táxi com rota

1. O piloto pede táxi. O controlador dá a rota até o **primeiro ponto de espera** (o da pista que a rota atravessa, se houver; senão, o da pista em uso).
2. O piloto chega ao ponto de espera da travessia e relata ("aguardando fora da pista 11" / "no ponto de espera da pista 11"). Hoje esse relato vira "chame a Torre": precisa distinguir **a pista da travessia** da **pista de decolagem** pelo estado da rota.
3. O controlador (Solo ou Torre, conforme o aeroporto) autoriza o cruzamento; o piloto coteja e cruza; "pista livre" encerra a travessia.
4. A rota segue até o ponto de espera da pista de decolagem, e daí o fluxo atual (passagem para a Torre, alinhar, decolar).
5. A **telemetria acompanha o progresso** (a que distância do próximo ponto de espera, se saiu da rota) e confirma o que o piloto relata, como já faz com a decolagem e o pouso.

## O que não se sabe e o spike responde (etapa L0)

- O que o **SimConnect.NET** expõe da API de instalações do SimConnect (`SimConnect_RequestFacilityData` e as definições `AIRPORT`, `RUNWAY`, `TAXI_PATH`, `TAXI_POINT`, `TAXI_NAME`, `TAXI_PARKING`): se dá para usar direto ou se é preciso chamar a biblioteca nativa, e como são os tipos e as unidades.
- **MSFS 2020 x 2024:** o campo `ident` das estruturas de instalações mudou de tamanho (já anotado em `simconnect.md`); o spike usa o 2020 e anota as diferenças.
- Quanto tempo leva pedir um aeroporto, quantos dados são (SBCT, SBGR), e se **cenários de terceiros sem taxiway** devolvem um grafo vazio (o caso do "como é hoje").
- Como identificar as **cabeceiras e os pontos de espera** (se o simulador marca o ponto de espera ou se é preciso inferi-lo na entrada da pista).

## Riscos

- **Não consigo rodar o MSFS aqui.** Cada etapa que toca o simulador (L0, L2, L5) precisa ser testada pelo mantenedor, com a saída ou o log trazidos de volta, como no M1.
- O grafo do simulador pode ter nomes ou conexões estranhos (taxiways sem nome, caminhos duplicados): o roteador precisa ser tolerante e, na dúvida, **cair no comportamento de hoje** em vez de falar uma rota errada.
- Uma rota errada falada com confiança é pior que nenhuma rota: o app só diz a taxiway quando a rota é sólida (caminho encontrado, nomes presentes).
