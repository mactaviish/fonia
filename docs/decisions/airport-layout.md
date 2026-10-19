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

## Resultados do L0a (2026-10-18): o que o MSFS 2020 entrega

O comando `fonia --dump-layout <ICAO>` (com o MSFS 2020 aberto e um voo carregado) pede o aeroporto ao simulador, grava o que veio em `%LOCALAPPDATA%\Fonia\layouts\raw\<ICAO>.raw.json` (fora do git) e mostra um resumo. O código está em `Fonia.Sim.Msfs` (`MsfsFacilityReader`, com P/Invoke próprio para as duas funções que o SimConnect.NET 0.2.2 não expõe) e em `Fonia.App` (`LayoutCommands`). Rodado em SBCT, SBGR, SBGO, SBPS, SBIH e SBSG:

- **Funciona de primeira e é rápido:** ~0,3 s por aeroporto, todos os pedidos terminam, e as contagens recebidas batem com as que o simulador diz ter.
- **Eixos dos pontos de táxi confirmados:** `BIAS_X` é leste e `BIAS_Z` é norte, a partir do ponto de referência do aeroporto (erro médio de 2 a 3 m contra as pistas, em todos). O comando refaz essa conferência a cada execução.
- **Tipos de caminho:** as taxiways vêm como **tipo 4** ("Path"), e não há nenhuma de tipo 1 ("Taxi") nos aeroportos vistos. Tipo 2 é pista, 3 é estacionamento e 6 é veículo (fora do táxi de aeronave). `RUNWAY_NUMBER` e `RUNWAY_DESIGNATOR` só valem nos caminhos de tipo 2; nos outros vêm com valores sem sentido e devem ser ignorados.
- **Pontos de espera:** tipo 5 ("hold short sem desenho") na maioria e alguns de tipo 2. Ficam **80 a 130 m do eixo da pista e nunca ao lado de um nó de pista no grafo** (0 em 4 aeroportos). A suposição do `TaxiRouter` (L3) de que o ponto de espera é vizinho do nó da pista **não vale nos dados reais**: o L0b precisa ligar cada ponto de espera à pista por geometria (a pista mais próxima) e guardar isso no modelo. O grafo de cada aeroporto é um único componente conectado.
- **Nomes de taxiway são o ponto fraco:** SBGR tem 24 taxiways e 71% dos caminhos de táxi com nome (703 de 986); SBGO, SBIH e SBSG têm de um terço a 40% e poucas letras; SBCT tem 25% (só "A" e "B"); SBPS não tem nenhum. Onde faltam nomes o roteador já devolve "sem rota" (nunca inventa), e o L4a diz a taxiway só quando a rota inteira tem nome: então **a rota por taxiway vai funcionar bem em alguns aeroportos (SBGR) e quase nunca em outros**, e o comportamento de hoje continua o padrão.
- **Frequências:** o simulador distingue tipos que o OurAirports não distingue (vários `APPROACH`, `CLEARANCE`, `GROUND` e `TOWER` múltiplos). Em SBGO, SBPS, SBIH e SBSG ele coincide com o que o mantenedor viu na AISWEB e corrige o OurAirports (Torre errada em SBPS e SBIH, Solo, Tráfego e ATIS faltando); em SBGR traz o Solo que o mantenedor ouve no ar e o OurAirports não tem. Em SBCT discorda do OurAirports na Torre (118,15 no simulador, 118,55 no OurAirports), e **o mantenedor confirmou que a certa é 118,15** (a outra frequência da Torre, 121,5, é a de emergência). Com isso a frequência do simulador acertou em todos os aeroportos conferidos e é a melhor fonte, com o OurAirports de base; a regra de mesclagem (e o que fazer com os muitos setores de `APPROACH` do mesmo Controle) fica para o L2.
- **Não testado:** MSFS 2024 (o tamanho do campo `ident` das instalações mudou lá); cenário de terceiros.

## L0b (2026-10-18): do dump ao mapa, e quantas rotas o mapa real dá

`AirportLayoutConverter` (em `Fonia.Sim.Msfs`) faz o `AirportLayout` a partir do dump, e o `fonia --dump-layout` passa a gravar também `%LOCALAPPDATA%\Fonia\layouts\<ICAO>.json` (o mapa) e a mostrar um **teste de rotas** (de cada estacionamento até o ponto de espera de cada cabeceira, pelo `TaxiRouter`). Regras da conversão, todas tiradas dos dados reais:

