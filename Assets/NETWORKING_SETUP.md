# Gamma Six - Configuración inicial de multijugador

## Paquetes requeridos

Instala desde Unity Package Manager:

- Netcode for GameObjects (`com.unity.netcode.gameobjects`)
- Unity Transport (`com.unity.transport`), normalmente instalado como dependencia
- Multiplayer Tools, opcional para pruebas y métricas

La implementación actual usa conexión directa por IP y puerto. No requiere Relay todavía.

## Escenas

Confirma que estas escenas estén incluidas en el Build Profile y con exactamente estos nombres:

1. Bootstrap
2. MainMenu
3. Lobby
4. GameScene

Siempre inicia el juego desde `Bootstrap`.

## Prueba local en un computador

1. Abre una instancia desde Unity y otra mediante un build del juego.
2. En la primera: Multijugador > Escaramuza > Iniciar Host.
3. Usa el puerto 7777.
4. En la segunda: Multijugador > Buscar partida.
5. Dirección: 127.0.0.1, puerto: 7777.
6. Al conectarse, ambos deben aparecer en la lista.
7. El host confirma el mapa y pulsa Iniciar partida.

## Prueba en dos computadores de la misma red

- El host debe entregar su IPv4 local, por ejemplo `192.168.1.25`.
- El cliente ingresa esa IP y el mismo puerto.
- Windows Firewall puede solicitar permiso para permitir la conexión.

## Flujo implementado

- Creación programática de `NetworkManager` y `UnityTransport` desde Bootstrap.
- Inicio Host/Cliente por conexión directa.
- Nombre persistente con `PlayerPrefs`.
- Lista de jugadores sincronizada mediante mensajes personalizados de Netcode.
- Equipo automático por jugador.
- Estado Listo sincronizado.
- Selección de mapa controlada por el host.
- Carga de `GameScene` sincronizada mediante Network Scene Management.
- Conservación del modo individual existente.

## Próximo paso recomendado

Crear la primera unidad de prueba con `NetworkObject` y movimiento autoritativo por órdenes. El cliente enviará el destino y el servidor validará propiedad, ejecutará el movimiento y sincronizará la posición.

## Fix 03 — ciclo de vida de sesiones y autoridad del host

- Al cerrar el host, cualquier callback de desconexión en los clientes invalida la sesión completa.
- Los handlers de mensajes personalizados se eliminan antes de `NetworkManager.Shutdown()`.
- Una conexión nueva vuelve a registrar handlers sobre el nuevo `CustomMessagingManager`.
- El mapa se sincroniza con un mensaje separado de la orden de iniciar partida.
- Solo el host puede ver y utilizar el selector de mapas y el botón para iniciar.
- Los clientes solo ven el nombre/ID del mapa elegido por el host.

## Prototipo de unidades RTS

La escena de partida crea automáticamente una unidad por jugador conectado.

Controles:

- Clic izquierdo sobre tu cápsula: seleccionar unidad.
- Clic derecho sobre el terreno: enviar orden de movimiento.
- Solo puedes seleccionar y ordenar unidades cuyo `OwnerClientId` corresponde a tu cliente.
- El servidor valida la propiedad y mantiene la posición verdadera.

En esta etapa las unidades son representaciones locales sincronizadas mediante snapshots. Esto permite validar autoridad, selección y órdenes antes de crear los prefabs definitivos con `NetworkObject`, NavMesh, animaciones y formaciones.

## Ubicación de mapas

Los mapas se leen como archivos `.json` desde:

`Application.persistentDataPath/GameContent/Scenarios`

En Windows normalmente corresponde a:

`C:\Users\<usuario>\AppData\LocalLow\<CompanyName>\<ProductName>\GameContent\Scenarios`

La ruta exacta se imprime en la consola con el prefijo `[GameContentRepository]`.


## Lobby: equipos, colores y jugadores listos

- La sesión admite hasta 8 jugadores, limitada además por `maxPlayers` del escenario seleccionado.
- El host recibe Equipo 1; los siguientes jugadores reciben Equipos 2, 3 y 4.
- Cada jugador recibe un color único de la paleta: rojo, azul, amarillo, verde, morado, naranja, café, celeste y rosa.
- Los jugadores pueden cambiar su propio color cuando `Colores fijos` está desactivado.
- Los clientes solo pueden elegir colores libres.
- El host puede cambiar el color de cualquier jugador; si el color está ocupado, se intercambian ambos colores.
- Con `Colores fijos` activo, solo el host puede modificar colores.
- El botón de iniciar partida permanece bloqueado hasta que todos los jugadores estén listos.
- El color elegido se transmite a la escena y se aplica a las unidades iniciales.


## Límite de jugadores y asignación de equipos

