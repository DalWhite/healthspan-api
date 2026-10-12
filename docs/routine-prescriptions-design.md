# Diseño: prescripciones ordenadas en rutinas

**Estado:** propuesta para aprobación de Wall. Sin código de producción.
**Tarjeta origen:** t_1c214694. **Desbloquea:** t_bbd0674b (vista de rutina trainee), t_aae5f780 (adherencia/weakness), t_c24b990a (editor trainer).
**Fecha:** 2026-10-12.

## 0. Decisiones cerradas (vinculantes para las tarjetas hijas)

1. La prescripción ordenada se modela como **entidad relacional nueva** `WorkoutTemplateExercise` (una fila por ejercicio ordenado dentro de `WorkoutTemplate`). Se **elimina** la join table implícita `ExerciseTypeWorkoutTemplate` y se **abandona** el intento en curso de guardar instrucciones como JSON (`WorkoutTemplate.ExerciseInstructions`): no puede expresar orden ni sets/reps/load y crearía dos fuentes de verdad.
2. `Apply to Today` **copia** la prescripción al workout del día como ejercicios normales y editables. **Nunca auto-confirma**: la finalización del entreno pasa a ser un acto explícito (`CompletedAt`), no una inferencia por "tiene ejercicios".
3. Versionado mínimo: `Routine.Version` (entero, +1 en cada edición). Las tarjetas hijas no introducen historial de versiones de plantilla.
4. Atajo de creación de rutina = **"Guardar como rutina" desde History** (un workout real ya logueado). No se construye un wizard de cero en la primera versión.
5. Terminología de usuario: "Rutina", "Entreno de hoy", "Repetir entreno". Los conceptos internos (`WorkoutTemplate`, `WorkoutTemplateExercise`, "prescripción") **nunca** aparecen en la UI.

## 1. Estado actual (ground truth verificado en main)

- `Routine` (Guid Id, Name, Description, UserId, TrainerId*, Status, CompletionDate) → `WorkoutTemplate` vía join table implícita `RoutineWorkoutTemplate`.
- `WorkoutTemplate` (int Id, Type, DaysOfWeek=frecuencia semanal, Goal) → `ExerciseType` vía join table implícita `ExerciseTypeWorkoutTemplate` (snapshot EF, tablas `WorkoutTemplate` y `ExerciseTypeWorkoutTemplate`). **Sin orden, sin sets/reps/load/rest.**
- `RoutinesController` solo tiene `GET /routines`, `GET /routines/plan` (ya resuelve frecuencia/próximo entreno vía `RoutinePlanService`) y `POST /routines`. No hay PUT/DELETE ni GET por id.
- `GET /workouts/adherence` ya existe (`RoutineAdherenceService`), pero mide adherencia solo por `Type`+frecuencia.
- Today: `GET/POST /workouts/today`, `POST /workouts/{id}/exercises`, `POST /workouts/{id}/duplicate` (feature ya integrada). Un workout se considera "completado" cuando `Exercises.Count > 0` (`GetCompletedWorkoutsSinceAsync`).
- En el working tree de healthspan-api hay cambios sin commitear que añaden `ExerciseInstructions` como JSON (`MainDbContext` lo `.Ignore()`). Este diseño los **reemplaza**; el autor debe consolidar antes de implementar.

Por qué el modelo actual garantiza experiencia rota: `Apply` sobre una lista desordenada de ExerciseTypes sin carga asignada produce un "entreno" vacío de significado — el usuario ve nombres sin saber cuántos sets, con qué peso, en qué orden. Es exactamente la condición que `frontend-ux-improvement-plan.md` (líneas 246-247) exige resolver antes de mostrar rutinas.

## 2. Modelo de datos

### 2.1 Nuevas/actualizadas entidades

