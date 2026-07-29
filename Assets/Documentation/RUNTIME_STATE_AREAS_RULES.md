# Estado runtime, entidades de área y reglas declarativas

## Objetivo

Esta fase agrega tres bases generales del motor:

1. Estado mutable de participantes, equipos, recursos y resultado de partida.
2. Entidades de área autoritativas reutilizables por aura y trigger.
3. Motor inicial de eventos, condiciones y acciones definido desde el escenario.

No existe código específico de Kodo Tag. El paquete guardado combinará estas
capacidades para captura, rescate, oleadas, objetivos y otros modos.

## Estado de participantes

Cada `MatchParticipantRuntimeState` posee:

- `ParticipantId` estable.
- `ParticipantLifeState`.
- Recursos propios extensibles.
- Equipo y controlador humano/headless.

Estados disponibles:

- `Active`
- `Captured`
- `Eliminated`
- `Disconnected`
- `Victorious`
- `Defeated`

Solo un participante `Active` puede emitir comandos humanos o headless. Las
reglas runtime pueden cambiar estados aunque no exista un cliente conectado.

## Equipos y recursos

`MatchTeamRegistry` mantiene recursos compartidos por equipo. Se conserva el
campo legado `gold` y se agrega una lista extensible:

```json
{
  "teamId": 1,
  "gold": 500,
  "resources": [
    { "resourceId": "wood", "amount": 150 }
  ]
}
```

También se pueden inicializar recursos por participante mediante
`participantResources`.

## Entidades de área

Una entidad común puede agregar el componente declarativo `area`:

```json
{
  "id": "area.rescue",
  "visual": "aura",
  "attributes": [
    "entity",
    "entity.area",
    "area.circular",
    "area.trigger",
    "trigger.rescue",
    "physics.not_solid"
  ],
  "area": {
    "shape": "circle",
    "radius": 3,
    "relationship": "ally",
    "requiredAttributes": ["role.player-anchor"],
    "excludedAttributes": ["entity.area"],
    "emitEnter": true,
    "emitStay": true,
    "emitExit": true,
    "stayInterval": 0.25,
    "visible": true
  }
}
```

Las áreas se calculan en `EntityAreaRuntimeSystem` sobre el servidor. Los
colliders del círculo son solamente presentación y raycast; no deciden las
reglas de gameplay.

El círculo de aura anterior sigue siendo compatible. `trigger.aura` deriva los
atributos `entity.area`, `area.circular`, `area.aura` y `area.trigger`. Una entidad
que solo declare `entity.area` también recibe un área circular básica calculada
desde su escala; el bloque `area` se utiliza cuando se necesita una configuración
explícita.

## Eventos iniciales

- `match-started`
- `entity-spawned`
- `entity-despawned`
- `entity-entered-area`
- `entity-stayed-in-area`
- `entity-exited-area`
- `entity-damaged`
- `entity-fatal-damage`
- `entity-died`
- `participant-state-changed`
- `resource-changed`
- `match-result-declared`

## Condiciones iniciales

- `area-has-attribute`
- `entity-has-attribute`
- `source-entity-has-attribute`
- `entity-team-is`
- `area-team-is`
- `entity-life-state-is`
- `entity-health-at-or-below`
- `damage-type-is`
- `participant-state-is`
- `entity-owner-state`
- `match-phase-is`

## Acciones iniciales

- `show-message`
- `prevent-death`
- `restore-health`
- `set-fatal-resolution`
- `set-participant-state`
- `give-resource`
- `remove-resource`
- `spawn-entity`
- `despawn-event-entity`
- `declare-victory`
- `declare-defeat`
- `declare-draw`

Los `spawn-entity` siguen pasando por `EntityLifecycleService` y únicamente
aceptan entidades registradas por el catálogo de la partida.

## Ejemplo de trigger

```json
{
  "id": "rule.rescue.enter",
  "eventType": "entity-entered-area",
  "conditions": [
    {
      "type": "area-has-attribute",
      "attribute": "trigger.rescue"
    },
    {
      "type": "entity-has-attribute",
      "attribute": "role.player-anchor"
    }
  ],
  "actions": [
    {
      "type": "show-message",
      "message": "Comenzó el rescate."
    }
  ]
}
```

## Comandos de diagnóstico

- `/state`: fase, participantes, estados y recursos.
- `/areas`: áreas activas y cantidad de ocupantes.

El menú `GammaSix/Content/Create and Import Package Example` genera un paquete
con un soldado, un círculo de área y una regla de entrada para probar el flujo.

## Integración con la fase 6

Las reglas `entity-fatal-damage` se evalúan sincrónicamente antes de confirmar la muerte. Esto permite cambiar la resolución a `Prevented` o `Downed`. Las mecánicas basadas en una muerte consumada deben escuchar `entity-died`. Los detalles completos están en `ENTITY_LIFE_DAMAGE_COMBAT.md`.
