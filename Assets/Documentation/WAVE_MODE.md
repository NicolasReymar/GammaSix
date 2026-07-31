# Fase 8 - Wave Mode genérico

## Objetivo

El runtime ofrece un orquestador autoritativo de oleadas, pero las unidades,
los tiempos, los grupos, las áreas de aparición y la condición para avanzar
provienen del escenario o paquete guardado. No existe lógica específica de
Kodo Tag dentro de `WaveRuntimeSystem`.

## Definición básica

```json
"waveControllers": [
  {
    "id": "mode.main-waves",
    "enabled": true,
    "autoStart": true,
    "initialDelay": 5,
    "defaultInterWaveDelay": 3,
    "repeatMode": "none",
    "repeatCount": 1,
    "waves": [
      {
        "id": "wave.one",
        "preparationTime": 2,
        "delayAfterCompletion": 3,
        "completionCondition": "all-spawned-resolved",
        "groups": [
          {
            "id": "north",
            "entityId": "unit.humanoid.default",
            "count": 5,
            "startDelay": 0,
            "spawnInterval": 0.5,
            "teamId": 0,
            "spawnAreaAttribute": "mode.spawn.north",
            "randomizePositionInArea": true,
            "positionJitterRadius": 0.25
          }
        ]
      }
    ]
  }
]
```

Las entidades referenciadas por los grupos se cargan en el catálogo de la
partida. El spawn sigue pasando por `MatchEntityCatalog` y
`EntityLifecycleService` con `EntityLifecycleReason.Wave`.

## Propiedad

Un grupo puede resolver propietario mediante, en este orden:

1. `ownerParticipantId`.
2. `ownerSlotId`.
3. Primer participante del `teamId` declarado.
4. Si `teamId` es 0, se genera como entidad neutral sin propietario.

Una entidad de equipo no se crea si no existe un participante que pueda ser su
propietario.

## Posición

`spawnAreaAttribute` busca una entidad de área con ese atributo. Se prioriza un
área del mismo equipo y luego una neutral. Si no existe, se usa `position`.

`randomizePositionInArea` distribuye las apariciones dentro del círculo o
rectángulo autoritativo. `positionJitterRadius` agrega una dispersión adicional.

## Condiciones de finalización

- `all-spawned-resolved`: avanza cuando todas las entidades generadas murieron o
  fueron despawneadas.
- `spawn-complete`: avanza apenas todos los grupos terminaron de encolar sus
  apariciones; las entidades anteriores pueden seguir en el mapa.

## Repetición

- `repeatMode: none`: termina tras la última oleada.
- `repeatMode: loop`: vuelve a la primera oleada.
- `repeatCount > 0`: cantidad total de ciclos.
- `repeatCount <= 0`: repetición ilimitada.

## Eventos declarativos

- `wave-controller-started`
- `wave-preparation-started`
- `wave-started`
- `wave-group-started`
- `wave-group-completed`
- `wave-completed`
- `wave-controller-paused`
- `wave-controller-resumed`
- `wave-controller-stopped`
- `wave-controller-completed`

El contexto incluye `WaveControllerId`, `WaveId`, `WaveIndex`, `WaveCycle`,
`WaveGroupId` y el estado del controlador.

## Condiciones para reglas

- `wave-controller-id-is`
- `wave-id-is`
- `wave-index-is`
- `wave-cycle-is`
- `wave-state-is`

## Acciones para reglas

- `start-wave-controller`
- `pause-wave-controller`
- `resume-wave-controller`
- `stop-wave-controller`
- `advance-wave-controller`

Cada acción recibe `waveControllerId`. Esto permite iniciar oleadas mediante
triggers, misiones, variables o cualquier evento ya soportado por las reglas.

## Comandos de prueba

```text
/waves [filtro]
/wave start <controller-id>
/wave pause <controller-id>
/wave resume <controller-id>
/wave stop <controller-id>
/wave advance <controller-id>
```

Los comandos que modifican oleadas son exclusivos del host o partida local.

## Escenario técnico

`scenario_wave_mode_test` contiene dos oleadas neutrales y dos áreas de spawn.
La segunda oleada comienza cuando todas las entidades de la primera murieron o
fueron despawneadas. Se puede usar `/runtime`, `/damage` y `/waves` para probarla.

## Escenario de prueba hostil

`scenario_wave_mode_test` utiliza ahora dos equipos reales:

- Equipo 1: jugador humano.
- Equipo 2: participante Headless obligatorio `wave.enemy`.

Los grupos de oleada se crean con `teamId: 2` y `ownerSlotId: "wave.enemy"`.
El runtime actual considera enemigas a dos entidades cuyos equipos sean distintos y mayores que cero. Las áreas de spawn pueden continuar neutrales porque solo ubican las apariciones y no representan combatientes.

En partida local, `MatchRuntimeController` incorpora también los participantes Headless obligatorios declarados por el escenario. Así la propiedad de las oleadas es idéntica entre un jugador y multijugador.
