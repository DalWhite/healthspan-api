/*
  Healthspan daily workout normalization - execution record
  Target: MySQL 8.0.45 / healthspan-dev / VS Code SQLTools

  Applied successfully on 2026-07-28:
  - Duplicate user/day groups merged: 109
  - Workouts: 886 -> 438
  - Duplicate workout shells removed: 448
  - Exercises: 2080 -> 2080
  - Exercises repointed: 412
  - Integrity issues: 0
  - Recorded snapshot: healthspan-dev-20260725-123320.sql
    SHA256: 77418932DB3C034D5A4B85AABB9B00BFBB16DBFAC8E0B92A78DAD604D3C3791A

  Do not rerun this historical migration on the current database.

  Policy:
  - One workout session per user and calendar day.
  - The earliest workout containing an exercise is canonical.
  - Empty shells are considered only when the whole day has no exercises.
  - Workout ID breaks timestamp ties deterministically.
  - Exercise IDs and exercise history are preserved.

  The blocks below are retained as the reviewed execution procedure.
*/

-- @block 1 - Read-only summary
-- @label Preview duplicate daily workouts
WITH workout_counts AS (
    SELECT
        w.Id AS WorkoutId,
        w.UserId,
        DATE(w.Date) AS SessionDate,
        w.Date AS WorkoutTimestamp,
        COUNT(e.Id) AS ExerciseCount
    FROM Workouts w
    LEFT JOIN Exercises e ON e.WorkoutId = w.Id
    WHERE w.Date IS NOT NULL
    GROUP BY w.Id, w.UserId, DATE(w.Date), w.Date
),
ranked AS (
    SELECT
        wc.*,
        ROW_NUMBER() OVER (
            PARTITION BY wc.UserId, wc.SessionDate
            ORDER BY
                CASE WHEN wc.ExerciseCount > 0 THEN 0 ELSE 1 END,
                wc.WorkoutTimestamp,
                wc.WorkoutId
        ) AS WorkoutRank
    FROM workout_counts wc
),
duplicate_days AS (
    SELECT
        UserId,
        SessionDate,
        COUNT(*) AS WorkoutCount,
        SUM(ExerciseCount) AS ExerciseCount,
        SUM(
            CASE WHEN WorkoutRank > 1 THEN ExerciseCount ELSE 0 END
        ) AS ExercisesToRepoint,
        SUM(
            CASE
                WHEN WorkoutRank > 1 AND ExerciseCount = 0 THEN 1
                ELSE 0
            END
        ) AS EmptyDuplicateWorkoutShells
    FROM ranked
    GROUP BY UserId, SessionDate
    HAVING COUNT(*) > 1
)
SELECT
    COUNT(*) AS DuplicateDays,
    COALESCE(SUM(WorkoutCount - 1), 0) AS DuplicateWorkoutShells,
    COALESCE(SUM(ExerciseCount), 0) AS ExercisesPreserved,
    COALESCE(SUM(ExercisesToRepoint), 0) AS ExercisesRepointed,
    COALESCE(
        SUM(EmptyDuplicateWorkoutShells),
        0
    ) AS EmptyDuplicateWorkoutShells,
    'READ_ONLY' AS Persistence
FROM duplicate_days;