- La sesión admite hasta 8 jugadores conectados.
- Existen 4 equipos disponibles.
- La asignación automática equilibra los equipos en el orden 1, 2, 3, 4 y vuelve a comenzar.
- Con 8 jugadores, cada equipo recibe 2 jugadores.
- Si un jugador se desconecta, el siguiente jugador ocupa primero el equipo con menos integrantes.

## Controles RTS y cámara (prototipo 02)

- `Tab`: alterna entre órdenes RTS por clic y control directo WASD.
- Clic izquierdo: selecciona una unidad propia.
- Clic izquierdo + arrastre: selección rectangular, máximo 50 unidades propias.
- `Shift` + clic/arrastre: invierte la selección de cada unidad encontrada.
- Doble clic: selecciona las unidades propias visibles del mismo tipo.
- `Ctrl + 1`, `Ctrl + 2`, `Ctrl + 3`: guarda grupos de control.
- `1`, `2`, `3`: recupera grupos de control.
- Clic derecho: mueve las unidades seleccionadas en formación simple.
- Flechas: desplazan la cámara cuando está desbloqueada.
- Rueda presionada + arrastre: desplaza la cámara con inercia.
- `Alt + R`: bloquea/desbloquea la cámara sobre una unidad seleccionada con `CameraDriver`.
- Mantener `Alt`: orbita la cámara mientras está bloqueada.
- Doble pulsación de `Alt`: activa/desactiva la órbita libre con el mouse.
- WASD: mueve directamente la unidad conductora seleccionada cuando está activo el modo DirectWASD.

La selección y los grupos son locales. Las órdenes de movimiento siguen siendo validadas por el servidor.

## Ajuste de cámara y selección 02

- Se eliminó la alternancia de modo mediante Tab.
- La cámara libre usa selección y órdenes RTS por clic.
- Alt + R fija/libera la cámara sobre una unidad CameraDriver.
- Mientras la cámara está fijada, WASD controla la unidad designada.
- Al fijar la cámara, el cursor queda bloqueado y oculto; al liberar vuelve a mostrarse.
- El halo de selección se posiciona encima del suelo y usa un material compatible con URP.
- Los eventos Alt usados por el control de cámara se consumen en la ventana de juego para reducir los avisos sonoros de Windows.

## Sistema de carga de contenido

GammaSix ahora distingue dos tipos de contenido:

- `Scenario`: una partida jugable con límites, puntos de aparición, entidades, misiones y overrides.
- `Campaign`: una secuencia de pasos. Los pasos soportados por el modelo son `scenario`, `video` y futuros tipos personalizados. En esta primera versión, iniciar una campaña resuelve y carga su primer paso de tipo `scenario`.

Las carpetas de usuario son:

```text
Application.persistentDataPath/GameContent/Scenarios
Application.persistentDataPath/GameContent/Campaigns
```

También se mantiene compatibilidad con la carpeta antigua:

```text
Application.persistentDataPath/Maps
```

Los ejemplos incluidos en `StreamingAssets/GameContent` se copian automáticamente a las carpetas del usuario cuando no existen.

### Overrides

Cada escenario puede declarar `settingOverrides`. El lobby muestra estas reglas con un indicador de override. El host puede desmarcar cada override para cancelarlo. Mientras un override esté activo, la configuración normal correspondiente queda bloqueada y prevalece el valor del escenario.

Ejemplo:

```json
"settingOverrides": [
  {
    "key": "fixedColors",
    "displayName": "Colores fijos",
    "value": "true",
    "enabled": true
  }
]
```

En esta versión, `fixedColors` ya está conectado a la configuración real del lobby. Las demás claves se muestran y sincronizan, quedando listas para conectarse cuando se implemente su sistema correspondiente.

## Equipos fijos y atributos de entidades

Los escenarios pueden bloquear el cambio de equipos con:

```json
"fixedTeams": true
```

Cuando está activo, ningún jugador ni el host puede cambiar los equipos desde el lobby.

Las entidades del escenario pueden declarar atributos:

```json
{
  "id": "soldado_equipo_1",
  "typeId": "soldado",
  "teamId": 1,
  "ownerTeamSlot": 1,
  "cameraDriver": true,
  "attributes": [
    "entity",
    "entity.unit",
    "unit.soldier",
    "movement.ground",
    "interaction.selectable",
    "interaction.controllable"
  ]
}
```

El código del sistema está separado en:

```text
Scripts/Game/Entities/Attributes/
```

`EntityAttributeResolver` combina los atributos de la definición con los atributos adicionales declarados en la instancia del escenario y aplica dependencias. La selección exige `interaction.selectable` y el movimiento autoritativo exige `interaction.controllable`.

## Atributo heroic y cámara en tercera persona

