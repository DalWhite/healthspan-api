using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

/// <summary>
/// Maps a database failure raised while inserting an import row to a short,
/// actionable, user-safe message (t_a4651a49). The per-row import errors used
/// to surface the wrapper text "Import batch insert failed", which told the
/// user nothing about WHY the row failed.
///
/// Rules:
///   - keyed on MySqlErrorCode, never on free-form message matching;
///   - the raw MySQL message is parsed only to extract the offending
///     column/constraint context (never echoed verbatim, so SQL fragments and
///     internal values don't leak into the API response);
///   - anything unmapped degrades to a generic message while the full
///     exception is logged internally by the caller.
/// </summary>
public static partial class CsvImportErrorMapper
{
    // MySQL error messages carry the offending column in quotes:
    //   "Data too long for column 'Comments' at row 1"
    //   "Incorrect integer value: 'x' for column 'Series' at row 1"
    //   "Column 'WorkoutId' cannot be null"
    //   "Unknown column 'Infinity' in 'field list'" (1054; MySqlConnector
    //    renders a non-finite double param as the bare token Infinity, so the
    //    quoted word here is the VALUE, not a column — see the 1054 case)
    [GeneratedRegex("for column '(?<col>[^']+)'|Column '(?<col>[^']+)' cannot be null", RegexOptions.IgnoreCase)]
    private static partial Regex ColumnRegex();

    [GeneratedRegex("Unknown column '(?<val>[^']+)' in 'field list'", RegexOptions.IgnoreCase)]
    private static partial Regex UnknownValueRegex();

    //   "Duplicate entry 'x' for key 'IX_Exercises_ClientEntityId'"
    //   "Cannot add or update a child row: a foreign key constraint fails (`db`.`Exercises`, CONSTRAINT `FK_...` ...)"
    [GeneratedRegex("for key '(?<key>[^']+)'|CONSTRAINT `(?<key>[^`]+)`", RegexOptions.IgnoreCase)]
    private static partial Regex KeyRegex();

    /// <summary>
    /// Returns the user-facing reason for a failed import row. <paramref name="rowNumber"/>
    /// is the physical CSV line (header = 1), already carried by the job error entry.
    /// </summary>
    public static string DescribeRowFailure(Exception exception, int rowNumber)
    {
        // The raw pipeline path throws MySqlException directly; the EF fallback
        // wraps it in DbUpdateException.
        var mysql = exception as MySqlException
            ?? (exception as DbUpdateException)?.InnerException as MySqlException;

        if (mysql is null)
        {
            return "No se pudo importar la fila por un error interno. Revisa los datos de la fila e inténtalo de nuevo.";
        }

        var column = ExtractColumn(mysql.Message);

        switch (mysql.Number) // MySqlException.Number is the raw server error code
        {
            case (int)MySqlErrorCode.DuplicateKeyEntry:
                var key = ExtractKey(mysql.Message);
                // The only unique index an import row can hit is the
                // idempotency id (Exercises.ClientEntityId).
                if (key is not null && key.Contains("ClientEntityId", StringComparison.OrdinalIgnoreCase))
                {
                    return "La fila ya fue importada anteriormente (identificador duplicado).";
                }
                return key is null
                    ? "Valor duplicado: ya existe una fila con el mismo identificador único."
                    : $"Valor duplicado en el índice '{key}'.";

            case (int)MySqlErrorCode.NoReferencedRow2: // FK on INSERT (1452)
            case (int)MySqlErrorCode.NoReferencedRow:  // FK on UPDATE (1451)
                return "La fila referencia un registro que no existe (restricción de integridad).";

            // 1406/1264/1292 have no named MySqlErrorCode member in
            // MySqlConnector 2.4; match the server codes numerically.
            case 1406: // DataTooLong
                return column is null
                    ? "Un valor de la fila es demasiado largo para la columna correspondiente."
                    : $"El valor de la columna '{column}' es demasiado largo.";

            case 1264: // DataOutOfRange (e.g. weight 1e999 -> Infinity)
                return column is null
                    ? "Un valor de la fila está fuera del rango permitido."
                    : $"El valor de la columna '{column}' está fuera del rango permitido.";

            case (int)MySqlErrorCode.TruncatedWrongValueForField: // 1366 (non-numeric text in a numeric column)
            case 1292: // 1292 (invalid date/enum value)
                return column is null
                    ? "Un valor de la fila no tiene el tipo correcto."
                    : $"El valor de la columna '{column}' no tiene el tipo correcto.";

            case (int)MySqlErrorCode.BadFieldError: // 1054
                // In the import pipeline every column name is ours, so a real
                // unknown column cannot happen; MySqlConnector renders a
                // non-finite double as the literal Infinity, which the server
                // reports as an unknown column. Report the offending value.
                var badValue = UnknownValueRegex().Match(mysql.Message) is { Success: true } m
                    ? m.Groups["val"].Value
                    : null;
                return badValue is null
                    ? "Un valor de la fila no tiene el tipo correcto."
                    : $"El valor '{badValue}' no es un número válido para esa columna.";

            case (int)MySqlErrorCode.ColumnCannotBeNull: // 1048
                return column is null
                    ? "Falta un valor obligatorio en la fila."
                    : $"La columna '{column}' no puede quedar vacía.";

            case 1213: // LockDeadlock
            case (int)MySqlErrorCode.LockWaitTimeout: // 1205
                return "La operación entró en conflicto con otra importación en curso. Vuelve a intentarlo.";

            default:
                return "No se pudo importar la fila por un error de la base de datos. Revisa los datos de la fila e inténtalo de nuevo.";
        }
    }

    private static string? ExtractColumn(string message) =>
        ColumnRegex().Match(message) is { Success: true } m ? m.Groups["col"].Value : null;

    private static string? ExtractKey(string message) =>
        KeyRegex().Match(message) is { Success: true } m
            ? m.Groups["key"].Value
            : null;
}
