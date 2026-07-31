# Chat y comandos de partida

## Propósito

`GameTextInputHud` es un módulo UXML independiente ubicado en la parte inferior de la pantalla. Sirve como interfaz común para chat y comandos de administración de la partida. No contiene reglas específicas de Kodo Tag.

## Uso estilo consola básica

La interfaz permanece oculta durante el gameplay normal.

- `Enter`: abre la consola y enfoca el campo.
- `Enter` dentro del campo: envía el texto y vuelve a ocultarla.
- `Enter` con el campo vacío: la cierra sin enviar.
- `/`: abre directamente la consola con el prefijo de comando preparado.
- `Escape`: cancela el texto y cierra la consola sin abrir el menú de pausa.
- `Flecha arriba/abajo`: recorre los últimos textos enviados localmente.
- Un texto normal se transmite como chat de partida.
- Los comandos comienzan con `/`. `spawn` y `despawn` también conservan su alias sin `/`.

El panel utiliza `DraggableHudPanel`, comparte el borde amarillo del modo de edición y guarda su posición con la clave `GammaSix.Hud.GameTextInput`. El encabezado actúa como superficie de arrastre para no interferir con el campo ni con el botón `Enviar`.

## Comandos actuales

```text
/help
/entities [filtro]
/runtime [filtro]
/state
/areas
/combat [filtro]
/channels
/waves [filtro]
/wave <start|pause|resume|stop|advance> <controller-id>
/attack <runtime-id-atacante> <runtime-id-objetivo>
/damage <runtime-id-objetivo> <cantidad> [runtime-id-origen]
/spawn <id-entidad-cargada> posicion(x,y,z)
/spawn <id-entidad-cargada> <x> <y> <z>
/despawn <runtime-id>
/despawn last
```

Antes de crear una entidad se consulta el catálogo de la partida:

```text
/entities
```

Ejemplo con una entidad real del escenario base:

```text
/spawn unit.humanoid.default posicion(0,0,0)
```

`/entities` no recorre todas las definiciones instaladas. Solo muestra las entidades habilitadas por el escenario activo y por los valores predeterminados de su modo de juego.

`/runtime` muestra el ID runtime junto con la definición, útil antes de ejecutar `/despawn`.

`/state` muestra fase, participantes, estados y recursos. `/areas` lista las entidades de área autoritativas y su cantidad de ocupantes. `/combat` muestra vida, actividad, fase de ataque y objetivo. `/attack` encola una orden por el Command Bus y `/damage` prueba el servicio autoritativo de daño.

## Restricción de spawn

Toda creación dinámica pasa por `MatchEntityCatalog` y `EntityLifecycleService`.

Una entidad puede crearse solamente cuando:

1. Su definición existe y pudo cargarse al iniciar la partida.
2. Está habilitada por el catálogo de la partida.
3. La solicitud proviene de la autoridad y supera las validaciones del runtime.

La restricción se aplica también a oleadas, reglas, construcciones, reemplazos y acciones declarativas. No es una validación exclusiva del comando de texto.

## Autoridad

- El chat está disponible para todos los jugadores humanos.
- `spawn`, `despawn`, `attack` y `damage` solo pueden ejecutarse desde el host o en una partida local.
- Un cliente remoto envía el texto al servidor, pero el servidor rechaza comandos que modifiquen el mundo.
- Spawn y despawn utilizan `EntityLifecycleService`, manteniendo la sincronización de eventos dinámicos.

## Reutilización del HUD

La estructura queda separada por responsabilidad:

```text
Comportamiento común de arrastre
└── DraggableHudPanel.cs

Apariencia común de edición
└── GameHudShared.uss

Estructura de la consola
└── GameTextInputHud.uxml

Apariencia específica
└── GameTextInputHud.uss
```

UXML no utiliza herencia de clases como C#. La reutilización se realiza mediante controladores C# compartidos y clases USS comunes.

## Archivos principales

```text
Scripts/UI/Uxml/Game/Resources/UI/GameHud/GameTextInputHud.uxml
Scripts/UI/Uxml/Game/Resources/UI/GameHud/GameTextInputHud.uss
Scripts/UI/Uxml/Game/Resources/UI/GameHud/GameHudShared.uss
Scripts/UI/Uxml/Game/TextInput/GameTextInputHudController.cs
Scripts/UI/Uxml/Game/Core/DraggableHudPanel.cs
Scripts/Game/Communication/MatchTextChannelController.cs
Scripts/Game/Communication/MatchTextCommandParser.cs
Scripts/Game/Communication/MatchTextNetworkMessages.cs
Scripts/Game/Runtime/Entities/MatchEntityCatalog.cs
```

La interfaz queda preparada para ampliar posteriormente canales de equipo, espectadores, mensajes del sistema, autocompletado y comandos registrados por paquetes.


## Canalizaciones

```text
/channels
```

`/channels` consulta las canalizaciones activas. No existen comandos base de
captura o rescate: esas mecánicas se construyen mediante reglas de la partida
guardada.


## Oleadas

```text
/waves [filtro]
/wave start <controller-id>
/wave pause <controller-id>
/wave resume <controller-id>
/wave stop <controller-id>
/wave advance <controller-id>
```

`/waves` puede consultarse desde cualquier cliente porque la respuesta se genera en la autoridad. Las operaciones de control solo se aceptan desde el host o una partida local.

### Diagnóstico Headless

```text
/headless [filtro]
```

Muestra los controladores autoritativos, su perfil, estado, cantidad de órdenes
y última decisión.

## Diplomacia asimétrica

```text
/diplomacy
/diplomacy_stance <equipo-origen> <equipo-objetivo> <ally|neutral|enemy>
```

`/diplomacy` muestra la matriz direccional actual. El comando de modificación es
host-only o local y altera únicamente `equipo-origen → equipo-objetivo`; no
cambia la dirección inversa. La ventana UXML de diplomacia se abre con `O` y
permite que el jugador cambie solo la postura saliente de su propio equipo.
