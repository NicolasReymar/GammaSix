# Paquetes de contenido y entidades dinámicas

Esta fase incorpora dos fundamentos para los minijuegos importables:

1. paquetes `.gsixpackage` aislados y validados;
2. creación y eliminación dinámica de entidades en el runtime autoritativo.

## Formato `.gsixpackage`

Un `.gsixpackage` es un ZIP cuya raíz contiene:

```text
manifest.json
Scenarios/
Entities/
Terrains/       (opcional)
Campaigns/      (opcional)
Rules/          (reservado para fases posteriores)
```

No debe existir una carpeta contenedora adicional dentro del ZIP.

### Manifest mínimo

```json
{
  "packageId": "minigame.kodo-tag",
  "packageVersion": "1.0.0",
  "displayName": "Kodo Tag",
  "author": "GammaSix",
  "requiredGameVersion": "MISMA_VERSION_DE_APPLICATION_VERSION",
  "contentFormatVersion": 1,
  "entryScenarioId": "scenario.main",
  "requiredFeatures": [
    "content.packages.v1",
    "runtime.participants.v1",
    "runtime.command-bus.v1",
    "runtime.dynamic-entities.v1"
  ]
}
```

En esta primera versión `requiredGameVersion` debe coincidir exactamente con
`Application.version`.

## Importación

GammaSix crea esta bandeja:

```text
Application.persistentDataPath/GameContent/Import
```

Al iniciar/refrescar el repositorio, todos los `.gsixpackage` ubicados directamente
en esa carpeta son:

1. extraídos a una carpeta temporal;
2. comprobados contra rutas inseguras;
3. validados;
4. hasheados con SHA-256;
5. instalados de forma transaccional;
6. registrados en `GameContent/Packages/registry.json`.

Los archivos procesados se mueven a:

```text
Import/Imported
Import/Rejected
```

También puede llamarse directamente:

```csharp
GameContentPackageImporter.ImportPackage(path);
```

## Aislamiento y namespaces

El contenido instalado vive en:

```text
GameContent/Packages/<packageId>/<packageVersion>/
```

Las referencias runtime usan:

```text
base:unit.humanoid.default
minigame.kodo-tag:unit.kodo.basic
```

Dentro de un paquete, una referencia sin namespace se intenta resolver primero
dentro del propio paquete. Para usar contenido base debe escribirse explícitamente
`base:<id>`.

## Compatibilidad multijugador

El host sincroniza:

```text
packageId
packageVersion
contentHash
scenarioId
```

Cada cliente responde si posee exactamente el mismo paquete, versión, hash y
escenario. El host no puede iniciar mientras un jugador remoto sea incompatible.

## Seguridad inicial

El importador rechaza:

- rutas que salgan de la carpeta temporal;
- DLL, ejecutables, scripts y otros archivos ejecutables;
- paquetes excesivamente grandes;
- IDs o versiones inválidas;
- versiones de juego incompatibles;
- funcionalidades no soportadas;
- referencias locales inexistentes;
- escenarios o definiciones con IDs duplicados.

Los paquetes son exclusivamente declarativos.

## Entidades dinámicas

Las altas y bajas pasan por:

```text
EntityLifecycleService
├── QueueSpawn
├── QueueDespawn
└── FlushPending
```

Los sistemas no deben modificar `EntityWorld` mientras lo enumeran.

### Spawn

```csharp
EntitySpawnRequest request = new()
{
    EntityDefinitionId = "minigame.kodo-tag:unit.kodo.basic",
    OwnerParticipantId = kodoParticipantId,
    TeamId = 2,
    ColorId = kodoColorId,
    Position = spawnPosition,
    Reason = EntityLifecycleReason.Wave
};

runtime.QueueEntitySpawn(request, out string rejection);
```

El ID runtime se asigna exclusivamente mediante `RuntimeEntityIdAllocator`.

### Despawn

```csharp
runtime.QueueEntityDespawn(
    entityId,
    EntityLifecycleReason.RuntimeRule,
    out string rejection);
```

Las solicitudes se aplican entre etapas del tick. Al eliminar una entidad también
se limpian referencias de seguimiento y extracción.

## Sincronización

Spawn y despawn generan eventos confiables inmediatos:

```text
GammaSix.EntitySpawned
GammaSix.EntityDespawned
```

Los snapshots completos continúan funcionando como recuperación y corrección de
estado.

## Catálogo de entidades de la partida

El escenario declara qué definiciones pueden utilizar los sistemas de creación dinámica:

```json
"entityCatalog": {
  "spawnableEntityIds": [
    "unit.humanoid.default",
    "unit.humanoid.worker",
    "building.mercenary"
  ]
}
```

Las colocaciones iniciales se cargan automáticamente. Sin embargo, una oleada, regla, construcción, reemplazo, rescate o comando de texto solo puede crear entidades incluidas en `spawnableEntityIds`.

Los escenarios antiguos conservan compatibilidad: si no declaran el catálogo, sus propios tipos colocados se consideran disponibles, pero nunca se habilitan todas las definiciones instaladas.

## Herramienta de paquete de ejemplo

En Unity está disponible:

```text
GammaSix/Content/Create and Import Package Example
```

Genera un paquete con `Application.version`, lo importa y agrega un escenario que utiliza el soldado y el trabajador completos del juego base. Las pruebas de spawn y despawn se realizan desde el canal de texto de la partida con `/entities`, `/spawn` y `/despawn`.

No se incluye ninguna entidad de depuración separada. Los visuales de las entidades reales continúan resolviéndose desde sus propias definiciones.
