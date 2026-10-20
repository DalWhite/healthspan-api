# Diseño: tracker universal de entrenamiento + readiness

Aprobado por Wall 2026-10-20. Complementa las prescripciones (t_be7f749a): las rutinas estructuradas conviven con el registro flexible de sesiones no estructuradas (funcional, pliométricos, movilidad, Jiu-Jitsu…). Objetivo final: log estructurado → readiness → planificación (futuro con LLM).

## 1. Principio rector

Todo entreno se registra (rápido, ~10s por bloque), con la estructura que haga falta y solo la que haga falta. El log estructurado es el combustible del planificador futuro.

## 2. Adaptaciones: las 9 de Andy Galpin (orden canónico)

Fuente: Huberman Lab Guest Series (Dr. Andy Galpin), "How to Assess & Improve All Aspects of Your Fitness". El ORDEN importa: adaptaciones cercanas son compatibles de entrenar en paralelo; lejanas interfieren (Chart of Interference). La salud no es una adaptación: es resultado.

| # | Adaptación | Ejemplos de estímulo |
|---|-----------|---------------------|
| 1 | Habilidad / técnica | muscle-up intentado, técnica de anillas, Jiu-Jitsu |
| 2 | Velocidad | sprints cortos, cambios de ritmo |
| 3 | Potencia | saltos al banco, pliométricos, lanzamientos |
| 4 | Fuerza máxima | sentadilla/banco pesado, 1-5 reps |
| 5 | Hipertrofia | 6-12 reps con carga progresiva |
| 6 | Resistencia muscular | altas reps, circuitos locales |
| 7 | Capacidad anaeróbica | esfuerzos all-out de ~10s-2min |
| 8 | Capacidad aeróbica máxima | VO₂max, intervalos intensos |
| 9 | Resistencia de larga duración | rodar, nadar, caminata larga, JJ de rolado largo |

Mobilidad/postura NO son adaptación Galpin: se modelan como objetivo de sesión y mapean a Habilidad (1) + resistencia muscular (6) según caso.

## 3. Modelo de datos (extiende lo actual, no reemplaza)

### Workout (sesión) — columnas nuevas
- `Goal` (string, objetivo principal: explosividad, movilidad, postura, hipertrofia, técnica, resistencia, "no fatigar"…)
- `SecondaryGoals` (JSON array de strings)
- `Modality` (enum: Fuerza, Funcional, Pliometria, Movilidad, JiuJitsu, Tenis, Kitesurf, Cardio, Caminata, Otro)
- `PerceivedFatigue` (int 1-5, se pide al cerrar la sesión)

### WorkoutBlock (nueva)
- Id, WorkoutId (FK cascade), Order (1..n denso), Type (Normal|Superset|Circuito|EMOM|AMRAP|Tabata|Ronda), Label?, Rounds? (int)
- Agrupa ejercicios; un ejercicio sin bloque pertenece a un bloque Normal implícito.

### Exercise — columnas nuevas
- `BlockId` (nullable FK), `Order` (denso dentro del bloque)
- `Attempts` (int?, intentos total) y `CompletedAttempts` (int?) — "muscle-up 8/0" es un registro válido
- `QualityIntent` (string?: "100% explosividad", "controlado", "isométrico 30s")
- `LoadType` (enum: Bodyweight|External|Band|Kettlebell|Machine|Poles|Other)
- Series/Repetitions/Weight existentes pasan a ser OPCIONALES (ya lo son en la UI; asegurar validación laxa: al menos una señal de volumen: series×reps, attempts, o duración).

### Adaptaciones (nuevas tablas)
- `Adaptation` enum 1-9 (tabla seed con orden + nombre + color).
- `ExerciseTypeAdaptation` (ExerciseTypeId, AdaptationId, Role Primary|Secondary): mapeo canónico por tipo de ejercicio, seed inicial heurístico desde tags/musculatura existentes (pliometría→Potencia; sentadilla pesada→Fuerza; etc.), editable.
- `ExerciseAdaptation` (ExerciseId, AdaptationId): override por sesión cuando el usuario declara la intención (salto al banco hecho como vuelta a la calma → Resistencia muscular, no Potencia). Sin override se usa el mapeo del tipo.

### Readiness (derivado, no almacenado)
`GET /readiness` → { score 0-10, fatigueLoad7d, perAdaptation: {volume7d, lastSessionDate, trend}, suggestions[] }
- Reglas v1 (sin LLM): fatiga media reciente + volumen 7d por adaptación + días desde última sesión por modalidad + principio de interferencia (no sugerir Fuerza máxima si Capacidad aeróbica alta ayer; sugerir la adaptación peor cubierta que sea compatible con la fatiga actual).
- v2 (futuro): el mismo JSON alimentará a un LLM para planificar la semana.

### Cobertura
`GET /adaptations/coverage?from&to` → volumen por adaptación (proxy: series×reps×load para fuerza/hipertrofia; attempts para habilidad; duración/intensidad para cardio) + objetivo semanal configurable por adaptación (0 = auto según edad/objetivos, v2).

## 4. Endpoints nuevos

- `POST/PATCH /workouts` extendido con goal/modality/fatigue/blocks/exercises flexibles (payload offline-first ya soporta: se añade al sync op `workout` la nueva forma).
- `GET /readiness`
- `GET /adaptations/coverage`
- `GET /adaptations` (catálogo 9 + mapeos editables por admin)

## 5. UX (registro rápido)

1. Nueva sesión: elegir modalidad (chips, recientes primero) + objetivo principal (chips sugeridos) — 2 toques.
2. Bloques: "+ bloque" (tipo por defecto Normal; superserie/circuito en menú). Ejercicios con autocomplete; series/reps opcionales; "+ intento" para intentos fallidos; chips de intención (explosivo/controlado/etc.).
3. Cierre: slider de fatiga 1-5 + nota opcional. Meta: <60s registrar un entreno funcional completo.
4. Dashboard: radar de 9 ejes (cobertura semanal vs objetivo) + readiness con sugerencia del día siguiente.

## 6. Offline-first

Todo lo nuevo entra en el mirror SQLite local + sync_queue igual que workouts actuales (op `workout` ampliado con blocks/goal/fatigue; migración de schema local incluida).

## 7. Fases

1. API: modelo + migración + endpoints (tarjeta t_api_tracker).
2. FE: registro rápido (tarjeta t_fe_log).
3. FE: radar cobertura + readiness (tarjeta t_fe_radar).
4. (Futuro) LLM planner leyendo /readiness + /coverage.
