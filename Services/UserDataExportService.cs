#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

public sealed class UserDataExportService
{
    private readonly MainDbContext _context;

    public UserDataExportService(MainDbContext context)
    {
        _context = context;
    }

    public async Task<byte[]> CreateExportAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new
            {
                item.Name,
                item.Email,
                item.Height,
                item.Weight
            })
            .SingleAsync(cancellationToken);

        var profile = await _context.HealthProfiles
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new
            {
                item.DateOfBirth,
                item.BiologicalSex
            })
            .SingleOrDefaultAsync(cancellationToken);

        var favoriteExerciseTypes = await _context.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .SelectMany(item => item.FavoriteExercises)
            .OrderBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.Name
            })
            .ToListAsync(cancellationToken);

        var workouts = await _context.Workouts
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Include(item => item.Exercises)
            .ThenInclude(item => item.Type)
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);

        var bloodTests = await _context.BloodTests
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Include(item => item.MarkerResults)
            .OrderBy(item => item.TestDate)
            .ToListAsync(cancellationToken);

        var assessments = await _context.GalpinAssessments
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Include(item => item.Scores)
            .OrderBy(item => item.AssessedOnUtc)
            .ToListAsync(cancellationToken);

        var routines = await _context.Routines
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Include(item => item.WorkoutTemplates)
            .ThenInclude(item => item.ExerciseTypes)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

        var export = new
        {
            SchemaVersion = 1,
            ExportedAtUtc = DateTime.UtcNow,
            Profile = new
            {
                user.Name,
                user.Email,
                user.Height,
                user.Weight,
                profile?.DateOfBirth,
                profile?.BiologicalSex
            },
            FavoriteExerciseTypes = favoriteExerciseTypes,
            Workouts = workouts.Select(workout => new
            {
                workout.Id,
                workout.Date,
                workout.Type,
                Exercises = workout.Exercises
                    .OrderBy(exercise => exercise.Id)
                    .Select(exercise => new
                    {
                        exercise.Id,
                        exercise.TypeId,
                        ExerciseType = exercise.Type?.Name,
                        exercise.Series,
                        exercise.Repetitions,
                        exercise.Weight,
                        exercise.BodyWeight,
                        exercise.AddedWeight,
                        exercise.MaxRepetition,
                        exercise.Volume,
                        exercise.Intensity,
                        exercise.Comments,
                        exercise.Tags
                    })
            }),
            BloodTests = bloodTests.Select(test => new
            {
                test.Id,
                test.TestDate,
                test.LabName,
                test.SourceFileName,
                test.ProcessingStatus,
                test.ProcessingNotes,
                Markers = test.MarkerResults
                    .OrderBy(marker => marker.DisplayName)
                    .Select(marker => new
                    {
                        marker.MetricKey,
                        marker.DisplayName,
                        marker.RawMarkerName,
                        marker.Value,
                        marker.Unit,
                        marker.NormalizedValue,
                        marker.NormalizedUnit,
                        marker.RawValueText,
                        marker.Confidence,
                        marker.Status,
                        marker.ReferenceMinValue,
                        marker.ReferenceMaxValue
                    })
            }),
            GalpinAssessments = assessments.Select(assessment => new
            {
                assessment.Id,
                assessment.AssessedOnUtc,
                assessment.Notes,
                Scores = assessment.Scores
                    .OrderBy(score => score.Adaptation)
                    .Select(score => new
                    {
                        score.Adaptation,
                        score.Score,
                        score.EvidenceNotes
                    })
            }),
            Routines = routines.Select(routine => new
            {
                routine.Id,
                routine.Name,
                routine.Description,
                routine.Status,
                routine.CompletionDate,
                WorkoutTemplates = routine.WorkoutTemplates
                    .OrderBy(template => template.DaysOfWeek)
                    .Select(template => new
                    {
                        template.Id,
                        template.Type,
                        template.DaysOfWeek,
                        template.Goal,
                        ExerciseTypes = template.ExerciseTypes
                            .OrderBy(type => type.Name)
                            .Select(type => new
                            {
                                type.Id,
                                type.Name
                            })
                    })
            })
        };

        return JsonSerializer.SerializeToUtf8Bytes(
            export,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Converters =
                {
                    new JsonStringEnumConverter()
                }
            });
    }
}
