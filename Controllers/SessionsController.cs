#nullable enable
using Controllers.ApiContracts.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

namespace Controllers;

/// <summary>
/// Guided sessions (MVP): POST /sessions/from-plan starts or resumes today's
/// guided session (idempotent by DailySessionKey user+day) and GET
/// /sessions/{sessionId} returns the frozen snapshot with derived progress and
/// an ETag "v{version}". No /sessions routes existed before this controller.
/// </summary>
[Authorize]
[ApiController]
[Route("sessions")]
public class SessionsController(
    CurrentUserProvider userProvider,
    RoutinesRepository routinesRepository,
    GuidedSessionsRepository guidedSessionsRepository,
    RoutinePlanService planService,
    UsersRepository usersRepository) : ControllerBase
{
    /// <summary>
    /// 201 created / 200 resumed. 400 plan_empty, 404 routine_not_found or
    /// template_not_found, 409 session_completed. Never reopens a completed
    /// session and never creates a second session for the day.
    /// </summary>
    [HttpPost("from-plan")]
    public async Task<IActionResult> CreateFromPlan(
        [FromBody] SessionFromPlanRequest request,
        CancellationToken cancellationToken)
    {
        var userId = userProvider.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (request.ClientSessionId == Guid.Empty)
        {
            return ApiProblemResult.Validation(
                "invalid_client_session_id",
                "clientSessionId is required (FE-generated UUID).");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Spec precedence: a completed session for today is a hard 409, even
        // when the routine is unknown or today's plan has since emptied out
        // (weekly quota met). Never reopens and never creates a second row.
        var existingToday = await guidedSessionsRepository.GetTodaySessionAsync(
            userId,
            today,
            cancellationToken);
        if (existingToday?.CompletedAt is not null)
        {
            return ApiProblemResult.Conflict(
                "session_completed",
                "Today's guided session is already completed.");
        }

        var routine = await routinesRepository.GetRoutineAsync(
            request.RoutineId,
            userId,
            cancellationToken);
        if (routine is null)
        {
            return ApiProblemResult.NotFound(
                "routine_not_found",
                "Routine not found for this user.");
        }

        var template = await ResolveTemplateAsync(routine, request.WorkoutTemplateId, cancellationToken);
        if (template is null)
        {
            // Explicit id unknown → template_not_found; nothing planned for
            // today → plan_empty (checked below on slots).
            return request.WorkoutTemplateId is null
                ? ApiProblemResult.Validation("plan_empty", "No workout is planned for this routine today.")
                : ApiProblemResult.NotFound("template_not_found", "Template not found in this routine.");
        }

        var slots = (template.Exercises ?? [])
            .OrderBy(exercise => exercise.Order)
            .Select(MapSlot)
            .ToList();
        if (slots.Count == 0)
        {
            return ApiProblemResult.Validation("plan_empty", "The planned workout has no exercises.");
        }

        var snapshot = new GuidedSessionSnapshotRecord
        {
            RoutineId = routine.Id,
            TemplateId = template.Id,
            CoachSigned = routine.TrainerId is not null,
            CoachName = routine.TrainerId is not null
                ? usersRepository.GetUser(routine.TrainerId)?.Name
                : null,
            StartedAt = DateTime.UtcNow,
            Slots = slots,
        };

        var (result, session) = await guidedSessionsRepository.CreateOrResumeFromPlanAsync(
            userId,
            today,
            template.Type,
            request.ClientSessionId,
            GuidedSessionSnapshotJson.Serialize(snapshot),
            cancellationToken);

        if (result == GuidedSessionFromPlanResult.AlreadyCompleted)
        {
            return ApiProblemResult.Conflict(
                "session_completed",
                "Today's guided session is already completed.");
        }

        // Resumed responses echo the FROZEN snapshot stored at creation, never
        // a re-resolution of the (possibly edited) routine.
        var stored = GuidedSessionSnapshotJson.Deserialize(session.GuidedSnapshotJson) ?? snapshot;
        var resource = GuidedSessionMapper.BuildResource(session, stored);
        Response.Headers.ETag = GuidedSessionMapper.ETagOf(resource.Version);

        return result == GuidedSessionFromPlanResult.Created
            ? Created($"/sessions/{session.Id}", resource)
            : Ok(resource);
    }

    /// <summary>
    /// Exact GuidedSession snapshot + ETag "v{version}". A matching
    /// If-None-Match answers 304. Owner or admin only; anyone else (coach
    /// included) gets 403 wrong_role.
    /// </summary>
    [HttpGet("{sessionId:guid}")]
    public async Task<IActionResult> GetSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var userId = userProvider.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var session = await guidedSessionsRepository.GetSessionAsync(sessionId, cancellationToken);
        var snapshot = GuidedSessionSnapshotJson.Deserialize(session?.GuidedSnapshotJson);
        if (session is null || snapshot is null)
        {
            return ApiProblemResult.NotFound("session_not_found", "Guided session not found.");
        }

        // Closed spec: owner or admin only. Coaches do NOT read guided sessions.
        if (session.UserId != userId && !User.IsInRole("Admin"))
        {
            return ApiProblemResult.Forbidden("wrong_role", "Only the owner or an admin can access this session.");
        }

        var resource = GuidedSessionMapper.BuildResource(session, snapshot);
        var etag = GuidedSessionMapper.ETagOf(resource.Version);
        Response.Headers.ETag = etag;

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (GuidedSessionMapper.IfNoneMatchMatches(ifNoneMatch, etag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(resource);
    }

    private async Task<WorkoutTemplate?> ResolveTemplateAsync(
        Routine routine,
        int? workoutTemplateId,
        CancellationToken cancellationToken)
    {
        var templates = routine.WorkoutTemplates ?? [];

        if (workoutTemplateId is int templateId)
        {
            return templates.FirstOrDefault(template => template.Id == templateId);
        }

        var plan = await planService.GetPlanAsync(
            routine.UserId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            cancellationToken);
        var planEntry = plan.FirstOrDefault(entry => entry.RoutineId == routine.Id);
        return planEntry is null
            ? null
            : templates.FirstOrDefault(template => template.Id == planEntry.TemplateId);
    }

    private static GuidedSessionSlotResource MapSlot(WorkoutTemplateExercise exercise)
    {
        var assignments = exercise.ExerciseType?.MuscleAssignments ?? [];
        return new GuidedSessionSlotResource
        {
            SlotId = exercise.Id,
            Order = exercise.Order,
            ExerciseTypeId = exercise.ExerciseTypeId,
            Name = exercise.ExerciseType?.Name ?? "",
            ImageRoute = exercise.ExerciseType?.ImageRoute,
            Sets = exercise.Sets ?? 0,
            Reps = exercise.Reps ?? 0,
            LoadKg = exercise.LoadKg ?? 0,
            RestSeconds = exercise.RestSeconds ?? 0,
            Tip = exercise.Notes,
            Muscles = new GuidedSessionSlotMusclesResource
            {
                Primary = assignments
                    .Where(assignment => assignment.Role == ExerciseMuscleRole.Primary)
                    .Select(assignment => assignment.Muscle?.Slug)
                    .Where(slug => slug is not null)
                    .Select(slug => slug!)
                    .ToList(),
                Secondary = assignments
                    .Where(assignment => assignment.Role == ExerciseMuscleRole.Secondary)
                    .Select(assignment => assignment.Muscle?.Slug)
                    .Where(slug => slug is not null)
                    .Select(slug => slug!)
                    .ToList(),
            },
        };
    }
}
