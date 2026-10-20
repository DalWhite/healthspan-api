#nullable enable

/// <summary>
/// How a session was performed. Independent from the goal: the same goal can be
/// trained with different modalities.
/// </summary>
public enum WorkoutModality
{
    Fuerza,
    Funcional,
    Pliometria,
    Movilidad,
    JiuJitsu,
    Tenis,
    Kitesurf,
    Cardio,
    Caminata,
    Otro
}

/// <summary>
/// Where the load of a logged exercise comes from.
/// </summary>
public enum ExerciseLoadType
{
    Bodyweight,
    External,
    Band,
    Kettlebell,
    Machine,
    Poles,
    Other
}
