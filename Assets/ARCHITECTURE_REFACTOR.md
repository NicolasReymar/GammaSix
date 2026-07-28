# GammaSix – Refacción estructural, fase 1

Esta fase reorganiza los sistemas existentes sin cambiar las reglas de jugabilidad.

## Flujo principal de entidades

```text
EntityDefinition JSON
    ↓
ScenarioEntityPlacement
    ↓
ScenarioEntitySpawner
    ↓
EntityRuntimeState (servidor)
    ↓
EntitySnapshotData
    ↓
NetworkEntityView (cliente/host)
    ↓
EntitySelectionService / HUD
```

## Carpetas principales

```text
Scripts/
├── Core/Content/
│   ├── Models/
│   │   ├── Scenarios/
│   │   └── Campaigns/
│   └── Storage/
│
├── Game/
│   ├── Input/
│   ├── Selection/
│   └── Entities/
│       ├── Attributes/
│       ├── Definitions/
│       ├── Spawning/
│       ├── Movement/
│       └── Buildings/
│
├── Network/
│   ├── Core/
│   ├── Session/
│   └── Entities/
│       └── Views/
│
└── UI/Uxml/Game/
    ├── Core/
    ├── Gold/
    ├── SelectionInspector/
    ├── SelectionExtended/
    └── Resources/UI/GameHud/
```

## Responsabilidades

### NetworkEntityCoordinator

Es la fachada de la partida en red. Coordina:

- registro de mensajes;
- snapshot periódico;
- input local;
- conexión entre selección, cámara y órdenes.

Ya no implementa directamente:

- creación de entidades;
- movimiento y colisiones;
- agrupación de selección;
- grupos de control;
- lectura de dispositivos;
- creación de vistas;
- DTO de red.

### EntitySelectionService

Única fuente de verdad para:

- entidades seleccionadas;
- entidad/grupo inspeccionado;
- preservación de la entidad bloqueada en tercera persona;
- eventos `SelectionChanged` e `InspectionChanged`.

### SelectionGroupBuilder

Regla pura de agrupación:

- heroicos individuales;
- entidades normales agrupadas por definición;
- representante con mayor vida.

### GameInputReader

Centraliza teclado y mouse. Cámara y selección ya no duplican lecturas del Input System.

### HudInteractionService

Centraliza:

- modo edición del HUD;
- paneles registrados;
- puntero sobre HUD;
- arrastre activo.

La selección RTS se bloquea durante todo el arrastre de un panel, aunque el cursor salga de sus límites.

### ScenarioEntitySpawner

Resuelve `ScenarioEntityPlacement` y genera estados runtime. El escenario solo indica colocación, equipo, propietario y atributos de instancia.

### EntityMovementService

Contiene movimiento autoritativo, validación de propietario y colisiones.

### EntityAttributeResolver

Combina atributos de definición e instancia y aplica dependencias, por ejemplo:

```text
unit.heroic → camera.third-person
```

## Compatibilidad heredada eliminada

Se eliminó la lectura de `persistentDataPath/Maps`. Todo contenido debe almacenarse en:

```text
GameContent/Scenarios
GameContent/Campaigns
GameContent/Entities
```

## Clases parciales por responsabilidad

`NetworkSessionManager` conserva una única instancia y los mismos campos, pero su código está distribuido en:

```text
NetworkSessionManager.cs
NetworkSessionManager.Connection.cs
NetworkSessionManager.Lobby.cs
NetworkSessionManager.Content.cs
NetworkSessionManager.Messaging.cs
```

`MainMenuController` mantiene sus referencias serializadas en el archivo principal y separa el comportamiento en:

```text
MainMenuController.cs
MainMenuController.SinglePlayer.cs
MainMenuController.Multiplayer.cs
MainMenuController.Content.cs
MainMenuController.Settings.cs
```

## Próximas fases recomendadas

1. Convertir las clases parciales de sesión en servicios internos cuando sus contratos estén estabilizados.
2. Convertir la cámara en una máquina de estados explícita.
3. Crear un estado runtime de recursos de equipo con eventos para oro y futuros recursos.
4. Agregar namespaces y archivos `.asmdef` cuando la estructura deje de cambiar con frecuencia.

## Terreno y recursos

La fase de terreno y recolección agrega módulos separados:

```text
Game/Terrain/Definitions
Game/Terrain/Runtime
Game/Entities/Resources
```

El terreno no es una entidad: se carga desde su propio catálogo y se combina en
mallas de celdas. Los árboles y otros objetos de ambiente sí son entidades, por lo
que reutilizan definición por ID, atributos, equipo neutral, snapshots y selección.
La extracción es autoritativa y reutiliza `EntityMovementService` mediante el
objetivo almacenado en `WorkerRuntimeState`.

## Contextual entity interactions

Contextual orders are resolved by `EntityInteractionRules` instead of checking
specific unit definitions in the input controller.

Current rules:

- Any locally-owned entity with `interaction.controllable` may follow a unit or
  building that is owned, allied, or neutral.
- `unit.worker` only grants the specialized `ExtractResource` action when the
  target has `entity.resource`.
- Enemy targets currently resolve to no contextual action and are reserved for
  the future combat system.
- Each selected source resolves its action independently, allowing mixed
  selections without coupling generic following to worker rules.

Future actions such as attack, repair, trade, enter-building, heal, or escort
should be added as new `ContextualEntityAction` values and resolved centrally.

## Feedback de órdenes contextuales

El feedback visual de una orden se centraliza en:

```text
Scripts/Game/Entities/Interaction/EntityCommandFeedbackService.cs
```

El servicio no decide reglas ni ejecuta gameplay. Solo confirma localmente el objetivo mediante el halo de `NetworkEntityView`. Cualquier acción contextual válida puede reutilizarlo sin implementar un parpadeo propio.

## Colores de relación de entidades

`EntityRelationshipVisuals` centraliza los colores usados por selección y feedback contextual:

- Propia o aliada del mismo equipo: verde.
- Neutral (team 0): amarillo.
- Enemiga: rojo.

El feedback no depende de la acción concreta, por lo que puede reutilizarse en seguir, extraer, atacar, reparar, curar o comerciar.


## Overrides de atributos

Los atributos que bloquean capacidades no se eliminan de la entidad. `EntityAttributeOverrideService` evalúa la configuración de la partida y permite ignorarlos temporalmente. El primer caso soportado es:

```text
interaction.not_selectable <-> override_not_selectable
```

Con `override_not_selectable: true`, las entidades conservan el atributo, pero pueden seleccionarse durante esa partida.

## Separación entre interacción y física

- `interaction.not_selectable` impide selección e interacción contextual.
- `physics.not_solid` hace que la entidad no bloquee el movimiento.
- El clic derecho sobre una entidad no seleccionable se transforma en movimiento al centro.
- Solo puede atravesarse cuando la entidad también es efectivamente no sólida.
- La colisión temporal por orden fue eliminada; la física depende únicamente de atributos y overrides.