-- @block 2 - Read-only merge mapping
-- @label Preview canonical workout mapping
WITH workout_counts AS (
    SELECT
        w.Id AS WorkoutId,
        w.UserId,
        DATE(w.Date) AS SessionDate,
        w.Date AS WorkoutTimestamp,
        w.Type,
        COUNT(e.Id) AS ExerciseCount
    FROM Workouts w
    LEFT JOIN Exercises e ON e.WorkoutId = w.Id
    WHERE w.Date IS NOT NULL
    GROUP BY
        w.Id,
        w.UserId,
        DATE(w.Date),
        w.Date,
        w.Type
),
ranked AS (
    SELECT
        wc.*,
        ROW_NUMBER() OVER (
            PARTITION BY wc.UserId, wc.SessionDate
            ORDER BY
                CASE WHEN wc.ExerciseCount > 0 THEN 0 ELSE 1 END,
                wc.WorkoutTimestamp,
                wc.WorkoutId
        ) AS WorkoutRank,
        SUM(wc.ExerciseCount) OVER (
            PARTITION BY wc.UserId, wc.SessionDate
        ) AS ProjectedExerciseCount
    FROM workout_counts wc
),
canonical AS (
    SELECT
        UserId,
        SessionDate,
        WorkoutId AS CanonicalWorkoutId,
        WorkoutTimestamp AS CanonicalTimestamp,
        Type AS CanonicalType,
        ExerciseCount AS CanonicalExerciseCount
    FROM ranked
    WHERE WorkoutRank = 1
)
SELECT
    source.SessionDate,
    source.UserId,
    canonical.CanonicalWorkoutId,
    canonical.CanonicalTimestamp,
    canonical.CanonicalType,
    canonical.CanonicalExerciseCount,
    source.WorkoutId AS DuplicateWorkoutId,
    source.WorkoutTimestamp AS DuplicateTimestamp,
    source.Type AS DuplicateType,
    source.ExerciseCount AS ExercisesToRepoint,
    source.ProjectedExerciseCount
FROM ranked source
JOIN canonical
    ON canonical.UserId = source.UserId
   AND canonical.SessionDate = source.SessionDate
WHERE source.WorkoutRank > 1
ORDER BY
    source.SessionDate DESC,
    source.UserId,
    source.WorkoutTimestamp,
    source.WorkoutId;

-- @block 3 - Remove stale procedure
-- @label Remove stale daily merge procedure
DROP PROCEDURE IF EXISTS hs_apply_daily_workout_merge_20260728;

