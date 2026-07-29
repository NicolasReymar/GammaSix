# Runtime autoritativo y Command Bus

## Alcance de la fase 2

Esta actualización extrae la simulación de entidades fuera de
`NetworkEntityCoordinator` y crea una ruta de comandos común para humanos,
participantes headless y reglas futuras de escenarios.

## Flujo actual

```text
Humano local / cliente remoto / headless futuro
                    ↓
            MatchCommandBus
                    ↓
       AuthoritativeMatchRuntime
                    ↓
 movimiento · interacción · extracción
                    ↓
             EntityWorld
                    ↓
 snapshots y vistas
```

## Clases principales

- `MatchRuntimeController`: hospeda el runtime. En multijugador solo el servidor
  crea la simulación; en un jugador se ejecuta localmente.
- `AuthoritativeMatchRuntime`: fuente de verdad de la partida.
- `EntityWorld`: contiene todas las entidades runtime.
- `RuntimeEntityIdAllocator`: entrega IDs estables y no reutilizados durante la
  partida.
- `MatchParticipantRegistry`: resuelve participantes por `ParticipantId` y
  autentica humanos mediante `ClientId`.
- `MatchCommandBus`: encola y procesa comandos en orden estable.
- `MatchWorldBounds`: deriva los límites desde `ScenarioDefinition.worldSize`.

## Propiedad de entidades

`OwnerParticipantId` es ahora la referencia utilizada para validar órdenes.
`OwnerClientId` se conserva temporalmente en estados y snapshots para
compatibilidad visual, pero ya no decide la propiedad del gameplay.

Esto permite que una entidad pertenezca a:

- un humano conectado;
- un participante headless;
- un participante sin conexión directa;
- un controlador de escenario futuro.

## Comandos disponibles

La fase registra tres tipos:

- `Move`;
- `ResourceInteraction`;
- `EntityInteraction`.

Los comandos de red ya no modifican directamente las entidades. El servidor
resuelve el `ClientId` hacia un `ParticipantId` y encola la orden en el runtime.

Un controlador headless futuro utilizará la misma ruta:

```csharp
runtime.EnqueueHeadlessCommand(
    participantId,
    controllerProfileId,
    MatchCommandType.Move,
    new EntityMoveCommand
    {
        UnitId = entityId,
        X = destination.x,
        Y = destination.y,
        Z = destination.z
    });
```

El runtime valida que:

1. el participante exista;
2. sea realmente headless;
3. el perfil coincida;
4. la entidad pertenezca al participante;
5. la entidad permita la orden.

## Un jugador

El modo de un jugador crea un participante humano local con `ParticipantId = 1`
y utiliza exactamente el mismo runtime, Command Bus, movimiento, extracción y
presentación que el host multijugador.

## Límites del mapa

Se eliminó el límite fijo de 19 unidades. El destino se limita usando:

```text
worldSize.width / 2
worldSize.height / 2
```

Si el escenario no define tamaño, se conserva un fallback equivalente al
comportamiento anterior.

## Pendiente para la fase siguiente

Esta fase no implementa todavía decisiones de IA. La base queda lista para:

1. registro y ciclo de vida de controladores headless;
2. comandante normal incluido en GammaSix;
3. comandante declarativo del escenario Kodo Tag;
4. órdenes de persecución y selección de objetivos;
5. spawn y despawn dinámicos;
6. eventos, reglas, oleadas, captura y rescate.

## Ciclo de vida dinámico

El runtime incorpora `EntityLifecycleService`. Las oleadas, construcciones, reglas
y rescates futuros deberán usar `QueueSpawn` y `QueueDespawn`; no deben modificar
`EntityWorld` directamente durante un tick. Los eventos de alta y baja se replican
de forma confiable y el snapshot completo continúa como mecanismo de corrección.
