using CsvHelper;
using System.Globalization;

public class CsvService
{
    public const int MaxImportRows = 50_000;
    public const long MaxImportBytes = 5 * 1024 * 1024;

    // dd/MM/yy plus unpadded day/month (the historical CSV uses 15/4/18) and ISO.
    private static readonly string[] DateFormats =
        ["dd/MM/yy", "d/M/yy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];

    public byte[] CreateCsvImportTemplate()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, leaveOpen: true);
        using var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csvWriter.WriteHeader<ExercisesImportTemplate>();
        csvWriter.NextRecord();
        csvWriter.WriteRecord(new ExercisesImportTemplate{
            Date = "01/01/25",
            ExerciseType = "example Bench press",
            Series = 5,
            Reps = 5,
            Weight = 100.5,
            AdditionalComments = "bilbo cile 2"
        });
        csvWriter.NextRecord();
        writer.Flush();

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Parses a CSV into valid rows plus per-row errors, without touching the
    /// database. Structural problems (missing header, row cap) go to
    /// StructuralErrors so the endpoint can answer 400 upfront; row-level
    /// problems (bad date, bad number) never abort the import (t_c1f8e6ac).
    /// </summary>
    public CsvImportParseResult ParseCsvRows(Stream stream)
    {
        var result = new CsvImportParseResult();

        if (FirstLineUsesSemicolonDelimiter(stream))
        {
            result.StructuralErrors.Add(
                "El CSV parece usar ';' como separador. Descarga la plantilla de comas en GET /data/import/template y vuelve a probar.");
            return result;
        }

        using var reader = new StreamReader(stream);
        var csvConfig = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            HeaderValidated = null,
            Delimiter = ","
        };

        using var csv = new CsvReader(reader, csvConfig);

        if (!csv.Read() || !csv.ReadHeader())
        {
            result.StructuralErrors.Add("El CSV no tiene fila de cabecera.");
            return result;
        }

        var header = csv.HeaderRecord;
        if (header == null ||
            !header.Contains("Date") ||
            !header.Contains("ExerciseType"))
        {
            result.StructuralErrors.Add("La cabecera debe incluir las columnas Date y ExerciseType.");
            return result;
        }

        while (csv.Read())
        {
            if (result.Rows.Count + result.Errors.Count >= MaxImportRows)
            {
                result.Rows.Clear();
                result.Errors.Clear();
                result.StructuralErrors.Add($"Demasiadas filas (máximo {MaxImportRows}).");
                return result;
            }

            var rowNumber = csv.Context.Parser.Row; // physical line in the file (header = 1)
            var dateRaw = csv.GetField("Date")?.Trim() ?? "";
            var typeName = csv.GetField("ExerciseType")?.Trim();

            if (!DateTime.TryParseExact(
                    dateRaw,
                    DateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                result.Errors.Add(new CsvImportRowError(rowNumber, $"Fecha inválida '{dateRaw}' (formato dd/MM/yy)."));
                continue;
            }

            if (string.IsNullOrEmpty(typeName))
            {
                result.Errors.Add(new CsvImportRowError(rowNumber, "Falta el nombre del ejercicio (ExerciseType)."));
                continue;
            }

            int? series = null;
            int? reps = null;
            double? weight = null;
            var invalid = false;

            var seriesRaw = csv.GetField("Series")?.Trim() ?? "";
            if (seriesRaw.Length > 0)
            {
                if (int.TryParse(seriesRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeries))
                {
                    series = parsedSeries;
                }
                else
                {
                    result.Errors.Add(new CsvImportRowError(rowNumber, $"Series inválido '{seriesRaw}'."));
                    invalid = true;
                }
            }

            var repsRaw = csv.GetField("Reps")?.Trim() ?? "";
            if (!invalid && repsRaw.Length > 0)
            {
                if (int.TryParse(repsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedReps))
                {
                    reps = parsedReps;
                }
                else
                {
                    result.Errors.Add(new CsvImportRowError(rowNumber, $"Repeticiones inválidas '{repsRaw}'."));
                    invalid = true;
                }
            }

            var weightRaw = csv.GetField("Weight")?.Trim() ?? "";
            if (!invalid && weightRaw.Length > 0)
            {
                if (double.TryParse(weightRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWeight))
                {
                    weight = parsedWeight;
                }
                else
                {
                    result.Errors.Add(new CsvImportRowError(rowNumber, $"Peso inválido '{weightRaw}'."));
                    invalid = true;
                }
            }

            if (invalid)
            {
                continue;
            }

            result.Rows.Add(new CsvImportRow(
                rowNumber,
                date,
                typeName,
                series,
                reps,
                weight,
                csv.GetField("AdditionalComments") ?? ""));
        }

        return result;
    }

    /// <summary>
    /// Reads just the first physical line (skipping a UTF-8 BOM) and reports
    /// whether it looks semicolon-delimited: contains ';' and no ','.
    /// A semicolon CSV with the comma-pinned config arrives as one giant
    /// header field, which previously surfaced as a bland structural 400;
    /// this turns it into an actionable message pointing to the template
    /// (t_020e68a5). Non-seekable streams are left untouched (no false
    /// positives, no rewinding side effects).
    /// </summary>
    private static bool FirstLineUsesSemicolonDelimiter(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        const int peekBytes = 4096;
        var origin = stream.Position;
        byte[] buffer = new byte[peekBytes];
        var bytesRead = stream.Read(buffer, 0, peekBytes);
        stream.Seek(origin, SeekOrigin.Begin);

        int start = 0;
        if (bytesRead >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            start = 3; // UTF-8 BOM
        }

        int end = start;
        while (end < bytesRead && buffer[end] != (byte)'\n' && buffer[end] != (byte)'\r')
        {
            end++;
        }

        var firstLine = System.Text.Encoding.UTF8.GetString(buffer, start, end - start);
        return firstLine.Contains(';') && !firstLine.Contains(',');
    }
}

public sealed record CsvImportRow(
    int RowNumber,
    DateTime Date,
    string TypeName,
    int? Series,
    int? Reps,
    double? Weight,
    string Comments);

public sealed record CsvImportRowError(int Row, string Message);

public sealed class CsvImportParseResult
{
    public List<CsvImportRow> Rows { get; } = [];
    public List<CsvImportRowError> Errors { get; } = [];
    public List<string> StructuralErrors { get; } = [];
}

public class ExercisesImportTemplate
{
    public string Date { get; set; }
    public string ExerciseType { get; set; }
    public int Series { get; set; }
    public int Reps { get; set; }
    public double Weight { get; set; }
    public string AdditionalComments { get; set; }
}