```csharp
public class WorkoutTemplateExercise
{
    public int Id { get; set; }
    public int WorkoutTemplateId { get; set; }        // FK -> WorkoutTemplate (cascade)
    public int ExerciseTypeId { get; set; }           // FK -> ExerciseType (restrict)
    public int Order { get; set; }                    // 1..n dentro del template
    public int? Sets { get; set; }
    public int? Reps { get; set; }                    // "8-12" se guarda como texto en Notes; Reps = objetivo numérico
    public double? LoadKg { get; set; }               // carga sugerida (kg); null = "a sensación"
    public int? RestSeconds { get; set; }
    public string? Notes { get; set; }                // explicación del coach para este ejercicio (reemplaza el JSON ExerciseInstructions)
}
```

`WorkoutTemplate` gana `List<WorkoutTemplateExercise> Exercises` y pierde la colección `ExerciseTypes` (la join table implícita desaparece) y el JSON `ExerciseInstructions`.

`Routine` gana `public int Version { get; set; } = 1;`.

`Workout` gana `public DateTime? CompletedAt { get; set; }` (ver §3.4; necesario para que "apply ≠ completar" no rompa adherencia).

`Exercise` gana `public int? SourceTemplateExerciseId { get; set; }` (nullable, FK restrict con `SetNull`: si se borra la plantilla, el ejercicio logueado sobrevive). Es el linaje que permite a adherencia comparar prescrito↔ejecutado.

### 2.2 Reglas

- `Order` es denso (1..n) y único por template: índice único `(WorkoutTemplateId, Order)`. El FE renumera al reordenar; el BE revalida.
- Todos los campos de prescripción son **opcionales**: una plantilla puede tener solo orden+notas (v1 del trainer) y aun así ser válida. Un template con 0 ejercicios no puede aplicarse (error específico, §3.3).
- Un `ExerciseType` puede aparecer varias veces en el mismo template (p. ej. sentadilla en bloque A y B) — la join table implícita actual no lo permite; la entidad nueva sí.
- Versionado: `PUT /routines/{id}` reemplaza la estructura completa (templates+ejercicios) y hace `Version++`. Optimistic concurrency barato: el FE envía `If-Match: {version}`; 409 con mensaje "La rutina cambió desde que la abriste. Recarga para ver la versión actual." Sin historial de versiones en v1 (YAGNI; los workouts logueados son el historial real vía `SourceTemplateExerciseId`).

### 2.3 Por qué relacional y no JSON

JSON en `WorkoutTemplate` (a) no permite índices ni unicidad de orden, (b) obliga a reescribir el payload completo en cada microedición, (c) no da FK para `SourceTemplateExerciseId` en `Exercise`, que es lo que hace posible la métrica prescrito↔ejecutado de t_aae5f780. EF Core 9 + Pomelo soportan JSON, pero aquí la necesidad es relacional desde el principio.

## 3. Contrato API

### 3.1 Recursos

```csharp
public class RoutineResource
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public string UserId { get; set; }
    public string? Status { get; set; }
    public int Version { get; set; }
    public List<WorkoutTemplateResource> WorkoutTemplates { get; set; }
}

public class WorkoutTemplateResource
{
    public int Id { get; set; }
    public string Type { get; set; }
    public int FrequencyPerWeek { get; set; }   // = DaysOfWeek, renombrado en el contrato para claridad
    public string? Goal { get; set; }
    public List<ExercisePrescriptionResource> Exercises { get; set; }
}

public class ExercisePrescriptionResource
{
    public int Id { get; set; }
    public int ExerciseTypeId { get; set; }
    public string ExerciseTypeName { get; set; }  // el FE no hace una segunda llamada para pintar nombres
    public int Order { get; set; }
    public int? Sets { get; set; }
    public int? Reps { get; set; }
    public double? LoadKg { get; set; }
    public int? RestSeconds { get; set; }
    public string? Notes { get; set; }
}
```

### 3.2 CRUD de rutinas (RoutinesController)

