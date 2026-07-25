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
