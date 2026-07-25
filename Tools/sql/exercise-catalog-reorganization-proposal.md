# Exercise Catalog Reorganization

## Current state

Phase 1 was committed to `healthspan-dev` on 2026-07-25.

- Backup: `healthspan-dev-20260725-123320.sql`
- SHA-256: `77418932DB3C034D5A4B85AABB9B00BFBB16DBFAC8E0B92A78DAD604D3C3791A`
- Catalog: `76 → 72`
- History: `2072 → 2072`; eight rows were repointed without changing their IDs
- Favorites: `13 → 13`
- Templates: `0 → 0`
- Muscle assignments: `151 → 143` after duplicate links collapsed
- Integrity issues: `0`

Executed merges:

| Removed source | Canonical target |
|---|---:|
| `53` Face pull | `21` Face pull |
| `71` Tríceps extensión polea | `950` Extensión de tríceps en polea |
| `26` Press pecho mancuernas | `37` Press de banca con mancuernas |
| `49` Kettlebell - Sentadilla | `68` Sentadilla goblet con kettlebell |

The exact atomic migration is retained in
[`exercise-catalog-phase1-apply.sql`](exercise-catalog-phase1-apply.sql).
It is an execution record and must not be rerun on the current database.

## Phase 2 — pending

Run a fresh audit and create a new read-only preview before implementing Phase 2.
Do not reuse the Phase 1 baseline.

### Variation hierarchy

These exercises remain distinct; only `ParentExerciseTypeId` should change.

| Parent | Proposed children |
|---:|---|
| `7` | `24`, `27` |
| `8` | `72` |
| `9` | `31`, `34`, `43`, `67`, `948` |
| `10` | `41`, `64`, `65`, `946`, `947`, `951`, `952` |
| `12` | `33` |
| `16` | `14`, `51`, `55`, `60`, `68` |
| `17` | `28`, `50` |
| `18` | `54`, `70`, `954` |
| `32` | `949` |
| `35` | `58`, `63` |
| `48` | `46` |
| `59` | `66` |

Keep the existing `23`, `36`, and `37` children of `12`.

### Muscle repairs

`P` means primary and `S` means secondary:

```text
52: P6, S21       56: P8, S12, S10    58: P9, S13
61: P14, S12      62: P12, S10        63: P16, S13
66: P7, S5        69: P8              70: P7, S1
73: P10, S11      74: P11             946: P18, S7, S1
954: P7, S1
```

Exercise `70` also needs the incorrect muscle links `4`, `5`, and `9` removed.

### Naming normalization

```text
8 Dominadas; 9 Curl de bíceps; 17 Peso muerto; 18 Jalón al pecho
23 Press de banca con kettlebell; 24 Press militar con kettlebell
31 Curl de bíceps colgado; 35 Elevaciones laterales
38 Extensión de tríceps con kettlebell; 41 Remo con kettlebell
42 Press de pecho; 43 Curl de bíceps en polea
46 Clean and press unilateral con kettlebell; 47 Puente de glúteos
50 Peso muerto rumano; 54 Jalón al pecho con agarre ancho
55 Sentadilla búlgara; 58 Elevaciones frontales; 60 Sentadilla sumo
64 Remo con barra; 65 Remo unilateral en polea; 67 Curl martillo
69 Extensión de cuádriceps; 70 Jalón al pecho supino
73 Curl femoral tumbado en máquina; 74 Elevación de gemelos en máquina
947 Remo en polea; 948 Curl crossover de bíceps
949 Hip thrust en máquina; 951 Remo unilateral con mancuerna
952 Remo abierto en polea; 953 Around the world de hombros
954 Jalón al pecho con agarre ancho en máquina
```

### Manual decisions

Do not merge these without confirmation:

| Records | Decision needed |
|---|---|
| `44`, `950` | Same cable-triceps exercise or distinct attachments? |
| `54`, `954` | Duplicate wide-grip pulldown or machine variation? |
| `40`, `947` | Does vague “Polea” history represent cable rows? |
| `46`, `48` | Unilateral versus bilateral, or duplicate? |
| `57`, `953` | Kettlebell halo versus shoulder around-the-world? |
| `47`, `32` | Distinct bridge/thrust movements or a variation family? |
| `74`, `19` | Same calf machine pattern or distinct machines? |
| `42`, `12` | Confirm `42` is a machine chest press before grouping. |

## Safety rules

- Take a new backup before every write phase.
- `MERGE` may repoint `Exercises.TypeId`, but must preserve every `Exercises.Id`.
- `VARIATION` keeps both catalog records and history separate.
- Use one atomic transaction with preconditions and post-commit integrity checks.
- Require unchanged history count/checksum and zero orphan references.
