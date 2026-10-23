# Importación CSV: baseline, targets y contrato de progreso

Tarea: t_37acc65b · 2026-10-23 · @pau
Repo: healthspan-api · Rama: feature/csv-import-baseline-t_37acc65b

## 1. Pipeline actual (cómo funciona hoy)

Endpoint único y síncrono: `POST /data/import` (multipart, un solo archivo).

Flujo (`Controllers/DataController.cs:52`):

1. `CsvService.ReadCsvExercisesAndGroupByDate(file)` — parseo CsvHelper fila a fila,
   agrupando por fecha. **Por cada fila**:
   - `EnsureExerciseTypeExists(name)` → 1 SELECT contra `ExerciseTypes` + 1 `SaveChanges()`
     si el tipo es nuevo (`Services/CsvService.cs`).
2. Por cada grupo de fecha: `WorkoutsRepository.GetOrCreateWorkoutSessionAsync` →
   transacción **Serializable** + semaphore por usuario/día (`WorkoutsRepository.cs:230`).
3. Por cada ejercicio: `ExercisesRepository.AddExercise` →
   - `CalculateComputedValues` → 1 SELECT del `WeightType` del tipo + **1 SELECT que carga
     TODO el histórico de `MaxRepetition` del tipo/usuario** (`ToList().Max()`, sin índice
     en `TypeId` — `MainDbContext.cs` solo indexa `ClientEntityId` en `Exercise`)
     (`ExercisesRepository.cs` CalculateComputedValues)
   - `context.SaveChanges()` → **1 round-trip de insert por fila**.

Coste por fila ≈ 3-4 queries + 1 insert + escaneo creciente del histórico →
superlineal (≈O(N²) en filas leídas).

Validación y persistencia:

- Fechas: solo `dd/MM/yy` exacto (`TypeConverterOptions Formats=["dd/MM/yy"]`).
  Un mes/día sin cero (`15/4/18`) lanza `TypeConverterException` no capturada → **HTTP 500,
  importación completa abortada**, sin errores por fila. Verificado empíricamente en el
  benchmark (fila `15/4/18` → fallo total).
- No hay límite de tamaño de archivo (más allá del default de Kestrel, 128 MB multipart).
- No hay idempotencia: reimportar el mismo CSV duplica todas las filas.
- Sin job ni progreso: el FE (`healthspan-fe/Pages/Settings.razor:166`) hace un único
  `await Http.PostAsync` con `HttpClient.Timeout = 300 s` (default en
  `healthspan-fe/Program.cs:66`). Con importaciones > ~300 s el FE muestra "Import error"
  mientras el servidor aborta a mitad (el `CancellationToken` del request cancela la
  importación) → **importación parcial sin rollback** (cada SaveChanges es independiente).

Tamaño típico real: `Tools/exercises-to-11-4-2024.csv` = 435 filas, 37 tipos
(importación histórica del usuario).

## 2. Baseline medido

Entorno: esta máquina (beelink MiniPC), MySQL 8.4 en Docker (`healthspan-mysql`),
pipeline real completo vía HTTP (WebApplicationFactory → `POST /data/import` → MySQL),
DB dedicada `healthspan_bench_*` con las mismas migraciones que dev.

Dataset representativo: determinista (seed fijo), 37 tipos reales, fechas 2018→2026,
series 3-5, reps 5-12, peso 20-140.

| Filas | Tiempo total | Filas/s | Persistidas |
|------:|-------------:|--------:|------------:|
| 1 000 | 154 s | 6.5 | 1 000 |
| 5 000 | 1 719 s (28,6 min) | 2.9 | 5 000 |
| 10 000 | __10K_TIME__ | __10K_RPS__ | 10 000 |

Lectura: ya a 5 000 filas la importación **supera de largo el timeout del FE (300 s)** —
hoy una importación de 5k es imposible de completar desde la UI. El coste por fila crece
con N (154 ms/fila a 1k → 344 ms/fila a 5k) por el escaneo de histórico sin índice.

Harness reproducible: `Tests/healthspan-api.tests/Benchmarks/CsvImportBaselineBenchmark.cs`
(opt-in, nunca corre en CI normal):

```
BENCHMARK_DB_CONNECTION_STRING='<conn dev>' \
BENCHMARK_RESULTS_FILE=/tmp/csv-bench.jsonl \
dotnet test Tests/healthspan-api.tests -c Release \
  --filter "FullyQualifiedName~CsvImportBaselineBenchmark"
```