-- @block 4 - Install guarded merge
-- @label Install daily workout merge procedure
CREATE PROCEDURE hs_apply_daily_workout_merge_20260728(
    IN p_snapshot_reference VARCHAR(255)
)
BEGIN
    DECLARE v_lock_acquired INT DEFAULT 0;
    DECLARE v_database_name VARCHAR(255);
    DECLARE v_workouts_before BIGINT DEFAULT 0;
    DECLARE v_workouts_after BIGINT DEFAULT 0;
    DECLARE v_history_before BIGINT DEFAULT 0;
    DECLARE v_history_after BIGINT DEFAULT 0;
    DECLARE v_history_checksum_before DECIMAL(65, 0) DEFAULT 0;
    DECLARE v_history_checksum_after DECIMAL(65, 0) DEFAULT 0;
    DECLARE v_duplicate_days BIGINT DEFAULT 0;
    DECLARE v_duplicate_workouts BIGINT DEFAULT 0;
    DECLARE v_exercises_repointed BIGINT DEFAULT 0;
    DECLARE v_workouts_deleted BIGINT DEFAULT 0;
    DECLARE v_integrity_issues BIGINT DEFAULT 0;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        DROP TEMPORARY TABLE IF EXISTS tmp_daily_workout_plan;
        IF v_lock_acquired = 1 THEN
            DO RELEASE_LOCK('healthspan.daily_workout.merge');
        END IF;
        RESIGNAL;
    END;

    IF p_snapshot_reference IS NULL
       OR TRIM(p_snapshot_reference) = ''
       OR p_snapshot_reference = 'REPLACE_WITH_NEW_BACKUP_REFERENCE' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'A new database backup reference is required';
    END IF;

    SELECT GET_LOCK('healthspan.daily_workout.merge', 10)
    INTO v_lock_acquired;

    IF COALESCE(v_lock_acquired, 0) <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Could not acquire the daily workout merge lock';
    END IF;

    SELECT DATABASE() INTO v_database_name;
    IF COALESCE(v_database_name, '') <> 'healthspan-dev' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Wrong database: expected healthspan-dev';
    END IF;

    START TRANSACTION;

    SELECT COUNT(*) INTO v_workouts_before FROM Workouts;
    SELECT COUNT(*) INTO v_history_before FROM Exercises;
    SELECT COALESCE(
        SUM(CRC32(CONCAT(Id, ':', COALESCE(TypeId, -1)))),
        0
    )
    INTO v_history_checksum_before
    FROM Exercises;

    DROP TEMPORARY TABLE IF EXISTS tmp_daily_workout_plan;
    CREATE TEMPORARY TABLE tmp_daily_workout_plan (
        WorkoutId CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
        CanonicalWorkoutId CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
        UserId VARCHAR(255) NOT NULL,
        SessionDate DATE NOT NULL,
        WorkoutTimestamp DATETIME(6) NOT NULL,
        ExerciseCount BIGINT NOT NULL,
        IsCanonical TINYINT(1) NOT NULL,
        PRIMARY KEY (WorkoutId),
        INDEX IX_tmp_daily_workout_canonical (CanonicalWorkoutId),
        INDEX IX_tmp_daily_workout_user_date (UserId, SessionDate)
    ) ENGINE = InnoDB;

    INSERT INTO tmp_daily_workout_plan (
        WorkoutId,
        CanonicalWorkoutId,
        UserId,
        SessionDate,
        WorkoutTimestamp,
        ExerciseCount,
        IsCanonical
    )
    WITH workout_counts AS (
        SELECT
            w.Id AS WorkoutId,
            w.UserId,
            DATE(w.Date) AS SessionDate,
            w.Date AS WorkoutTimestamp,
            COUNT(e.Id) AS ExerciseCount
        FROM Workouts w
        LEFT JOIN Exercises e ON e.WorkoutId = w.Id
        WHERE w.Date IS NOT NULL
        GROUP BY w.Id, w.UserId, DATE(w.Date), w.Date
    ),
    ranked AS (
        SELECT
            wc.*,
            ROW_NUMBER() OVER (
                PARTITION BY wc.UserId, wc.SessionDate
                ORDER BY
                    CASE WHEN wc.ExerciseCount > 0 THEN 0 ELSE 1 END,
                    wc.WorkoutTimestamp,
                    wc.WorkoutId
            ) AS WorkoutRank
        FROM workout_counts wc
    ),
    canonical AS (
        SELECT
            UserId,
            SessionDate,
            WorkoutId AS CanonicalWorkoutId
        FROM ranked
        WHERE WorkoutRank = 1
    )
    SELECT
        ranked.WorkoutId,
        canonical.CanonicalWorkoutId,
        ranked.UserId,
        ranked.SessionDate,
        ranked.WorkoutTimestamp,
        ranked.ExerciseCount,
        IF(ranked.WorkoutRank = 1, 1, 0)
    FROM ranked
    JOIN canonical
        ON canonical.UserId = ranked.UserId
       AND canonical.SessionDate = ranked.SessionDate;

    SELECT COUNT(*)
    INTO v_duplicate_workouts
    FROM tmp_daily_workout_plan
    WHERE IsCanonical = 0;

    SELECT COUNT(*)
    INTO v_duplicate_days
    FROM (
        SELECT UserId, SessionDate
        FROM tmp_daily_workout_plan
        GROUP BY UserId, SessionDate
        HAVING COUNT(*) > 1
    ) duplicate_days;

    UPDATE Workouts w
    JOIN tmp_daily_workout_plan plan
        ON plan.WorkoutId = w.Id
    SET w.DailySessionKey = NULL
    WHERE plan.IsCanonical = 0;

    UPDATE Exercises e
    JOIN tmp_daily_workout_plan plan
        ON plan.WorkoutId = e.WorkoutId
    SET e.WorkoutId = plan.CanonicalWorkoutId
    WHERE plan.IsCanonical = 0;
    SET v_exercises_repointed = ROW_COUNT();

    DELETE w
    FROM Workouts w
    JOIN tmp_daily_workout_plan plan
        ON plan.WorkoutId = w.Id
    WHERE plan.IsCanonical = 0;
    SET v_workouts_deleted = ROW_COUNT();

    UPDATE Workouts w
    JOIN tmp_daily_workout_plan plan
        ON plan.WorkoutId = w.Id
       AND plan.IsCanonical = 1
    SET w.DailySessionKey = UPPER(
        SHA2(
            CONCAT(
                w.UserId,
                CHAR(10),
                DATE_FORMAT(plan.SessionDate, '%Y-%m-%d')
            ),
            256
        )
    );

    SELECT COUNT(*) INTO v_workouts_after FROM Workouts;
    SELECT COUNT(*) INTO v_history_after FROM Exercises;
    SELECT COALESCE(
        SUM(CRC32(CONCAT(Id, ':', COALESCE(TypeId, -1)))),
        0
    )
    INTO v_history_checksum_after
    FROM Exercises;

    SELECT
        (
            SELECT COUNT(*)
            FROM Exercises e
            LEFT JOIN Workouts w ON w.Id = e.WorkoutId
            WHERE e.WorkoutId IS NOT NULL
              AND w.Id IS NULL
        )
        + (
            SELECT COUNT(*)
            FROM (
                SELECT UserId, DATE(Date) AS SessionDate
                FROM Workouts
                WHERE Date IS NOT NULL
                GROUP BY UserId, DATE(Date)
                HAVING COUNT(*) > 1
            ) remaining_duplicate_days
        )
        + (
            SELECT COUNT(*)
            FROM Workouts
            WHERE Date IS NOT NULL
              AND DailySessionKey IS NULL
        )
        + (
            SELECT COUNT(*)
            FROM (
                SELECT
                    UserId,
                    SessionDate,
                    MAX(
                        CASE
                            WHEN IsCanonical = 1 THEN ExerciseCount
                            ELSE NULL
                        END
                    ) AS CanonicalExerciseCount,
                    SUM(ExerciseCount) AS TotalExerciseCount
                FROM tmp_daily_workout_plan
                GROUP BY UserId, SessionDate
                HAVING CanonicalExerciseCount = 0
                   AND TotalExerciseCount > 0
            ) invalid_canonical_workouts
        )
    INTO v_integrity_issues;

    IF v_history_after <> v_history_before
       OR v_history_checksum_after <> v_history_checksum_before THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Exercise history changed during the merge';
    END IF;

    IF v_workouts_after <> v_workouts_before - v_duplicate_workouts
       OR v_workouts_deleted <> v_duplicate_workouts THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Unexpected workout count after the merge';
    END IF;

    IF v_integrity_issues <> 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Integrity validation failed after the merge';
    END IF;

    COMMIT;
    DROP TEMPORARY TABLE IF EXISTS tmp_daily_workout_plan;
    DO RELEASE_LOCK('healthspan.daily_workout.merge');
    SET v_lock_acquired = 0;

    SELECT
        'PASS' AS Status,
        'COMMITTED' AS Persistence,
        p_snapshot_reference AS SnapshotReference,
        v_duplicate_days AS DuplicateDaysMerged,
        v_workouts_before AS WorkoutsBefore,
        v_workouts_after AS WorkoutsAfter,
        v_workouts_deleted AS DuplicateWorkoutShellsDeleted,
        v_history_before AS ExercisesBefore,
        v_history_after AS ExercisesAfter,
        v_exercises_repointed AS ExercisesRepointed,
        v_integrity_issues AS IntegrityIssueCount;
END;

-- @block 5 - Apply after taking a new backup
-- @label Apply daily workout merge
CALL hs_apply_daily_workout_merge_20260728(
    'REPLACE_WITH_NEW_BACKUP_REFERENCE'
);

-- @block 6 - Remove procedure after successful execution
-- @label Remove daily workout merge procedure
DROP PROCEDURE IF EXISTS hs_apply_daily_workout_merge_20260728;
