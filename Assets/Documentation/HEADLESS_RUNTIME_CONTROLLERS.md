# Fase 9 — Controladores Headless

## Objetivo

Los participantes Headless ya existían en lobby y runtime. Esta fase conecta un
planificador autoritativo que percibe el mundo y emite exactamente las mismas
órdenes que un humano mediante `MatchCommandBus`.

Los escenarios configuran perfiles conocidos, pero no cargan código C#.

## Flujo

```text
HeadlessControllerRuntimeSystem
→ HeadlessPerceptionContext (solo lectura)
→ IHeadlessController
→ MatchCommandBus
→ validación de participante y propiedad
→ sistemas de movimiento/combate
```

## Perfil configurable

```json
{
  "id": "phase9.simple-assault",
  "runtimeImplemented": true,
  "runtimeControllerId": "base:headless-controller.simple-assault",
  "controllerSettings": {
    "updateInterval": 0.35,
    "maxOrdersPerUpdate": 4,
    "targetPolicy": "nearest-hostile",
    "includeNeutralTargets": false,
    "controlledRequiredAttributes": ["phase9.ai-controlled"],
    "targetExcludedAttributes": ["entity.area"]
  }
}
```

`runtimeControllerId` debe estar registrado en GammaSix. Los paquetes pueden
configurarlo, pero no inyectar implementaciones.

## Controlador técnico de asalto

`base:headless-controller.simple-assault`:

- obtiene entidades propias vivas, controlables y con ataque;
- respeta postura `Passive`;
- conserva objetivos hostiles todavía válidos;
- busca el objetivo hostil más cercano cuando necesita uno;
- emite `MatchCommandType.Attack`;
- deja persecución, windup, recuperación y daño a los sistemas existentes.

No construye, no recolecta y no reemplaza al comandante normal de escaramuza.

## Diagnóstico

```text
/headless
/headless simple
```

Muestra perfil, implementación, estado, órdenes emitidas y última decisión.

## Escenario de prueba

`Headless Controller - Prueba` crea dos soldados humanos y dos soldados del
participante Headless. Los soldados Headless poseen `phase9.ai-controlled` y
deben adquirir objetivos del equipo 1 automáticamente.

## Integración con diplomacia

Desde la Fase 9.5 el controlador no interpreta que dos IDs de equipo diferentes
sean enemigos. Consulta la relación direccional:

```text
Diplomacy.GetStance(equipoHeadless, equipoObjetivo) == Enemy
```

Por eso una relación asimétrica puede hacer que una facción persiga a otra sin
que esta última la adquiera automáticamente. Cambiar la postura durante la
partida afecta la siguiente evaluación del controlador.
