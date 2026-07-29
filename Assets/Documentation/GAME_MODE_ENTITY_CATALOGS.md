# Catálogos de entidades por modo de juego

El registro dinámico de una partida se forma con tres fuentes:

1. Entidades colocadas inicialmente por el escenario.
2. Entidades base registradas por el modo de juego.
3. Entidades adicionales declaradas en `entityCatalog.spawnableEntityIds`.

`base:game-mode.normal` hereda actualmente:

- `unit.humanoid.default`
- `unit.humanoid.worker`
- `building.mercenary`

Un escenario normal no necesita repetirlas. Puede añadir contenido mediante
`spawnableEntityIds`.

Un modo importado, como Kodo Tag, no hereda unidades del modo normal. Su paquete
declara sus propias entidades. Si un escenario necesita aislarse incluso de los
defaults de su modo, puede usar:

```json
"entityCatalog": {
  "includeGameModeDefaults": false,
  "spawnableEntityIds": [
    "minigame.kodo-tag:unit.kodo.basic"
  ]
}
```

El comando `/spawn`, las oleadas, reglas y construcciones siguen pasando por
`EntityLifecycleService`; solo pueden crear definiciones presentes en este
catálogo de partida.