- `unit.heroic`: asciende un humanoide a entidad heroica.
- `camera.third-person`: autoriza el bloqueo de cámara y el control en tercera persona con `Alt + R`.
- `EntityAttributeResolver` deriva automáticamente `camera.third-person` cuando encuentra `unit.heroic`.
- No existe compatibilidad con `unit.hero`, tipos `hero` ni `camera.driver`.

## Lobby y configuraciones

- La tabla secundaria de overrides fue retirada del lobby.
- `Colores fijos` y `Equipos fijos` son los únicos controles visibles de esas reglas.
- Los valores del escenario se precargan en esos controles.
- El host puede modificar `Colores fijos`; al hacerlo se cancela su override activo.
- `fixedTeams: true` en el escenario bloquea la edición de equipos.
- Los clientes reconstruyen las casillas cuando reciben `ScenarioMaxPlayers` del host.


## Modelo Humanoid + Heroic

- `humanoid` es el tipo base para las unidades humanas.
- `Soldado` es el nombre visible de la entidad, no un tipo técnico.
- `unit.heroic` asciende una unidad humanoid a heroica.
- Toda unidad con `unit.heroic` recibe automáticamente `camera.third-person`.
- `unit.hero` y `unit.soldier` se aceptan únicamente para migrar escenarios antiguos.
- Los antiguos typeId `soldado`, `heroe`, `hero` y `prototype_unit` se normalizan a `humanoid`.

## Modelo estricto de unidades humanoides

- Las unidades básicas usan exclusivamente `typeId: "humanoid"`.
- `name` contiene el nombre visible, por ejemplo `Soldado`.
- Una unidad se vuelve heroica únicamente mediante el atributo `unit.heroic`.
- `unit.heroic` añade automáticamente `camera.third-person`.
- Se eliminó toda migración de `soldado`, `heroe`, `hero`, `prototype_unit`, `unit.hero` y `unit.soldier`.
- Se eliminó por completo `cameraDriver` / `camera.driver` del contenido, runtime y snapshots.
- Un escenario con un tipo distinto de `humanoid` genera un error explícito en lugar de migrarse silenciosamente.

## Catálogo de entidades por ID

Las instancias de escenario usan `entityId` y cargan su definición desde:

- `GameContent/Entities/unit.humanoid.default.json`
- `GameContent/Entities/building.mercenary.json`
- `GameContent/Entities/building.aura.json`

El equipo neutral usa `teamId: 0`, color gris claro y no ocupa una casilla del lobby.
Las entidades enemigas pueden seleccionarse para inspección, con halo amarillo, pero el servidor solo acepta órdenes del propietario.

El escenario `scenario_3v3_buildings` incluye seis soldados, un edificio de mercenarios del equipo 1 y un círculo de aura neutral.

## HUD de partida y recursos por escenario

- `TeamSetup` ya no contiene oro inicial.
- Cada escenario puede definir recursos por equipo mediante `teamResources`.
- Si un equipo no tiene una entrada de recursos, su oro inicial es `0`.
- El HUD se encuentra en `Resources/UI/GameHud` y su controlador en `Scripts/UI/Uxml/Game`.
- La parte superior muestra el oro del equipo local.
- La parte inferior muestra nombre, equipo, vida, definición y atributos de la entidad seleccionada.

Ejemplo:

```json
"teamResources": [
  { "teamId": 1, "gold": 500 },
  { "teamId": 2, "gold": 500 }
]
```

## HUD modular y reposicionable

El HUD de partida está separado en dos módulos independientes:

- `GameGoldHudController` + `GoldHud.uxml` + `GoldHud.uss`
- `SelectedEntityHudController` + `SelectedEntityHud.uxml` + `SelectedEntityHud.uss`

Cuando el modo de edición del HUD está desbloqueado, arrastra cualquier panel con clic izquierdo. La posición se guarda mediante `PlayerPrefs` y el sistema mantiene al menos la mitad del panel visible dentro de la pantalla.

## HUD runtime: PanelSettings y edición

Los módulos del HUD cargan `GameHudPanelSettings.asset` desde `Resources/UI/GameHud`.
La plantilla conserva el Theme Style Sheet de UI Toolkit y evita el aviso `No Theme Style Sheet set to PanelSettings`.

El movimiento del HUD ya no requiere Control. Se controla mediante:

```csharp
GameHudController.Instance.SetHudEditingUnlocked(true);  // clic + arrastre
GameHudController.Instance.SetHudEditingUnlocked(false); // paneles bloqueados
```

`GameHudController` inicia con `hudEditingUnlocked = true` para facilitar las pruebas. Este booleano puede conectarse más adelante a un Toggle del menú de opciones.

## Estado RTS de cámara y retorno desde tercera persona

`RTSCameraController` mantiene el estado de la cámara RTS separado del modo de tercera persona.

Valores iniciales configurables:
- `initialRtsPosition`: posición de inicio RTS.
- `initialRtsEulerAngles`: ángulo de inicio RTS.
- `returnToRtsSmoothness`: velocidad de la transición de regreso.