| Verbo | Ruta | Comportamiento |
|---|---|---|
| GET | `/routines?traineeId=` | Lista (ya existe); ahora incluye `Id`, `Version` y ejercicios ordenados. |
| GET | `/routines/{id}` | Detalle completo. 404 si no existe o no accesible (`TrainingAccessService`). |
| POST | `/routines` | Crea con templates+prescripciones. Valida: nombre 2-100 chars único por usuario (misma convención que ExerciseTypes, t_aa075fb5); `FrequencyPerWeek` 1-7; `Order` denso 1..n; `Sets/Reps ≥ 1`, `LoadKg ≥ 0`, `RestSeconds ≥ 0`. 201 + `RoutineResource` (el FE necesita el id para seguir editando sin recargar la lista). |
| PUT | `/routines/{id}` | Reemplazo completo + `Version++`. Respeta `If-Match` → 409. Mismas validaciones que POST. |
| DELETE | `/routines/{id}` | Borra rutina y templates. Los `Workout` logueados **no** se tocan (`SourceTemplateExerciseId` → null por `SetNull`). |

Errores: `ProblemDetails` con `title` específico por campo ("El nombre de la rutina debe tener entre 2 y 100 caracteres", "Ejercicio duplicado en la posición 3: elige otro orden u otro ejercicio"), nunca un 500 genérico. El FE pinta cada error inline junto al campo (regla 5 de ux-ui-good-apps).

### 3.3 Apply to Today (el endpoint crítico)

```
POST /workouts/apply-template
{ "routineId": "guid", "templateId": 42, "date": "2026-10-12" }   // date opcional = hoy
→ 200/201 WorkoutSessionResource   (mismo shape que GET /workouts/today)
```

Semántica (transaccional, una sola llamada):

1. Resuelve el workout del día vía la mecánica `DailySessionKey` existente (`GetOrCreateWorkoutSessionAsync`); si el día ya tiene sesión, se **añade** a ella; si no, se crea.
2. Copia cada `WorkoutTemplateExercise` (en `Order`) como un `Exercise` normal del workout: `Series=Sets`, `Repetitions=Reps`, `Weight=LoadKg`, `Comments=Notes`, `SourceTemplateExerciseId=Id`. **No** se copia `RestSeconds` a `Exercise` (no hay campo hoy; el FE lo muestra como texto en la tarjeta del ejercicio desde la respuesta del apply, que incluye la prescripción original).
3. **Idempotencia:** si el `ExerciseType` ya existe en la sesión del día, se **omite** (no duplica). Si todos los ejercicios ya estaban, devuelve 200 con la sesión tal cual. Esto hace que "Aplicar" sea seguro de pulsar dos veces desde el móvil.
4. **Nunca auto-confirma:** `CompletedAt` sigue null. El apply crea/rellena un borrador editable.
5. Template sin ejercicios → 400 "Esta rutina todavía no tiene ejercicios. Añade al menos uno antes de aplicarla." (mensaje constructivo, con el arreglo).

Respuesta = `WorkoutSessionResource` completo → el FE navega directamente a Today en modo edición sobre ese workout. El usuario aterriza **en su entreno**, no en un catálogo (regla 2).

### 3.4 Confirmar entreno (cambio necesario en Today)

```
POST /workouts/{id}/complete   → 200 WorkoutSessionResource (CompletedAt = now)
```

Hoy "completado" se infiere de `Exercises.Count > 0` (`GetCompletedWorkoutsSinceAsync`, usado por plan y adherencia). Con apply creando sesiones precargadas, esa inferencia contaría como cumplido un entreno solo aplicado. Cambio: la inferencia pasa a `CompletedAt != null`. Impacto: `WorkoutsRepository.GetCompletedWorkoutsSinceAsync` y `RoutineAdherenceService` (una condición cada uno). El flujo actual de logging no se rompe: al confirmar el último ejercicio el FE llama a complete; si el usuario nunca confirma, History muestra el workout como "en curso" (estado vacío intencional, no error).

### 3.5 Guardar como rutina (desde History)

```
POST /workouts/{id}/save-as-routine
{ "name": "Empuje martes" }        // name opcional = sugerido por FE desde Type+fecha
→ 201 RoutineResource
```