(Requiere que el usuario MySQL tenga grants sobre `healthspan_bench%`; ya concedidos en
el MySQL dev.)

## 3. Targets

| Métrica | Target | Criterio |
|---|---|---|
| 10 000 filas | **< 15 s** server-side (~700 filas/s) | benchmark mismo harness |
| 50 000 filas | < 60 s server-side | escalado lineal, no cuadrático |
| UI | nunca bloqueada; progreso visible < 1 s tras subir | job asíncrono + polling |
| Timeout FE | sin timeouts: la importación no depende de mantener el request vivo | 202 + job id |
| Cancelación | cerrar la UI o cancelar no deja datos parciales inconsistentes | job transaccional por lotes con estado |
| Errores | 0 filas corruptas silenciosas: error por fila con número de línea | validación por lote |

Nota de realismo: 700 filas/s exige batching (SaveChanges por lote de ~500-1000,
lookup de tipos precargado en memoria, 1 query de histórico por tipo en vez de por fila,
índice compuesto en `Exercises(TypeId, WorkoutId)`). Es factible con EF Core sin
dependencias nuevas.

## 4. Contrato mínimo de API para progreso

```
POST /data/import                 (multipart file)
  → 202 { "jobId": "…" }                       # acepta y procesa en background
  → 400 { "errors": [{ "row": 12, "message": "Fecha inválida '15/4/18' (formato dd/MM/yy)" }] }
     # validación estructural upfront (cabecera, tamaño máx 5 MB / 50 000 filas)

GET  /data/import/{jobId}         (polling FE cada 1-2 s)
  → 200 {
      "jobId": "…",
      "status": "queued|running|completed|completed_with_errors|failed|cancelled",
      "total": 10000,
      "processed": 4200,
      "errors": [ { "row": 12, "message": "…" } ],   # máx 100; "errorCount" si hay más
      "startedAt": "ISO", "finishedAt": "ISO|null"
    }
  → 404 si el job no existe o no pertenece al usuario

POST /data/import/{jobId}/cancel  (opcional, fase 2)
```

Reglas:

- El job es por usuario; `jobId` solo visible para su dueño.
- Persistencia por lotes (batch de ~500-1000 filas por SaveChanges/tx). Un fallo de lote
  no aborta el resto: las filas del lote fallido van a `errors` con su número de fila.
- `processed` avanza al cerrar cada lote (granularidad de lote suficiente para la UI).
- Jobs terminados se purgan (p. ej. a las 24 h). Estado en memoria + tabla es aceptable
  para v1; si se reinicia la API, un job `running` huérfano pasa a `failed` y el FE lo
  muestra.
- FE: `Settings.razor` sube el archivo, guarda `jobId`, poller con barra de progreso
  `processed/total`, lista de errores por fila, botón cerrar/cancelar; la página sigue
  usable (no `await` bloqueante).

## 5. Criterios de aceptación por tarjeta hija

t_c1f8e6ac (BE bulk/batching):
- [ ] 10k filas < 15 s en el mismo harness (benchmark actualizado pasa).
- [ ] `POST /data/import` devuelve 202 + jobId; `GET /data/import/{jobId}` conforme al §4.
- [ ] Errores por fila (fecha inválida, numérico inválido) no abortan la importación.
- [ ] Índice `Exercises(TypeId, WorkoutId)` (o equivalente) en migración.
- [ ] Sin regresión en tests existentes.

t_d1db9866 (FE progreso):
- [ ] Subida de 10k filas: barra de progreso real, página navegable durante la importación.
- [ ] Errores mostrados con número de fila y mensaje entendible; opción de cerrar sin
  cancelar el job (y ver resultado al volver).
- [ ] Sin timeouts de 300 s: el FE no mantiene el POST vivo hasta el final.

## 6. Riesgos / deudas detectadas (para las tarjetas)

1. `CalculateComputedValues` por fila es el cuello principal (histórico sin índice).
   Con batching hay que recalcular intensidad una vez por tipo al final del job, no por fila.
2. Fechas: aceptar también `d/M/yy` (el CSV histórico real usa meses sin cero) —
   corregir al implementar el lote de validación.
3. Idempotencia: reimportación duplica. Decisión de diseño en t_c1f8e6ac: dedupe por
   (userId, date, exerciseType, series, reps, weight) o `ClientEntityId` sintético por
   importación. No bloquea el baseline.
4. `GetOrCreateWorkoutSessionAsync` con Serializable tx por fecha: con batching, resolver
   los workout sessions una vez por fecha del job (1 query + 1 insert por fecha), no por lote.
