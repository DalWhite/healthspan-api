/*
  Healthspan exercise catalog - Phase 1 execution record
  Target: MySQL 8.0.45 / healthspan-dev

  Applied successfully on 2026-07-25. Retained for traceability and disaster
  recovery only. Do not rerun against the current database.
*/

-- @block 1 - Remove stale Phase 1 procedure
-- @label Remove stale procedure
DROP PROCEDURE IF EXISTS hs_apply_exercise_catalog_phase1_20260716;

-- @block 2 - Install atomic Phase 1 procedure
-- @label Install apply procedure
CREATE PROCEDURE hs_apply_exercise_catalog_phase1_20260716(
    IN p_snapshot_reference VARCHAR(255)
)
BEGIN
    DECLARE v_lock_acquired INT DEFAULT 0;
    DECLARE v_database_name VARCHAR(255);
    DECLARE v_check_count BIGINT DEFAULT 0;
    DECLARE v_catalog_before BIGINT DEFAULT 0;
    DECLARE v_catalog_after BIGINT DEFAULT 0;
    DECLARE v_history_before BIGINT DEFAULT 0;
    DECLARE v_history_after BIGINT DEFAULT 0;
    DECLARE v_typed_history_before BIGINT DEFAULT 0;
    DECLARE v_typed_history_after BIGINT DEFAULT 0;
    DECLARE v_history_checksum_before DECIMAL(65, 0) DEFAULT 0;
    DECLARE v_history_checksum_after DECIMAL(65, 0) DEFAULT 0;
    DECLARE v_source_history_count BIGINT DEFAULT 0;
    DECLARE v_history_repointed BIGINT DEFAULT 0;
    DECLARE v_favorite_before BIGINT DEFAULT 0;
    DECLARE v_favorite_after BIGINT DEFAULT 0;
    DECLARE v_expected_favorite_after BIGINT DEFAULT 0;
    DECLARE v_template_before BIGINT DEFAULT 0;
    DECLARE v_template_after BIGINT DEFAULT 0;
    DECLARE v_expected_template_after BIGINT DEFAULT 0;
    DECLARE v_muscle_before BIGINT DEFAULT 0;
    DECLARE v_muscle_after BIGINT DEFAULT 0;
    DECLARE v_expected_muscle_after BIGINT DEFAULT 0;
    DECLARE v_deleted_sources BIGINT DEFAULT 0;
    DECLARE v_integrity_issue_count BIGINT DEFAULT 0;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        IF v_lock_acquired = 1 THEN
            DO RELEASE_LOCK('healthspan.exercise_catalog.phase1');
        END IF;
        RESIGNAL;
    END;

    IF p_snapshot_reference IS NULL
       OR TRIM(p_snapshot_reference) = ''
       OR p_snapshot_reference = 'REPLACE_WITH_SNAPSHOT_ID' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'A real managed database snapshot reference is required';
    END IF;

    SELECT GET_LOCK('healthspan.exercise_catalog.phase1', 10)
    INTO v_lock_acquired;

    IF COALESCE(v_lock_acquired, 0) <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Could not acquire the Phase 1 catalog lock';
    END IF;

    START TRANSACTION;

    SELECT DATABASE() INTO v_database_name;
    IF COALESCE(v_database_name, '') <> 'healthspan-dev' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Wrong database: expected healthspan-dev';
    END IF;

    SELECT COUNT(*) INTO v_catalog_before FROM ExerciseTypes;
    IF v_catalog_before <> 76 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Catalog changed: expected exactly 76 exercise types';
    END IF;

    SELECT COUNT(*) INTO v_history_before FROM Exercises;
    IF v_history_before < 2044 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'History count regressed below the audited baseline';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE Id IN (26, 49, 53, 71);
    IF v_check_count <> 4 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more Phase 1 source exercises are missing';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE Id IN (21, 37, 68, 950);
    IF v_check_count <> 4 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more Phase 1 target exercises are missing';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE (Id = 26 AND BINARY TRIM(Name) = BINARY 'Press pecho mancuernas')
       OR (Id = 49 AND BINARY TRIM(Name) = BINARY 'Kettlebell - Sentadilla')
       OR (Id = 53 AND BINARY TRIM(Name) = BINARY 'Face pull')
       OR (Id = 71 AND BINARY TRIM(Name) = BINARY 'Tríceps extensión polea');
    IF v_check_count <> 4 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more Phase 1 source names changed';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE (Id = 21 AND BINARY TRIM(Name) = BINARY 'Face pull')
       OR (Id = 37 AND BINARY TRIM(Name) = BINARY 'Press banca mancuernas')
       OR (Id = 68 AND BINARY TRIM(Name) = BINARY 'Sentadilla Globet')
       OR (Id = 950 AND BINARY TRIM(Name) = BINARY 'Triceps extensión polea');
    IF v_check_count <> 4 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more Phase 1 target names changed';
    END IF;

    SELECT COUNT(*) INTO v_check_count
    FROM Exercises WHERE TypeId IN (21, 53);
    IF v_check_count <> 44 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Face pull projected history total changed';
    END IF;

    SELECT COUNT(*) INTO v_check_count
    FROM Exercises WHERE TypeId IN (26, 37);
    IF v_check_count <> 7 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Dumbbell bench projected history total changed';
    END IF;

    SELECT COUNT(*) INTO v_check_count
    FROM Exercises WHERE TypeId IN (49, 68);
    IF v_check_count <> 7 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Goblet squat projected history total changed';
    END IF;

    SELECT COUNT(*) INTO v_check_count
    FROM Exercises WHERE TypeId IN (71, 950);
    IF v_check_count <> 14 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Triceps cable projected history total changed';
    END IF;

    SELECT COUNT(*)
    INTO v_typed_history_before
    FROM Exercises
    WHERE TypeId IS NOT NULL;

    SELECT COALESCE(SUM(CRC32(CAST(Id AS CHAR))), 0)
    INTO v_history_checksum_before
    FROM Exercises;

    SELECT COUNT(*)
    INTO v_source_history_count
    FROM Exercises
    WHERE TypeId IN (26, 49, 53, 71);

    SELECT COUNT(*) INTO v_favorite_before FROM ExerciseTypeUser;
    SELECT COUNT(*)
    INTO v_expected_favorite_after
    FROM (
        SELECT DISTINCT
            CASE FavoriteExercisesId
                WHEN 26 THEN 37
                WHEN 49 THEN 68
                WHEN 53 THEN 21
                WHEN 71 THEN 950
                ELSE FavoriteExercisesId
            END AS ExerciseTypeId,
            FavoritedUsersId
        FROM ExerciseTypeUser
    ) projected_favorites;

    SELECT COUNT(*) INTO v_template_before FROM ExerciseTypeWorkoutTemplate;
    SELECT COUNT(*)
    INTO v_expected_template_after
    FROM (
        SELECT DISTINCT
            CASE ExerciseTypesId
                WHEN 26 THEN 37
                WHEN 49 THEN 68
                WHEN 53 THEN 21
                WHEN 71 THEN 950
                ELSE ExerciseTypesId
            END AS ExerciseTypeId,
            WorkoutTemplatesId
        FROM ExerciseTypeWorkoutTemplate
    ) projected_templates;

    SELECT COUNT(*)
    INTO v_muscle_before
    FROM ExerciseTypeMuscleAssignments;
    SELECT COUNT(*)
    INTO v_expected_muscle_after
    FROM (
        SELECT DISTINCT
            CASE ExerciseTypeId
                WHEN 26 THEN 37
                WHEN 49 THEN 68
                WHEN 53 THEN 21
                WHEN 71 THEN 950
                ELSE ExerciseTypeId
            END AS ExerciseTypeId,
            MuscleId
        FROM ExerciseTypeMuscleAssignments
    ) projected_muscles;

    UPDATE ExerciseTypes
    SET
        WeightType = 'Standard',
        Tags = CAST(
            JSON_ARRAY(
                JSON_OBJECT('Name', 'cable', 'Category', 'equipment'),
                JSON_OBJECT('Name', 'compound', 'Category', 'mechanic'),
                JSON_OBJECT('Name', 'pull', 'Category', 'mechanic'),
                JSON_OBJECT('Name', 'row', 'Category', 'type')
            ) AS CHAR CHARACTER SET utf8mb4
        )
    WHERE Id = 21;

    UPDATE ExerciseTypes
    SET
        Name = 'Press de banca con mancuernas',
        Description = 'Press de pecho tumbado en banco, realizado con una mancuerna en cada mano.',
        WeightType = 'Standard',
        Tags = CAST(
            JSON_ARRAY(
                JSON_OBJECT('Name', 'dumbbell', 'Category', 'equipment'),
                JSON_OBJECT('Name', 'bench', 'Category', 'equipment'),
                JSON_OBJECT('Name', 'compound', 'Category', 'mechanic'),
                JSON_OBJECT('Name', 'push', 'Category', 'mechanic'),
                JSON_OBJECT('Name', 'press', 'Category', 'type'),
                JSON_OBJECT('Name', 'variation', 'Category', 'type')
            ) AS CHAR CHARACTER SET utf8mb4
        )
    WHERE Id = 37;

    UPDATE ExerciseTypes
    SET
        Name = 'Sentadilla goblet con kettlebell',
        Description = 'Sentadilla sujetando una kettlebell frente al pecho en posición goblet.',
        ImageRoute = 'https://kettlebellsworkouts.com/wp-content/uploads/2019/08/11_goblet_squat.gif',
        WeightType = 'Standard',
        Tags = CAST(
            JSON_ARRAY(
                JSON_OBJECT('Name', 'kettlebell', 'Category', 'equipment'),
                JSON_OBJECT('Name', 'compound', 'Category', 'mechanic'),
                JSON_OBJECT('Name', 'squat', 'Category', 'mechanic'),
                JSON_OBJECT('Name', 'variation', 'Category', 'type')
            ) AS CHAR CHARACTER SET utf8mb4
        )
    WHERE Id = 68;

    UPDATE ExerciseTypes
    SET
        Name = 'Extensión de tríceps en polea',
        Description = 'Extensión de codo en polea para trabajar principalmente el tríceps.',
        WeightType = 'Standard',
        Tags = CAST(
            JSON_ARRAY(
                JSON_OBJECT('Name', 'cable', 'Category', 'equipment'),
                JSON_OBJECT('Name', 'isolation', 'Category', 'mechanic'),
                JSON_OBJECT('Name', 'push', 'Category', 'mechanic'),
                JSON_OBJECT('Name', 'variation', 'Category', 'type')
            ) AS CHAR CHARACTER SET utf8mb4
        )
    WHERE Id = 950;

    UPDATE Exercises
    SET TypeId = CASE TypeId
        WHEN 26 THEN 37
        WHEN 49 THEN 68
        WHEN 53 THEN 21
        WHEN 71 THEN 950
        ELSE TypeId
    END
    WHERE TypeId IN (26, 49, 53, 71);

    SET v_history_repointed = ROW_COUNT();
    IF v_history_repointed <> v_source_history_count THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Not every source history row was repointed';
    END IF;

    INSERT IGNORE INTO ExerciseTypeUser
        (FavoriteExercisesId, FavoritedUsersId)
    SELECT
        CASE FavoriteExercisesId
            WHEN 26 THEN 37
            WHEN 49 THEN 68
            WHEN 53 THEN 21
            WHEN 71 THEN 950
        END,
        FavoritedUsersId
    FROM ExerciseTypeUser
    WHERE FavoriteExercisesId IN (26, 49, 53, 71);

    DELETE FROM ExerciseTypeUser
    WHERE FavoriteExercisesId IN (26, 49, 53, 71);

    INSERT IGNORE INTO ExerciseTypeWorkoutTemplate
        (ExerciseTypesId, WorkoutTemplatesId)
    SELECT
        CASE ExerciseTypesId
            WHEN 26 THEN 37
            WHEN 49 THEN 68
            WHEN 53 THEN 21
            WHEN 71 THEN 950
        END,
        WorkoutTemplatesId
    FROM ExerciseTypeWorkoutTemplate
    WHERE ExerciseTypesId IN (26, 49, 53, 71);

    DELETE FROM ExerciseTypeWorkoutTemplate
    WHERE ExerciseTypesId IN (26, 49, 53, 71);

    INSERT IGNORE INTO ExerciseTypeMuscleAssignments
        (ExerciseTypeId, MuscleId, Role, SortOrder)
    SELECT
        CASE ExerciseTypeId
            WHEN 26 THEN 37
            WHEN 49 THEN 68
            WHEN 53 THEN 21
            WHEN 71 THEN 950
        END,
        MuscleId,
        Role,
        SortOrder
    FROM ExerciseTypeMuscleAssignments
    WHERE ExerciseTypeId IN (26, 49, 53, 71);

    DELETE FROM ExerciseTypeMuscleAssignments
    WHERE ExerciseTypeId IN (26, 49, 53, 71);

    UPDATE ExerciseTypes
    SET ParentExerciseTypeId = CASE ParentExerciseTypeId
        WHEN 26 THEN 37
        WHEN 49 THEN 68
        WHEN 53 THEN 21
        WHEN 71 THEN 950
        ELSE ParentExerciseTypeId
    END
    WHERE ParentExerciseTypeId IN (26, 49, 53, 71);

    DELETE FROM ExerciseTypes
    WHERE Id IN (26, 49, 53, 71);

    SET v_deleted_sources = ROW_COUNT();
    IF v_deleted_sources <> 4 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Exactly four source exercises were not deleted';
    END IF;

    SELECT COUNT(*) INTO v_catalog_after FROM ExerciseTypes;
    SELECT COUNT(*) INTO v_history_after FROM Exercises;
    SELECT COUNT(*)
    INTO v_typed_history_after
    FROM Exercises
    WHERE TypeId IS NOT NULL;
    SELECT COALESCE(SUM(CRC32(CAST(Id AS CHAR))), 0)
    INTO v_history_checksum_after
    FROM Exercises;
    SELECT COUNT(*) INTO v_favorite_after FROM ExerciseTypeUser;
    SELECT COUNT(*) INTO v_template_after FROM ExerciseTypeWorkoutTemplate;
    SELECT COUNT(*)
    INTO v_muscle_after
    FROM ExerciseTypeMuscleAssignments;

    IF v_catalog_after <> 72 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: catalog count is not 72';
    END IF;
    IF v_history_after <> v_history_before
       OR v_typed_history_after <> v_typed_history_before
       OR v_history_checksum_after <> v_history_checksum_before THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: history identity or count changed';
    END IF;
    IF v_favorite_after <> v_expected_favorite_after THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: favorite link count mismatch';
    END IF;
    IF v_template_after <> v_expected_template_after THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: template link count mismatch';
    END IF;
    IF v_muscle_after <> v_expected_muscle_after THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: muscle assignment count mismatch';
    END IF;

    SELECT COUNT(*) INTO v_check_count
    FROM Exercises WHERE TypeId = 21;
    IF v_check_count <> 44 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed for Face pull history';
    END IF;
    SELECT COUNT(*) INTO v_check_count
    FROM Exercises WHERE TypeId = 37;
    IF v_check_count <> 7 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed for dumbbell bench history';
    END IF;
    SELECT COUNT(*) INTO v_check_count
    FROM Exercises WHERE TypeId = 68;
    IF v_check_count <> 7 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed for goblet squat history';
    END IF;
    SELECT COUNT(*) INTO v_check_count
    FROM Exercises WHERE TypeId = 950;
    IF v_check_count <> 14 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed for triceps cable history';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE (Id = 21 AND BINARY TRIM(Name) = BINARY 'Face pull')
       OR (Id = 37 AND BINARY TRIM(Name) = BINARY 'Press de banca con mancuernas')
       OR (Id = 68 AND BINARY TRIM(Name) = BINARY 'Sentadilla goblet con kettlebell')
       OR (Id = 950 AND BINARY TRIM(Name) = BINARY 'Extensión de tríceps en polea');
    IF v_check_count <> 4 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: canonical target names are incorrect';
    END IF;

    SELECT
        (
            SELECT COUNT(*)
            FROM Exercises e
            LEFT JOIN ExerciseTypes et ON et.Id = e.TypeId
            WHERE e.TypeId IS NOT NULL AND et.Id IS NULL
        )
        + (
            SELECT COUNT(*)
            FROM ExerciseTypes child
            LEFT JOIN ExerciseTypes parent
                ON parent.Id = child.ParentExerciseTypeId
            WHERE child.ParentExerciseTypeId IS NOT NULL
              AND (
                  parent.Id IS NULL
                  OR child.ParentExerciseTypeId = child.Id
                  OR parent.ParentExerciseTypeId IS NOT NULL
              )
        )
        + (
            SELECT COUNT(*)
            FROM ExerciseTypeUser f
            LEFT JOIN ExerciseTypes et ON et.Id = f.FavoriteExercisesId
            WHERE et.Id IS NULL
        )
        + (
            SELECT COUNT(*)
            FROM ExerciseTypeWorkoutTemplate wt
            LEFT JOIN ExerciseTypes et ON et.Id = wt.ExerciseTypesId
            WHERE et.Id IS NULL
        )
        + (
            SELECT COUNT(*)
            FROM ExerciseTypeMuscleAssignments a
            LEFT JOIN ExerciseTypes et ON et.Id = a.ExerciseTypeId
            LEFT JOIN Muscles m ON m.Id = a.MuscleId
            WHERE et.Id IS NULL OR m.Id IS NULL
        )
    INTO v_integrity_issue_count;

    IF v_integrity_issue_count <> 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: integrity issues were created';
    END IF;

    COMMIT;
    DO RELEASE_LOCK('healthspan.exercise_catalog.phase1');
    SET v_lock_acquired = 0;

    SELECT
        'PASS' AS Status,
        'COMMITTED' AS Persistence,
        p_snapshot_reference AS SnapshotReference,
        v_catalog_before AS CatalogBefore,
        v_catalog_after AS CatalogAfter,
        v_history_before AS HistoryBefore,
        v_history_after AS HistoryAfter,
        v_history_repointed AS HistoryRowsRepointed,
        v_favorite_before AS FavoriteLinksBefore,
        v_favorite_after AS FavoriteLinksAfter,
        v_template_before AS TemplateLinksBefore,
        v_template_after AS TemplateLinksAfter,
        v_muscle_before AS MuscleAssignmentsBefore,
        v_muscle_after AS MuscleAssignmentsAfter,
        v_integrity_issue_count AS IntegrityIssueCount;
END;

-- @block 3 - Apply Phase 1 after snapshot
-- @label Apply and commit Phase 1
CALL hs_apply_exercise_catalog_phase1_20260716(
    'mysqldump: healthspan-dev-20260725-123320.sql; SHA256: 77418932DB3C034D5A4B85AABB9B00BFBB16DBFAC8E0B92A78DAD604D3C3791A'
);

-- @block 4 - Remove procedure after successful apply
-- @label Remove completed procedure
DROP PROCEDURE hs_apply_exercise_catalog_phase1_20260716;
