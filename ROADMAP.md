# Healthspan API — roadmap

Short, honest plan of what we are building next. The public issue tracker
and these items are the source of truth for priorities.

## Now

- [ ] Slim + paginated `ExerciseTypes` endpoint (the full catalogue is 1.4 MB / ~1,000 rows; offline hydration moves to a background call so first render stays under 3 s).
- [ ] Offline-first catalogue hardening: the PWA mirror keeps fetching the full catalogue in the background, decoupled from render.
- [ ] CSV workout-import UX round: bulk import path and import progress feedback.

## Next

- [ ] Weekly training report: generation endpoint + weekly metrics rollup (muscle load, adherence, progression).
- [ ] Guided follow-along sessions (series/reps pacing UI backed by the workouts API).
- [ ] QA seeding hardening: dev-only seeded login with stronger environment gating and docs.

## Later / ideas

- [ ] Return created resource ids on all POST/PUT endpoints.
- [ ] RPE-based progression helpers (see https://barbend.com/how-to-use-rpe-scale-strength-training/).
- [ ] Ollama as an alternative marker-normalization provider alongside OpenRouter.
- [ ] Exercise variant search quality.
