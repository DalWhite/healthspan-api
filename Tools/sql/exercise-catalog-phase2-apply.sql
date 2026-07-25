/*
  Healthspan exercise catalog - Phase 2 apply
  Target: MySQL 8.0 / healthspan-dev

  SQLTools usage:
  1. Take a new database backup.
  2. Run blocks 1 and 2 separately.
  3. Put the new backup reference in block 3 and run it once.
  4. Require Status=PASS and Persistence=COMMITTED.
  5. Run block 4 only after a successful commit.

  The procedure rolls back on every failed precondition or post-check.
*/

-- @block 1 - Remove stale Phase 2 procedure
-- @label Remove stale procedure
DROP PROCEDURE IF EXISTS hs_apply_exercise_catalog_phase2_20260725;

-- @block 2 - Install atomic Phase 2 procedure
-- @label Install apply procedure
CREATE PROCEDURE hs_apply_exercise_catalog_phase2_20260725(
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
    DECLARE v_muscle_before BIGINT DEFAULT 0;
    DECLARE v_muscle_after BIGINT DEFAULT 0;
    DECLARE v_hierarchy_changed BIGINT DEFAULT 0;
    DECLARE v_names_changed BIGINT DEFAULT 0;
    DECLARE v_muscles_removed BIGINT DEFAULT 0;
    DECLARE v_muscles_added BIGINT DEFAULT 0;
    DECLARE v_integrity_issue_count BIGINT DEFAULT 0;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        IF v_lock_acquired = 1 THEN
            DO RELEASE_LOCK('healthspan.exercise_catalog.phase2');
        END IF;
        RESIGNAL;
    END;

    IF p_snapshot_reference IS NULL
       OR TRIM(p_snapshot_reference) = ''
       OR p_snapshot_reference = 'REPLACE_WITH_NEW_BACKUP_REFERENCE' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'A new Phase 2 backup reference is required';
    END IF;

    SELECT GET_LOCK('healthspan.exercise_catalog.phase2', 10)
    INTO v_lock_acquired;

    IF COALESCE(v_lock_acquired, 0) <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Could not acquire the Phase 2 catalog lock';
    END IF;

    START TRANSACTION;

    SELECT DATABASE() INTO v_database_name;
    IF COALESCE(v_database_name, '') <> 'healthspan-dev' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Wrong database: expected healthspan-dev';
    END IF;

    SELECT COUNT(*) INTO v_catalog_before FROM ExerciseTypes;
    IF v_catalog_before <> 72 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Catalog changed: expected exactly 72 exercise types';
    END IF;

    SELECT COUNT(*) INTO v_history_before FROM Exercises;
    IF v_history_before < 2072 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'History count regressed below the Phase 2 baseline';
    END IF;

    SELECT COUNT(*)
    INTO v_typed_history_before
    FROM Exercises
    WHERE TypeId IS NOT NULL;

    SELECT COALESCE(
        SUM(CRC32(CONCAT(Id, ':', COALESCE(TypeId, -1)))),
        0
    )
    INTO v_history_checksum_before
    FROM Exercises;

    SELECT COUNT(*)
    INTO v_muscle_before
    FROM ExerciseTypeMuscleAssignments;
    IF v_muscle_before <> 143 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Muscle assignments changed: expected exactly 143';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE Id IN (26, 49, 53, 71);
    IF v_check_count <> 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Phase 1 duplicate sources unexpectedly remain';
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
            SET MESSAGE_TEXT = 'Phase 1 canonical targets changed';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE Id IN (
        14, 24, 27, 28, 31, 33, 34, 41, 43, 46, 50,
        51, 54, 55, 58, 60, 63, 64, 65, 66, 67, 68,
        70, 72, 946, 947, 948, 949, 951, 952, 954
    )
      AND ParentExerciseTypeId IS NULL;
    IF v_check_count <> 31 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Planned variations are missing or already assigned';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE Id IN (7, 8, 9, 10, 12, 16, 17, 18, 32, 35, 48, 59)
      AND ParentExerciseTypeId IS NULL;
    IF v_check_count <> 12 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more planned parents are missing or nested';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE (Id = 23 AND ParentExerciseTypeId = 12)
       OR (Id = 36 AND ParentExerciseTypeId = 12)
       OR (Id = 37 AND ParentExerciseTypeId = 12);
    IF v_check_count <> 3 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Existing Press banca variation links changed';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE (Id = 8 AND BINARY TRIM(Name) = BINARY 'dominadas')
       OR (Id = 9 AND BINARY TRIM(Name) = BINARY 'Curl Biceps')
       OR (Id = 17 AND BINARY TRIM(Name) = BINARY 'Peso Muerto')
       OR (Id = 18 AND BINARY TRIM(Name) = BINARY 'Jalon al pecho')
       OR (Id = 23 AND BINARY TRIM(Name) = BINARY 'Ketelbel banca')
       OR (Id = 24 AND BINARY TRIM(Name) = BINARY 'Ketelbel militar')
       OR (Id = 31 AND BINARY TRIM(Name) = BINARY 'Biceps colgado')
       OR (Id = 35 AND BINARY TRIM(Name) = BINARY 'Elevaciones hombro')
       OR (Id = 38 AND BINARY TRIM(Name) = BINARY 'Triceps ketelbel')
       OR (Id = 41 AND BINARY TRIM(Name) = BINARY 'Remo ketelbel')
       OR (Id = 42 AND BINARY TRIM(Name) = BINARY 'Praise de pecho')
       OR (Id = 43 AND BINARY TRIM(Name) = BINARY 'Biceps polea')
       OR (Id = 46 AND BINARY TRIM(Name) = BINARY 'Clean n press one arm')
       OR (Id = 47 AND BINARY TRIM(Name) = BINARY 'Puente de gluteos')
       OR (Id = 54 AND BINARY TRIM(Name) = BINARY 'Jalon')
       OR (Id = 55 AND BINARY TRIM(Name) = BINARY 'Sentadilla bulgara')
       OR (Id = 58 AND BINARY TRIM(Name) = BINARY 'Elevaciones hombro frontales')
       OR (Id = 60 AND BINARY TRIM(Name) = BINARY 'Sentadilla Sumo')
       OR (Id = 65 AND BINARY TRIM(Name) = BINARY 'Remo Unilateral Polea')
       OR (Id = 69 AND BINARY TRIM(Name) = BINARY 'Extensión de Cuádriceps')
       OR (Id = 70 AND BINARY TRIM(Name) = BINARY 'Jalón al pecho SUPINO')
       OR (Id = 73 AND BINARY TRIM(Name) = BINARY 'Isquios máquina tumbada')
       OR (Id = 74 AND BINARY TRIM(Name) = BINARY 'Extensión Gemelos Máquina')
       OR (Id = 947 AND BINARY TRIM(Name) = BINARY 'Remo en Polea')
       OR (Id = 948 AND BINARY TRIM(Name) = BINARY 'Biceps crossover curl')
       OR (Id = 949 AND BINARY TRIM(Name) = BINARY 'Hip Thrust Máquina')
       OR (Id = 951 AND BINARY TRIM(Name) = BINARY 'Remo Unilateral mancuerna')
       OR (Id = 953 AND BINARY TRIM(Name) = BINARY 'Hombro around the world')
       OR (Id = 954 AND BINARY TRIM(Name) = BINARY 'Pulldown');
    IF v_check_count <> 29 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more Phase 2 source names changed';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE Id IN (52, 56, 58, 61, 62, 63, 66, 69, 70, 73, 74, 946, 954);
    IF v_check_count <> 13 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more muscle-repair exercises are missing';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM Muscles
    WHERE Id IN (1, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16, 18, 21);
    IF v_check_count <> 14 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more planned muscles are missing';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypeMuscleAssignments
    WHERE (ExerciseTypeId, MuscleId) IN (
        (52, 6), (52, 21),
        (56, 8), (56, 12), (56, 10),
        (58, 9), (58, 13),
        (61, 14), (61, 12),
        (62, 12), (62, 10),
        (63, 16), (63, 13),
        (66, 7), (66, 5),
        (69, 8),
        (70, 7), (70, 1),
        (73, 10), (73, 11),
        (74, 11),
        (946, 18), (946, 7), (946, 1),
        (954, 7), (954, 1)
    );
    IF v_check_count <> 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'One or more planned muscle additions already exist';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypeMuscleAssignments
    WHERE ExerciseTypeId = 70
      AND MuscleId IN (4, 5, 9);
    IF v_check_count <> 3 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'The three audited muscle removals are not present';
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
                  OR child.Id = child.ParentExerciseTypeId
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
            SET MESSAGE_TEXT = 'Existing integrity issues must be resolved first';
    END IF;

    UPDATE ExerciseTypes
    SET ParentExerciseTypeId = CASE Id
        WHEN 24 THEN 7
        WHEN 27 THEN 7
        WHEN 72 THEN 8
        WHEN 31 THEN 9
        WHEN 34 THEN 9
        WHEN 43 THEN 9
        WHEN 67 THEN 9
        WHEN 948 THEN 9
        WHEN 41 THEN 10
        WHEN 64 THEN 10
        WHEN 65 THEN 10
        WHEN 946 THEN 10
        WHEN 947 THEN 10
        WHEN 951 THEN 10
        WHEN 952 THEN 10
        WHEN 33 THEN 12
        WHEN 14 THEN 16
        WHEN 51 THEN 16
        WHEN 55 THEN 16
        WHEN 60 THEN 16
        WHEN 68 THEN 16
        WHEN 28 THEN 17
        WHEN 50 THEN 17
        WHEN 54 THEN 18
        WHEN 70 THEN 18
        WHEN 954 THEN 18
        WHEN 949 THEN 32
        WHEN 58 THEN 35
        WHEN 63 THEN 35
        WHEN 46 THEN 48
        WHEN 66 THEN 59
        ELSE ParentExerciseTypeId
    END
    WHERE Id IN (
        14, 24, 27, 28, 31, 33, 34, 41, 43, 46, 50,
        51, 54, 55, 58, 60, 63, 64, 65, 66, 67, 68,
        70, 72, 946, 947, 948, 949, 951, 952, 954
    );

    SET v_hierarchy_changed = ROW_COUNT();
    IF v_hierarchy_changed <> 31 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Not every planned hierarchy link was updated';
    END IF;

    UPDATE ExerciseTypes
    SET Name = CASE Id
        WHEN 8 THEN 'Dominadas'
        WHEN 9 THEN 'Curl de bíceps'
        WHEN 17 THEN 'Peso muerto'
        WHEN 18 THEN 'Jalón al pecho'
        WHEN 23 THEN 'Press de banca con kettlebell'
        WHEN 24 THEN 'Press militar con kettlebell'
        WHEN 31 THEN 'Curl de bíceps colgado'
        WHEN 35 THEN 'Elevaciones laterales'
        WHEN 38 THEN 'Extensión de tríceps con kettlebell'
        WHEN 41 THEN 'Remo con kettlebell'
        WHEN 42 THEN 'Press de pecho'
        WHEN 43 THEN 'Curl de bíceps en polea'
        WHEN 46 THEN 'Clean and press unilateral con kettlebell'
        WHEN 47 THEN 'Puente de glúteos'
        WHEN 54 THEN 'Jalón al pecho con agarre ancho'
        WHEN 55 THEN 'Sentadilla búlgara'
        WHEN 58 THEN 'Elevaciones frontales'
        WHEN 60 THEN 'Sentadilla sumo'
        WHEN 65 THEN 'Remo unilateral en polea'
        WHEN 69 THEN 'Extensión de cuádriceps'
        WHEN 70 THEN 'Jalón al pecho supino'
        WHEN 73 THEN 'Curl femoral tumbado en máquina'
        WHEN 74 THEN 'Elevación de gemelos en máquina'
        WHEN 947 THEN 'Remo en polea'
        WHEN 948 THEN 'Curl crossover de bíceps'
        WHEN 949 THEN 'Hip thrust en máquina'
        WHEN 951 THEN 'Remo unilateral con mancuerna'
        WHEN 953 THEN 'Around the world de hombros'
        WHEN 954 THEN 'Jalón al pecho con agarre ancho en máquina'
        ELSE Name
    END
    WHERE Id IN (
        8, 9, 17, 18, 23, 24, 31, 35, 38, 41,
        42, 43, 46, 47, 54, 55, 58, 60, 65, 69,
        70, 73, 74, 947, 948, 949, 951, 953, 954
    );

    SET v_names_changed = ROW_COUNT();

    DELETE FROM ExerciseTypeMuscleAssignments
    WHERE ExerciseTypeId = 70
      AND MuscleId IN (4, 5, 9);

    SET v_muscles_removed = ROW_COUNT();
    IF v_muscles_removed <> 3 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Exactly three incorrect muscle links were not removed';
    END IF;

    INSERT INTO ExerciseTypeMuscleAssignments
        (ExerciseTypeId, MuscleId, Role, SortOrder)
    VALUES
        (52, 6, 'Primary', 1),
        (52, 21, 'Secondary', 1),
        (56, 8, 'Primary', 1),
        (56, 12, 'Secondary', 1),
        (56, 10, 'Secondary', 2),
        (58, 9, 'Primary', 1),
        (58, 13, 'Secondary', 1),
        (61, 14, 'Primary', 1),
        (61, 12, 'Secondary', 1),
        (62, 12, 'Primary', 1),
        (62, 10, 'Secondary', 1),
        (63, 16, 'Primary', 1),
        (63, 13, 'Secondary', 1),
        (66, 7, 'Primary', 1),
        (66, 5, 'Secondary', 1),
        (69, 8, 'Primary', 1),
        (70, 7, 'Primary', 1),
        (70, 1, 'Secondary', 1),
        (73, 10, 'Primary', 1),
        (73, 11, 'Secondary', 1),
        (74, 11, 'Primary', 1),
        (946, 18, 'Primary', 1),
        (946, 7, 'Secondary', 1),
        (946, 1, 'Secondary', 2),
        (954, 7, 'Primary', 1),
        (954, 1, 'Secondary', 1);

    SET v_muscles_added = ROW_COUNT();
    IF v_muscles_added <> 26 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Exactly 26 muscle assignments were not added';
    END IF;

    SELECT COUNT(*) INTO v_catalog_after FROM ExerciseTypes;
    SELECT COUNT(*) INTO v_history_after FROM Exercises;
    SELECT COUNT(*)
    INTO v_typed_history_after
    FROM Exercises
    WHERE TypeId IS NOT NULL;
    SELECT COALESCE(
        SUM(CRC32(CONCAT(Id, ':', COALESCE(TypeId, -1)))),
        0
    )
    INTO v_history_checksum_after
    FROM Exercises;
    SELECT COUNT(*)
    INTO v_muscle_after
    FROM ExerciseTypeMuscleAssignments;

    IF v_catalog_after <> v_catalog_before
       OR v_catalog_after <> 72 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: catalog count changed';
    END IF;

    IF v_history_after <> v_history_before
       OR v_typed_history_after <> v_typed_history_before
       OR v_history_checksum_after <> v_history_checksum_before THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: history changed';
    END IF;

    IF v_muscle_after <> 166 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: expected 166 muscle assignments';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE (ParentExerciseTypeId = 7 AND Id IN (24, 27))
       OR (ParentExerciseTypeId = 8 AND Id = 72)
       OR (ParentExerciseTypeId = 9 AND Id IN (31, 34, 43, 67, 948))
       OR (ParentExerciseTypeId = 10 AND Id IN (41, 64, 65, 946, 947, 951, 952))
       OR (ParentExerciseTypeId = 12 AND Id = 33)
       OR (ParentExerciseTypeId = 16 AND Id IN (14, 51, 55, 60, 68))
       OR (ParentExerciseTypeId = 17 AND Id IN (28, 50))
       OR (ParentExerciseTypeId = 18 AND Id IN (54, 70, 954))
       OR (ParentExerciseTypeId = 32 AND Id = 949)
       OR (ParentExerciseTypeId = 35 AND Id IN (58, 63))
       OR (ParentExerciseTypeId = 48 AND Id = 46)
       OR (ParentExerciseTypeId = 59 AND Id = 66);
    IF v_check_count <> 31 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: hierarchy links are incomplete';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypes
    WHERE (Id = 8 AND BINARY Name = BINARY 'Dominadas')
       OR (Id = 9 AND BINARY Name = BINARY 'Curl de bíceps')
       OR (Id = 17 AND BINARY Name = BINARY 'Peso muerto')
       OR (Id = 18 AND BINARY Name = BINARY 'Jalón al pecho')
       OR (Id = 23 AND BINARY Name = BINARY 'Press de banca con kettlebell')
       OR (Id = 24 AND BINARY Name = BINARY 'Press militar con kettlebell')
       OR (Id = 31 AND BINARY Name = BINARY 'Curl de bíceps colgado')
       OR (Id = 35 AND BINARY Name = BINARY 'Elevaciones laterales')
       OR (Id = 38 AND BINARY Name = BINARY 'Extensión de tríceps con kettlebell')
       OR (Id = 41 AND BINARY Name = BINARY 'Remo con kettlebell')
       OR (Id = 42 AND BINARY Name = BINARY 'Press de pecho')
       OR (Id = 43 AND BINARY Name = BINARY 'Curl de bíceps en polea')
       OR (Id = 46 AND BINARY Name = BINARY 'Clean and press unilateral con kettlebell')
       OR (Id = 47 AND BINARY Name = BINARY 'Puente de glúteos')
       OR (Id = 54 AND BINARY Name = BINARY 'Jalón al pecho con agarre ancho')
       OR (Id = 55 AND BINARY Name = BINARY 'Sentadilla búlgara')
       OR (Id = 58 AND BINARY Name = BINARY 'Elevaciones frontales')
       OR (Id = 60 AND BINARY Name = BINARY 'Sentadilla sumo')
       OR (Id = 65 AND BINARY Name = BINARY 'Remo unilateral en polea')
       OR (Id = 69 AND BINARY Name = BINARY 'Extensión de cuádriceps')
       OR (Id = 70 AND BINARY Name = BINARY 'Jalón al pecho supino')
       OR (Id = 73 AND BINARY Name = BINARY 'Curl femoral tumbado en máquina')
       OR (Id = 74 AND BINARY Name = BINARY 'Elevación de gemelos en máquina')
       OR (Id = 947 AND BINARY Name = BINARY 'Remo en polea')
       OR (Id = 948 AND BINARY Name = BINARY 'Curl crossover de bíceps')
       OR (Id = 949 AND BINARY Name = BINARY 'Hip thrust en máquina')
       OR (Id = 951 AND BINARY Name = BINARY 'Remo unilateral con mancuerna')
       OR (Id = 953 AND BINARY Name = BINARY 'Around the world de hombros')
       OR (Id = 954 AND BINARY Name = BINARY 'Jalón al pecho con agarre ancho en máquina');
    IF v_check_count <> 29 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: canonical names are incomplete';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypeMuscleAssignments
    WHERE (ExerciseTypeId, MuscleId, Role, SortOrder) IN (
        (52, 6, 'Primary', 1),
        (52, 21, 'Secondary', 1),
        (56, 8, 'Primary', 1),
        (56, 12, 'Secondary', 1),
        (56, 10, 'Secondary', 2),
        (58, 9, 'Primary', 1),
        (58, 13, 'Secondary', 1),
        (61, 14, 'Primary', 1),
        (61, 12, 'Secondary', 1),
        (62, 12, 'Primary', 1),
        (62, 10, 'Secondary', 1),
        (63, 16, 'Primary', 1),
        (63, 13, 'Secondary', 1),
        (66, 7, 'Primary', 1),
        (66, 5, 'Secondary', 1),
        (69, 8, 'Primary', 1),
        (70, 7, 'Primary', 1),
        (70, 1, 'Secondary', 1),
        (73, 10, 'Primary', 1),
        (73, 11, 'Secondary', 1),
        (74, 11, 'Primary', 1),
        (946, 18, 'Primary', 1),
        (946, 7, 'Secondary', 1),
        (946, 1, 'Secondary', 2),
        (954, 7, 'Primary', 1),
        (954, 1, 'Secondary', 1)
    );
    IF v_check_count <> 26 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: muscle repairs are incomplete';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM ExerciseTypeMuscleAssignments
    WHERE ExerciseTypeId = 70
      AND MuscleId IN (4, 5, 9);
    IF v_check_count <> 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: incorrect muscle links remain';
    END IF;

    SELECT COUNT(*)
    INTO v_check_count
    FROM (
        SELECT LOWER(TRIM(Name)) AS NormalizedName
        FROM ExerciseTypes
        GROUP BY LOWER(TRIM(Name))
        HAVING COUNT(*) > 1
    ) duplicate_groups;
    IF v_check_count <> 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-check failed: duplicate canonical names created';
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
                  OR child.Id = child.ParentExerciseTypeId
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
    DO RELEASE_LOCK('healthspan.exercise_catalog.phase2');
    SET v_lock_acquired = 0;

    SELECT
        'PASS' AS Status,
        'COMMITTED' AS Persistence,
        p_snapshot_reference AS SnapshotReference,
        v_catalog_before AS CatalogBefore,
        v_catalog_after AS CatalogAfter,
        v_history_before AS HistoryBefore,
        v_history_after AS HistoryAfter,
        v_hierarchy_changed AS HierarchyLinksChanged,
        v_names_changed AS NamesChanged,
        v_muscle_before AS MuscleAssignmentsBefore,
        v_muscle_after AS MuscleAssignmentsAfter,
        v_muscles_removed AS MuscleAssignmentsRemoved,
        v_muscles_added AS MuscleAssignmentsAdded,
        v_integrity_issue_count AS IntegrityIssueCount;
END;

-- @block 3 - Apply Phase 2 after new backup
-- @label Apply and commit Phase 2
CALL hs_apply_exercise_catalog_phase2_20260725(
    'mysqldump: healthspan-dev-20260725-123320.sql; SHA256: 77418932DB3C034D5A4B85AABB9B00BFBB16DBFAC8E0B92A78DAD604D3C3791A'
);


-- @block 4 - Remove procedure after successful apply
-- @label Remove completed procedure
DROP PROCEDURE hs_apply_exercise_catalog_phase2_20260725;
