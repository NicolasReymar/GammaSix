# Fase 9.5 — Diplomacia asimétrica de equipos

## Objetivo

La hostilidad ya no se deduce comparando IDs de equipo. Cada equipo mantiene
una postura direccional hacia cada otro equipo:

```text
Equipo origen → Equipo objetivo = Ally | Neutral | Enemy
```

La dirección inversa es independiente. Por ejemplo, el Equipo 1 puede considerar
enemigo al Equipo 2 mientras el Equipo 2 todavía considera neutral al Equipo 1.

Los participantes humanos y Headless usan el mismo equipo y consultan la misma
matriz autoritativa.

## Valores predeterminados

```text
Mismo equipo                 → Ally
Equipo 0 involucrado         → Neutral
Relación no declarada        → Neutral
Relación explícita del mapa  → valor declarado
```

Un equipo no puede configurarse como enemigo o neutral de sí mismo.

## Configuración inicial del escenario

```json
{
  "diplomacy": [
    {
      "sourceTeamId": 1,
      "targetTeamId": 2,
      "stance": "Enemy",
      "bidirectional": false
    },
    {
      "sourceTeamId": 2,
      "targetTeamId": 1,
      "stance": "Neutral",
      "bidirectional": false
    }
  ]
}
```

`bidirectional: true` es una comodidad del contenido: expande la declaración a
las dos direcciones. El runtime continúa almacenando dos relaciones separadas.

## Combate, interacción e IA

Todas las consultas usan la perspectiva de la entidad que actúa:

```text
Diplomacy.GetStance(attacker.TeamId, target.TeamId)
```

- `Enemy`: el clic contextual puede atacar y la IA Headless puede adquirir el objetivo.
- `Neutral`: no se adquiere automáticamente; puede seguirse o interactuarse cuando corresponda.
- `Ally`: no se adquiere automáticamente y puede seguirse o interactuarse.
- La orden manual forzada de ataque conserva su comportamiento provisional de la Fase 8+.

Las áreas con filtros de relación y los visuales de selección también consultan
la matriz direccional.

## UI UXML

La tecla `O` abre o cierra el módulo:

```text
DiplomacyHud.uxml
DiplomacyHud.uss
DiplomacyHudController.cs
```

La ventana muestra para cada equipo:

- participantes humanos y Headless visibles;
- postura de tu equipo hacia ellos;
- postura de ellos hacia tu equipo;
- botones `Aliado`, `Neutral` y `Enemigo` para cambiar solo la dirección local.

La ventana es modal y bloquea el input de gameplay mientras está abierta. El
envío de recursos se reserva para una fase posterior.

## Comandos

```text
/diplomacy
/diplomacy_stance <equipo-origen> <equipo-objetivo> <ally|neutral|enemy>
```

`/diplomacy` consulta la matriz. `/diplomacy_stance` es una herramienta de
administración disponible para host o partida local y cambia solo una dirección.

Ejemplo de guerra mutua:

```text
/diplomacy_stance 1 2 enemy
/diplomacy_stance 2 1 enemy
```

## Reglas declarativas

Evento:

```text
diplomacy-stance-changed
```

Condiciones:

```text
diplomacy-source-team-is
diplomacy-target-team-is
diplomacy-stance-is
```

Acción:

```json
{
  "type": "set-diplomacy-stance",
  "sourceTeamId": 2,
  "targetTeamId": 1,
  "diplomacyStance": "Enemy",
  "bidirectional": false,
  "reason": "faction-provoked"
}
```

## Sincronización

La autoridad incluye la matriz completa en `EntitySnapshotPayload`. Los clientes
mantienen una copia de presentación en `DiplomacyClientState`; esta copia no
puede modificar el gameplay.

Los cambios del HUD se envían como `MatchCommandType.SetDiplomacyStance`. El
servidor valida que un participante normal solo cambie la postura saliente de su
propio equipo. Las reglas del runtime pueden modificar cualquier dirección.

## Escenario de prueba

`Diplomacia asimétrica - Prueba` contiene tres equipos:

```text
1 → 2 = Enemy       2 → 1 = Enemy
1 → 3 = Ally        3 → 1 = Neutral
2 → 3 = Neutral     3 → 2 = Enemy
```

Los equipos 2 y 3 son Headless. La adquisición automática de objetivos debe
seguir exactamente esas direcciones y actualizarse al cambiar una postura desde
la ventana `O` o desde el comando administrativo.
