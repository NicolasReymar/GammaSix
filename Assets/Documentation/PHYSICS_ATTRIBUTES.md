# Atributos físicos de entidades

## `physics.solid`

Declara que la entidad bloquea el movimiento de otras entidades.
El campo JSON legado `solid: true` continúa siendo compatible.

## `physics.not_solid`

Anula la solidez de la definición para esa entidad o instancia.
La entidad conserva un collider de tipo `trigger` para poder recibir raycasts
y eventos, pero no bloquea el movimiento.

Ejemplo:

```json
"attributes": [
  "interaction.not_selectable",
  "physics.not_solid"
]
```

La combinación anterior hace que un clic derecho se transforme en una orden de
movimiento al centro de la entidad, sin ejecutar seguimiento ni otra interacción,
y permite que la unidad atraviese el objetivo.

## Overrides de partida

- `override_not_selectable: true`: ignora `interaction.not_selectable`.
- `override_not_solid: true`: ignora `physics.not_solid` y recupera la solidez
  declarada por `solid` o `physics.solid`.

La interacción y la física se evalúan por separado. Una entidad puede ser no
seleccionable pero sólida, o seleccionable y no sólida.