Crea una rutina de un solo template: `Type` = el del workout, `FrequencyPerWeek = 1`, ejercicios en el orden logueado, prescripción inicial = los valores reales del día (`Series/Repetitions/Weight` → `Sets/Reps/LoadKg`). El FE navega al editor de rutina con todo prellenado y editable. Este es el atajo intuitivo que conecta la feature de duplicar (t_9780ea5f, ya integrada) con las rutinas: duplicar = repetir una vez; guardar como rutina = repetir siempre.

## 4. UX

### 4.1 Crear una rutina (desde History — el camino principal)

1. History → workout → acción **"Guardar como rutina"** (junto a "Duplicar", mismo patrón de descubrimiento).
2. Modal de 1 campo: nombre (prellenado "Full body — 7 oct"). Un campo, no un wizard.
3. Aterriza en **editor de rutina**: lista ordenada de tarjetas de ejercicio (drag/reorder, campos sets/reps/rest/notes detrás de un "Editar" por tarjeta — progressive disclosure: la vista compacta muestra "4×8 · 60 kg"). Guardar arriba a la derecha.
4. Sin wizard de cero en v1. Crear rutina vacía desde Training queda como ruta secundaria ("+ Rutina") con el mismo editor.

### 4.2 Today consume la prescripción sin romper el logging

- Training home ya muestra el plan (`GET /routines/plan` existe). Cuando hay un entreno `DueNow`, CTA **"Empezar entreno de hoy"** → `POST /workouts/apply-template` → navegación a Today con la sesión precargada.
- Today **no cambia su flujo de logging**: los ejercicios aplicados son `Exercise` normales, editables, borrables, con los valores de prescripción prellenados como sugerencia. El usuario puede ignorarlos y registrar lo que hizo. La prescripción es un valor por defecto, nunca una jaula.
- Al confirmar → `POST /workouts/{id}/complete` → toast + landing en Training con el plan actualizado (el entreno deja de estar "due"). Aterriza de vuelta en contexto, no en catálogo.
- Diferencia visual prescrito↔registrado: los campos prellenados se muestran como valor editable normal (placeholder visual "sugerido: 60 kg" solo si el usuario lo borra). Nada de modo "solo lectura" — eso rompería el flujo existente.
- Regla de oro: **apply nunca aparece como paso obligatorio**. Si el usuario pulsa "+ Ejercicio" directamente, Today funciona exactamente igual que hoy. Las rutinas son un acelerador, no un portero (regla 1: activación antes que explicación).

### 4.3 Trainer ↔ trainee (una sola pantalla, dos vistas)

Mismos componentes del editor; el trainer edita la rutina de un trainee (flujo trainer→trainees ya existe; `TrainerId` ya está en el modelo) y el trainee ve la misma rutina en modo lectura + CTA aplicar. Los `Notes` por ejercicio son las "explicaciones" que pide t_c24b990a — un solo campo, sin sistema aparte.

## 5. Migration plan (EF Core 9 + Pomelo/MySQL)

Migración única `AddOrderedExercisePrescriptions`, reversible:

1. **Up:**
   - `AddColumn Version (int, NOT NULL, default 1)` a `Routines`.
   - `AddColumn CompletedAt (datetime, nullable)` a `Workouts`.
   - Crear tabla `WorkoutTemplateExercises` (PK int, FKs: `WorkoutTemplateId` cascade, `ExerciseTypeId` restrict; índice único `(WorkoutTemplateId, Order)`; índice `ExerciseTypeId`).
   - **Backfill** desde `ExerciseTypeWorkoutTemplate`: `INSERT INTO WorkoutTemplateExercises (WorkoutTemplateId, ExerciseTypeId, "Order") SELECT ..., ROW_NUMBER() OVER (PARTITION BY WorkoutTemplateId ORDER BY ExerciseTypeId) FROM ExerciseTypeWorkoutTemplate` (orden arbitrario pero denso y determinista; prescripciones null). Aceptable porque hoy ninguna pantalla expone rutinas.
   - `AddColumn SourceTemplateExerciseId (int, nullable)` a `Exercises` con FK `SetNull`.
   - Drop `ExerciseTypeWorkoutTemplate`.
