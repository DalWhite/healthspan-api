-- @block Exercise catalog Phase 2 preview
-- @label Phase 2 preview report
/*
  Read-only SQLTools preview for healthspan-dev / MySQL 8.0.
  Run this file as one query and export the single result grid.
  It creates no tables and changes no data.
*/

WITH
hierarchy_plan AS (
    SELECT 24 AS ChildId, 7 AS ParentId
    UNION ALL SELECT 27, 7
    UNION ALL SELECT 72, 8
    UNION ALL SELECT 31, 9
    UNION ALL SELECT 34, 9
    UNION ALL SELECT 43, 9
    UNION ALL SELECT 67, 9
    UNION ALL SELECT 948, 9
    UNION ALL SELECT 41, 10
    UNION ALL SELECT 64, 10
    UNION ALL SELECT 65, 10
    UNION ALL SELECT 946, 10
    UNION ALL SELECT 947, 10
    UNION ALL SELECT 951, 10
    UNION ALL SELECT 952, 10
    UNION ALL SELECT 33, 12
    UNION ALL SELECT 14, 16
    UNION ALL SELECT 51, 16
    UNION ALL SELECT 55, 16
    UNION ALL SELECT 60, 16
    UNION ALL SELECT 68, 16
    UNION ALL SELECT 28, 17
    UNION ALL SELECT 50, 17
    UNION ALL SELECT 54, 18
    UNION ALL SELECT 70, 18
    UNION ALL SELECT 954, 18
    UNION ALL SELECT 949, 32
    UNION ALL SELECT 58, 35
    UNION ALL SELECT 63, 35
    UNION ALL SELECT 46, 48
    UNION ALL SELECT 66, 59
),
name_plan AS (
    SELECT
        8 AS ExerciseTypeId,
        CONVERT('dominadas' USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS ExpectedCurrentName,
        CONVERT('Dominadas' USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS CanonicalName
    UNION ALL SELECT 9, 'Curl Biceps', 'Curl de bíceps'
    UNION ALL SELECT 17, 'Peso Muerto', 'Peso muerto'
    UNION ALL SELECT 18, 'Jalon al pecho', 'Jalón al pecho'
    UNION ALL SELECT 23, 'Ketelbel banca', 'Press de banca con kettlebell'
    UNION ALL SELECT 24, 'Ketelbel militar', 'Press militar con kettlebell'
    UNION ALL SELECT 31, 'Biceps colgado', 'Curl de bíceps colgado'
    UNION ALL SELECT 35, 'Elevaciones hombro', 'Elevaciones laterales'
    UNION ALL SELECT 38, 'Triceps ketelbel', 'Extensión de tríceps con kettlebell'
    UNION ALL SELECT 41, 'Remo ketelbel', 'Remo con kettlebell'
    UNION ALL SELECT 42, 'Praise de pecho', 'Press de pecho'
    UNION ALL SELECT 43, 'Biceps polea', 'Curl de bíceps en polea'
    UNION ALL SELECT 46, 'Clean n press one arm', 'Clean and press unilateral con kettlebell'
    UNION ALL SELECT 47, 'Puente de gluteos', 'Puente de glúteos'
    UNION ALL SELECT 54, 'Jalon', 'Jalón al pecho con agarre ancho'
    UNION ALL SELECT 55, 'Sentadilla bulgara', 'Sentadilla búlgara'
    UNION ALL SELECT 58, 'Elevaciones hombro frontales', 'Elevaciones frontales'
    UNION ALL SELECT 60, 'Sentadilla Sumo', 'Sentadilla sumo'
    UNION ALL SELECT 65, 'Remo Unilateral Polea', 'Remo unilateral en polea'
    UNION ALL SELECT 69, 'Extensión de Cuádriceps', 'Extensión de cuádriceps'
    UNION ALL SELECT 70, 'Jalón al pecho SUPINO', 'Jalón al pecho supino'
    UNION ALL SELECT 73, 'Isquios máquina tumbada', 'Curl femoral tumbado en máquina'
    UNION ALL SELECT 74, 'Extensión Gemelos Máquina', 'Elevación de gemelos en máquina'
    UNION ALL SELECT 947, 'Remo en Polea', 'Remo en polea'
    UNION ALL SELECT 948, 'Biceps crossover curl', 'Curl crossover de bíceps'
    UNION ALL SELECT 949, 'Hip Thrust Máquina', 'Hip thrust en máquina'
    UNION ALL SELECT 951, 'Remo Unilateral mancuerna', 'Remo unilateral con mancuerna'
    UNION ALL SELECT 953, 'Hombro around the world', 'Around the world de hombros'
    UNION ALL SELECT 954, 'Pulldown', 'Jalón al pecho con agarre ancho en máquina'
),
muscle_plan AS (
    SELECT 52 AS ExerciseTypeId, 6 AS MuscleId
    UNION ALL SELECT 52, 21
    UNION ALL SELECT 56, 8
    UNION ALL SELECT 56, 12
    UNION ALL SELECT 56, 10
    UNION ALL SELECT 58, 9
    UNION ALL SELECT 58, 13
    UNION ALL SELECT 61, 14
    UNION ALL SELECT 61, 12
    UNION ALL SELECT 62, 12
    UNION ALL SELECT 62, 10
    UNION ALL SELECT 63, 16
    UNION ALL SELECT 63, 13
    UNION ALL SELECT 66, 7
    UNION ALL SELECT 66, 5
    UNION ALL SELECT 69, 8
    UNION ALL SELECT 70, 7
    UNION ALL SELECT 70, 1
    UNION ALL SELECT 73, 10
    UNION ALL SELECT 73, 11
    UNION ALL SELECT 74, 11
    UNION ALL SELECT 946, 18
    UNION ALL SELECT 946, 7
    UNION ALL SELECT 946, 1
    UNION ALL SELECT 954, 7
    UNION ALL SELECT 954, 1
),
muscle_removals AS (
    SELECT 70 AS ExerciseTypeId, 4 AS MuscleId
    UNION ALL SELECT 70, 5
    UNION ALL SELECT 70, 9
),
baseline AS (
    SELECT
        DATABASE() AS DatabaseName,
        (SELECT COUNT(*) FROM ExerciseTypes) AS CatalogCount,
        (SELECT COUNT(*) FROM Exercises) AS HistoryCount,
        (
            SELECT COALESCE(SUM(CRC32(CAST(Id AS CHAR))), 0)
            FROM Exercises
        ) AS HistoryIdChecksum,
        (
            SELECT COUNT(*)
            FROM ExerciseTypeMuscleAssignments
        ) AS MuscleAssignmentCount
),
phase1_state AS (
    SELECT
        (
            SELECT COUNT(*)
            FROM ExerciseTypes
            WHERE Id IN (26, 49, 53, 71)
        ) AS RemovedSourcesRemaining,
        (
            SELECT COUNT(*)
            FROM ExerciseTypes
            WHERE (Id = 21 AND BINARY TRIM(Name) = BINARY 'Face pull')
               OR (Id = 37 AND BINARY TRIM(Name) = BINARY 'Press de banca con mancuernas')
               OR (Id = 68 AND BINARY TRIM(Name) = BINARY 'Sentadilla goblet con kettlebell')
               OR (Id = 950 AND BINARY TRIM(Name) = BINARY 'Extensión de tríceps en polea')
        ) AS CanonicalTargetsReady
),
hierarchy_state AS (
    SELECT
        (SELECT COUNT(*) FROM hierarchy_plan) AS PlanRows,
        (
            SELECT COUNT(*)
            FROM hierarchy_plan p
            JOIN ExerciseTypes child ON child.Id = p.ChildId
            WHERE child.ParentExerciseTypeId IS NULL
        ) AS UnassignedChildrenReady,
        (
            SELECT COUNT(*)
            FROM (
                SELECT DISTINCT ParentId
                FROM hierarchy_plan
            ) planned_parents
            JOIN ExerciseTypes parent ON parent.Id = planned_parents.ParentId
            WHERE parent.ParentExerciseTypeId IS NULL
        ) AS RootParentsReady,
        (
            SELECT COUNT(*)
            FROM ExerciseTypes
            WHERE (Id = 23 AND ParentExerciseTypeId = 12)
               OR (Id = 36 AND ParentExerciseTypeId = 12)
               OR (Id = 37 AND ParentExerciseTypeId = 12)
        ) AS ExistingLinksReady
),
name_state AS (
    SELECT
        (SELECT COUNT(*) FROM name_plan) AS PlanRows,
        (
            SELECT COUNT(*)
            FROM name_plan p
            JOIN ExerciseTypes et ON et.Id = p.ExerciseTypeId
        ) AS ExistingRows,
        (
            SELECT COUNT(*)
            FROM name_plan p
            JOIN ExerciseTypes et ON et.Id = p.ExerciseTypeId
            WHERE BINARY TRIM(et.Name) = BINARY TRIM(p.ExpectedCurrentName)
        ) AS CurrentNamesMatched
),
muscle_state AS (
    SELECT
        (SELECT COUNT(*) FROM muscle_plan) AS PlanRows,
        (
            SELECT COUNT(DISTINCT p.ExerciseTypeId)
            FROM muscle_plan p
            JOIN ExerciseTypes et ON et.Id = p.ExerciseTypeId
        ) AS ExerciseTypesReady,
        (
            SELECT COUNT(DISTINCT p.MuscleId)
            FROM muscle_plan p
            JOIN Muscles m ON m.Id = p.MuscleId
        ) AS MusclesReady,
        (
            SELECT COUNT(*)
            FROM muscle_plan p
            JOIN ExerciseTypeMuscleAssignments a
              ON a.ExerciseTypeId = p.ExerciseTypeId
             AND a.MuscleId = p.MuscleId
        ) AS ExistingPlanAssignments,
        (
            SELECT COUNT(*)
            FROM muscle_removals r
            JOIN ExerciseTypeMuscleAssignments a
              ON a.ExerciseTypeId = r.ExerciseTypeId
             AND a.MuscleId = r.MuscleId
        ) AS RemovalAssignmentsReady
),
current_integrity AS (
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
        ) AS IssueCount
),
projected_catalog AS (
    SELECT
        et.Id,
        COALESCE(
            p.CanonicalName,
            CONVERT(et.Name USING utf8mb4) COLLATE utf8mb4_unicode_ci
        ) AS Name,
        COALESCE(h.ParentId, et.ParentExerciseTypeId) AS ParentId
    FROM ExerciseTypes et
    LEFT JOIN name_plan p ON p.ExerciseTypeId = et.Id
    LEFT JOIN hierarchy_plan h ON h.ChildId = et.Id
),
projection_state AS (
    SELECT
        (
            SELECT COUNT(*)
            FROM projected_catalog child
            LEFT JOIN projected_catalog parent ON parent.Id = child.ParentId
            WHERE child.ParentId IS NOT NULL
              AND (
                  parent.Id IS NULL
                  OR child.Id = child.ParentId
                  OR parent.ParentId IS NOT NULL
              )
        ) AS HierarchyIssues,
        (
            SELECT COUNT(*)
            FROM (
                SELECT LOWER(TRIM(Name)) AS NormalizedName
                FROM projected_catalog
                GROUP BY LOWER(TRIM(Name))
                HAVING COUNT(*) > 1
            ) duplicate_groups
        ) AS DuplicateNameGroups,
        (
            SELECT b.MuscleAssignmentCount
                   - m.RemovalAssignmentsReady
                   + m.PlanRows
                   - m.ExistingPlanAssignments
            FROM baseline b
            CROSS JOIN muscle_state m
        ) AS ProjectedMuscleAssignmentCount
),
base_checks AS (
    SELECT
        3 AS CheckOrder,
        CONVERT('database' USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS CheckCode,
        CONVERT(
            IF(b.DatabaseName = 'healthspan-dev', 'PASS', 'FAIL')
            USING utf8mb4
        ) COLLATE utf8mb4_unicode_ci AS Status,
        CONVERT(COALESCE(b.DatabaseName, 'NULL') USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS ActualValue,
        CONVERT('healthspan-dev' USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS ExpectedValue,
        CONVERT('Preview is locked to the development database' USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS Detail
    FROM baseline b

    UNION ALL
    SELECT
        4,
        'catalog_baseline',
        IF(b.CatalogCount = 72, 'PASS', 'FAIL'),
        CAST(b.CatalogCount AS CHAR),
        '72',
        'Phase 2 must not add or delete exercise types'
    FROM baseline b

    UNION ALL
    SELECT
        5,
        'history_baseline',
        IF(b.HistoryCount >= 2072, 'PASS', 'FAIL'),
        CONCAT(b.HistoryCount, ' rows; checksum=', b.HistoryIdChecksum),
        '>= 2072 rows',
        'No history rows or IDs are changed by this phase'
    FROM baseline b

    UNION ALL
    SELECT
        6,
        'phase1_state',
        IF(
            p.RemovedSourcesRemaining = 0
            AND p.CanonicalTargetsReady = 4,
            'PASS',
            'FAIL'
        ),
        CONCAT(
            p.RemovedSourcesRemaining,
            ' removed sources remain; ',
            p.CanonicalTargetsReady,
            '/4 targets ready'
        ),
        '0 removed sources; 4/4 targets ready',
        'Confirms the committed Phase 1 baseline'
    FROM phase1_state p

    UNION ALL
    SELECT
        7,
        'hierarchy_entities',
        IF(
            h.PlanRows = 31
            AND h.UnassignedChildrenReady = 31
            AND h.RootParentsReady = 12,
            'PASS',
            'FAIL'
        ),
        CONCAT(
            h.UnassignedChildrenReady,
            '/31 children; ',
            h.RootParentsReady,
            '/12 parents'
        ),
        '31/31 children; 12/12 root parents',
        'Every planned variation and root must exist'
    FROM hierarchy_state h

    UNION ALL
    SELECT
        8,
        'existing_hierarchy',
        IF(h.ExistingLinksReady = 3, 'PASS', 'FAIL'),
        CONCAT(h.ExistingLinksReady, '/3 existing links'),
        '3/3 existing links',
        'Keeps exercise types 23, 36, and 37 under parent 12'
    FROM hierarchy_state h

    UNION ALL
    SELECT
        9,
        'naming_plan',
        IF(
            n.PlanRows = 29
            AND n.ExistingRows = 29
            AND n.CurrentNamesMatched = 29,
            'PASS',
            'FAIL'
        ),
        CONCAT(n.CurrentNamesMatched, '/29 current names matched'),
        '29/29 current names matched',
        'Prevents overwriting catalog edits made after the audit'
    FROM name_state n

    UNION ALL
    SELECT
        10,
        'muscle_baseline',
        IF(b.MuscleAssignmentCount = 143, 'PASS', 'FAIL'),
        CAST(b.MuscleAssignmentCount AS CHAR),
        '143',
        'Confirms the post-Phase-1 assignment baseline'
    FROM baseline b

    UNION ALL
    SELECT
        11,
        'muscle_plan',
        IF(
            m.PlanRows = 26
            AND m.ExerciseTypesReady = 13
            AND m.MusclesReady = 14
            AND m.ExistingPlanAssignments = 0
            AND m.RemovalAssignmentsReady = 3,
            'PASS',
            'FAIL'
        ),
        CONCAT(
            m.PlanRows,
            ' additions; ',
            m.ExerciseTypesReady,
            ' exercises; ',
            m.MusclesReady,
            ' muscles; ',
            m.ExistingPlanAssignments,
            ' overlaps; ',
            m.RemovalAssignmentsReady,
            ' removals'
        ),
        '26 additions; 13 exercises; 14 muscles; 0 overlaps; 3 removals',
        'Repairs are locked to the audited empty/incorrect assignments'
    FROM muscle_state m

    UNION ALL
    SELECT
        12,
        'current_integrity',
        IF(i.IssueCount = 0, 'PASS', 'FAIL'),
        CAST(i.IssueCount AS CHAR),
        '0',
        'History, hierarchy, favorites, templates, and muscles'
    FROM current_integrity i

    UNION ALL
    SELECT
        13,
        'projected_hierarchy',
        IF(p.HierarchyIssues = 0, 'PASS', 'FAIL'),
        CAST(p.HierarchyIssues AS CHAR),
        '0',
        'Projection rejects missing, self, and nested parents'
    FROM projection_state p

    UNION ALL
    SELECT
        14,
        'projected_duplicate_names',
        IF(p.DuplicateNameGroups = 0, 'PASS', 'FAIL'),
        CAST(p.DuplicateNameGroups AS CHAR),
        '0',
        'Canonical names must not create exact duplicate groups'
    FROM projection_state p

    UNION ALL
    SELECT
        15,
        'projected_muscles',
        IF(p.ProjectedMuscleAssignmentCount = 166, 'PASS', 'FAIL'),
        CONCAT('143 -> ', p.ProjectedMuscleAssignmentCount),
        '143 -> 166',
        'Twenty-six additions minus three incorrect assignments'
    FROM projection_state p
),
validation AS (
    SELECT COUNT(*) AS ErrorCount
    FROM base_checks
    WHERE Status = 'FAIL'
),
report AS (
    SELECT
        1 AS CheckOrder,
        CONVERT('overall_preview' USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS CheckCode,
        CONVERT(IF(v.ErrorCount = 0, 'PASS', 'FAIL') USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS Status,
        CONVERT(
            IF(v.ErrorCount = 0, 'All checks passed', 'One or more checks failed')
            USING utf8mb4
        ) COLLATE utf8mb4_unicode_ci AS ActualValue,
        CONVERT('All checks passed' USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS ExpectedValue,
        CONVERT('Read-only projection; no changes were attempted' USING utf8mb4)
            COLLATE utf8mb4_unicode_ci AS Detail
    FROM validation v

    UNION ALL
    SELECT
        2,
        'validation_errors',
        IF(v.ErrorCount = 0, 'PASS', 'FAIL'),
        CAST(v.ErrorCount AS CHAR),
        '0',
        IF(v.ErrorCount = 0, 'None', 'Review failed checks below')
    FROM validation v

    UNION ALL
    SELECT
        CheckOrder,
        CheckCode,
        Status,
        ActualValue,
        ExpectedValue,
        Detail
    FROM base_checks

    UNION ALL
    SELECT
        16,
        'manual_reviews',
        'INFO',
        '8 candidate groups deferred',
        'Informational',
        'Ambiguous records are not changed by Phase 2'

    UNION ALL
    SELECT
        17,
        'persistence',
        'PASS',
        'READ_ONLY',
        'READ_ONLY',
        'This SQL statement cannot modify the database'
)
SELECT
    CheckOrder,
    CheckCode,
    Status,
    ActualValue,
    ExpectedValue,
    Detail
FROM report
ORDER BY CheckOrder;