- Os pontos de táxi viram nós com o índice do simulador; os estacionamentos, nós à parte (1.000.000 + índice), ligados pelo caminho de estacionamento (tipo 3, em que `START` é o ponto de táxi e `END` é a vaga). Caminhos de tipo 4 (e 1) viram taxiways, com o nome quando há; tipo 2 vira trecho de pista, da pista dada por número e lado; veículo, estrada, fechado e linha pintada ficam de fora (e contados nas notas).
- A cabeceira primária fica **atrás** do centro, no começo da corrida (`HEADING` é o rumo da primária); a de menor número vai primeiro no nome da pista ("15/33").
- Cada ponto de espera é ligado à **pista mais próxima** (até 250 m; mais longe, a nenhuma). O `TaxiNode` ganhou `Runway`, o `TaxiRouter` escolhe o ponto de espera de uma cabeceira entre os ligados à pista (o mais perto da cabeceira) e o ponto de parada antes de uma travessia é o último ponto de espera ligado àquela pista, olhando para trás pela rota.
- As frequências viram as do app: `APPROACH` e `DEPARTURE` são o Controle, `CLEARANCE` é o Tráfego, `UNICOM`/`MULTICOM`/`CTAF` são Rádio.

**Os nomes que faltam, e a regra nova do roteador.** Recusar qualquer trecho de taxiway sem nome dava **0 rotas até em Guarulhos**: 29% dos caminhos de SBGR não têm nome, mas são sobretudo conectores curtos de interseção (mediana de 21 m) e o pátio. Na rota típica de SBGR só 8 a 10% do comprimento é sem nome (p90 de 37 a 44%), e o maior trecho seguido sem nome tem 65 m de mediana (p90 268 m); em SBCT a rota típica tem 50 a 60% sem nome. Por isso o roteador passou a **atravessar os trechos sem nome e dizer só as taxiways com nome**, e a recusar ("taxiway sem nome") quando o nome falta demais: pelo menos uma taxiway com nome, no máximo **40%** do comprimento de taxiway sem nome (`MaxUnnamedShare`) e nenhum trecho seguido sem nome com mais de **300 m** (`MaxUnnamedRunMeters`). Os dois valores são julgamento, escolhidos com esses números; ajustar é trocar uma constante.

Resultado do teste de rotas no MSFS 2020 (estacionamentos ligados até cada cabeceira):

| Aeroporto | Com rota | Sem rota |
|---|---|---|
| SBGR | 86 de 99 (as duas cabeceiras de cada pista) | 10 taxiway sem nome, 3 estacionamentos isolados |
| SBGO | 39 e 42 de 62 | ~20 taxiway sem nome |
| SBSG | 25 e 26 de 59 | ~30 taxiway sem nome |
| SBCT | 22 e 19 de 40 (cabeceiras 15 e 33); 0 nas 11 e 29 | taxiway sem nome |
| SBPS | 0 de 20 | nenhuma taxiway tem nome |

As rotas fazem sentido com o que o mantenedor descreveu: em SBGR a rota até a pista de pouso 10R atravessa a 10L, e em SBCT a rota até a 15 atravessa uma pista. **O mapa serve onde o cenário tem nomes** (SBGR é o melhor caso visto) e quase nunca onde não tem; o comportamento sem mapa continua o padrão.

## O que não se sabia e o spike respondeu (etapa L0)

- O que o **SimConnect.NET** expõe da API de instalações do SimConnect (`SimConnect_RequestFacilityData` e as definições `AIRPORT`, `RUNWAY`, `TAXI_PATH`, `TAXI_POINT`, `TAXI_NAME`, `TAXI_PARKING`): se dá para usar direto ou se é preciso chamar a biblioteca nativa, e como são os tipos e as unidades.
- **MSFS 2020 x 2024:** o campo `ident` das estruturas de instalações mudou de tamanho (já anotado em `simconnect.md`); o spike usa o 2020 e anota as diferenças.
- Quanto tempo leva pedir um aeroporto, quantos dados são (SBCT, SBGR), e se **cenários de terceiros sem taxiway** devolvem um grafo vazio (o caso do "como é hoje").
- Como identificar as **cabeceiras e os pontos de espera** (se o simulador marca o ponto de espera ou se é preciso inferi-lo na entrada da pista).

## Riscos

- **Não consigo rodar o MSFS aqui.** Cada etapa que toca o simulador (L0, L2, L5) precisa ser testada pelo mantenedor, com a saída ou o log trazidos de volta, como no M1.
- O grafo do simulador pode ter nomes ou conexões estranhos (taxiways sem nome, caminhos duplicados): o roteador precisa ser tolerante e, na dúvida, **cair no comportamento de hoje** em vez de falar uma rota errada.
- Uma rota errada falada com confiança é pior que nenhuma rota: o app só diz a taxiway quando a rota é sólida (caminho encontrado, nomes presentes).