2. **Down:** recrear la join table desde `WorkoutTemplateExercises` (sin orden ni prescripción), drop nueva tabla y columnas. `CompletedAt`/`SourceTemplateExerciseId` se conservan en down para no perder datos de logging.
3. **Sin romper datos existentes:** ningún workout, exercise ni exercise type se modifica ni borra. Los `Routine` existentes conservan su estructura vía backfill.
4. Consolidación previa requerida: el working tree con `ExerciseInstructions` JSON debe resolverse (descartado por este diseño) antes de generar la migración, para no snapshotear un modelo en tránsito.
5. Orden de implementación sugerido para las tarjetas hijas: migración + CRUD (t_c24b990a BE) → apply + complete (t_bbd0674b BE) → vistas FE → adherencia sobre prescripción (t_aae5f780, último: es el único que necesita linaje completo).

## 6. Criterios de aceptación UX (checklist ux-ui-good-apps)

- [ ] **Activación:** un usuario con una rutina asignada puede completar su entreno de hoy en ≤4 taps desde Training home: CTA "Empezar entreno" (1) → Today precargado (2) → ajustar/confirmar (3-4). Sin decisiones previas (no elige plantilla, no confirma fecha, no ve un catálogo).
- [ ] **Guía, no biblioteca:** apply devuelve al usuario a Today en edición; ningún flujo de rutina termina en una lista de opciones.
- [ ] **Progressive disclosure:** la tarjeta de ejercicio del editor muestra nombre+orden; sets/reps/rest/notes tras "Editar". Cada pantalla = una decisión (nombre en el modal; luego una tarjeta a la vez).
- [ ] **Flujo corto:** crear rutina desde History = 2 taps + 1 campo. Editor sin campos obligatorios más allá del nombre.
- [ ] **Errores:** inline, on blur, específicos, no culpabilizadores, input preservado, color+icono+texto. 409 de versión y 400 de plantilla vacía tienen mensaje con arreglo. Prohibido el snackbar genérico "No se pudo guardar".
- [ ] **Mecánicas de hábito:** la adherencia (t_aae5f780) se construye sobre este core y debe mostrar personal antes que social, tono neutro, y gracia (un día perdido no borra el progreso mostrado). Ningún streak/badge en esta fase.
- [ ] **Mobile:** targets 44px en reorder y CTA; estados loading/empty/error intencionales en Today precargado (sesión aplicada pero vacía → empty state con "Añade ejercicios", no pantalla rota).
- [ ] **Regla anti-regresión:** el flujo actual de logging sin rutinas (hoy, la activación del producto) no puede empeorar en taps ni en estados.

## 7. Fuera de scope (explícito)

- Prescripciones por semana/bloque (periodización), RPE, series warm-up estructuradas, plantillas compartidas entre usuarios, historial de versiones de plantilla, backfill de rutinas desde histórico de workouts.
- UI de trainer para asignar rutinas a múltiples trainees a la vez.

## 8. Riesgos / abiertos para Wall

1. **`CompletedAt` cambia la definición de "entreno completado"** para plan y adherencia existentes. Es la decisión con más impacto de este doc; si Wall prefiere no tocarlo, apply debería crear sesión solo al primer registro manual — pero entonces pierde la precarga, que es el valor central del feature. Recomendación: adoptarlo.
2. Renombrar `Frequency`→`FrequencyPerWeek` en el contrato rompe el FE actual de `AddRoutineResource`; mínimo, pero hay que hacerlo en la misma iteración.
3. El working tree sin commitear de healthspan-api (JSON `ExerciseInstructions`) choca con este diseño: hay que descartarlo o consolidarlo antes de implementar (hotspot: `Models/WorkoutTemplate.cs`, `Persistence/MainDbContext.cs`).