Al entrar en tercera persona se guarda el transform RTS actual. Al salir mediante Alt+R, se restaura suavemente la posición y rotación guardadas.

## Retorno de tercera persona centrado en la entidad

Al salir de tercera persona con `Alt + R`, la cámara conserva la altura y la
rotación del último estado RTS, pero calcula una nueva posición para que el
centro de la vista quede sobre la entidad que estaba siguiendo. De esta forma
el jugador continúa observando la misma zona del mapa.

## Selección extendida e inspección con Tab

- Las entidades con `unit.heroic` se muestran individualmente.
- Las demás entidades seleccionadas se agrupan por `EntityDefinitionId`.
- El representante de cada grupo es la entidad con mayor vida actual.
- `Tab` alterna el grupo cuyas estadísticas muestra el inspector.
- El visor `SelectedEntitiesExtendedHud` admite hasta 30 grupos, distribuidos en 3 filas de 10.
- Durante la cámara en tercera persona, la entidad controlada no puede deseleccionarse al hacer clic en el terreno o en otra entidad.

## Modos de cursor en tercera persona

La cámara fijada a una entidad posee dos estados internos:

- **Tercera persona bloqueada**: cursor oculto y capturado. El movimiento del mouse controla la cámara y no se procesan selecciones RTS.
- **Tercera persona desbloqueada**: cursor visible y libre. La cámara continúa siguiendo a la entidad controlada y el jugador puede seleccionar otras entidades. Mantener Alt permite orbitar temporalmente.

Controles:

- `Alt + R`: entrar o salir de tercera persona.
- Doble pulsación de `Alt`: alternar cursor bloqueado/desbloqueado sin salir de tercera persona.

La entidad controlada permanece seleccionada en ambos estados.

## Corrección: interacción HUD y selección propia homogénea

- Los paneles HUD registran su geometría en `HudInteractionService`.
- Un clic o arrastre iniciado sobre un panel no inicia selección RTS ni dibuja el rectángulo verde.
- Si el cursor entra en un panel durante un arrastre de selección, el gesto se cancela.
- Una selección múltiple de entidades propias no puede mezclarse ni reemplazarse con entidades neutrales, enemigas o pertenecientes a otro jugador.
- `Shift + clic` sobre una entidad ajena se ignora cuando existe una selección múltiple propia.
- `SelectedEntitiesExtendedHud` muestra solamente entidades propiedad del cliente local.
- Las entidades ajenas todavía pueden seleccionarse individualmente para inspección cuando no existe una selección múltiple propia.

## Refacción estructural – fase 1

La coordinación de entidades fue dividida en servicios de input, selección, spawn, movimiento, vistas y DTO de red. El coordinador principal ahora se llama `NetworkEntityCoordinator`. Consulta `ARCHITECTURE_REFACTOR.md` para ver responsabilidades y dependencias.

## Terreno, recursos y trabajadores

El escenario puede declarar un terreno base mediante `terrain.defaultTerrainId`.
`ScenarioTerrainController` crea la malla local en host y clientes desde el catálogo
`GameContent/Terrains`.

Para probar extracción:

1. Cargar `scenario_3v3_resources`.
2. Seleccionar la entidad `Trabajador` del equipo 1.
3. Hacer clic derecho sobre uno de los árboles neutrales.
4. El servidor valida tier y herramientas, mueve al trabajador y procesa ciclos de
   extracción de 2 segundos.

Mensajes nuevos:

- `GammaSix.ResourceInteractionCommand`: cliente → servidor.
- El estado restante del recurso y la carga temporal del trabajador viajan dentro
  del snapshot de entidades usando `ReliableFragmentedSequenced`.

El snapshot ahora usa bytes UTF-8 de tamaño dinámico y no queda limitado por
`FixedString4096Bytes`.

## Propiedad runtime y snapshots

Los cambios administrativos de propietario no crean una entidad nueva. La autoridad actualiza `OwnerParticipantId`, `OwnerClientId`, `TeamId` y `ColorId`; el snapshot periódico replica el cambio a todos los clientes. En multijugador, `/spawn`, `/change_owner` y `/change_team` son comandos exclusivos del host.

## Mensajes de navegación de la Fase 10

Las órdenes de terreno nuevas utilizan mensajes pequeños y autoritativos:

```text
GammaSix.UnitAttackMoveCommand
GammaSix.UnitPatrolCommand
```

El servidor las transforma en `MatchCommandType.AttackMove` o
`MatchCommandType.Patrol`. El cliente no es fuente de verdad del camino; los
snapshots replican posición y diagnóstico de navegación (`NavigationOrder`,
`NavigationPathPurpose`, waypoint actual y estado del cálculo).
