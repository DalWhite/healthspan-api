using CsvHelper;
using CsvHelper.TypeConversion;
using System.Globalization;

public class CsvService
{
    private readonly MainDbContext _context;
    public CsvService(MainDbContext context)
    {
        _context = context;
    }
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
    public Dictionary<DateTime, List<Exercise>> ReadCsvExercisesAndGroupByDate(IFormFile file)
    {
        var exerciseGroups = new Dictionary<DateTime, List<Exercise>>();

        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            var csvConfig = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
                HeaderValidated = null,
                Delimiter = ","
            };

            using (var csv = new CsvReader(reader, csvConfig))
            {
                csv.Read();
                csv.ReadHeader();
                var options = new TypeConverterOptions { Formats = ["dd/MM/yy"] };
                csv.Context.TypeConverterOptionsCache.AddOptions<DateTime>(options);
                while (csv.Read())
                {
                    var date = csv.GetField<DateTime>("Date");
                    var typeId = EnsureExerciseTypeExists(csv.GetField("ExerciseType"));

                    var exercise = new Exercise
                    {
                        Series = csv.GetField<int?>("Series"),
                        Repetitions = csv.GetField<int?>("Reps"),
                        Weight = csv.GetField<double?>("Weight"),
                        Comments = csv.GetField("AdditionalComments"),
                        TypeId = typeId,
                    };

                    if (!exerciseGroups.ContainsKey(date))
                    {
                        exerciseGroups[date] = new List<Exercise>();
                    }
                    exerciseGroups[date].Add(exercise);
                }
            }
        }

        return exerciseGroups;
    }
    private int EnsureExerciseTypeExists(string typeName)
    {
        var type = _context.ExerciseTypes.SingleOrDefault(t => t.Name == typeName);
        if (type == null)
        {
            type = new ExerciseType { Name = typeName };
            _context.ExerciseTypes.Add(type);
            _context.SaveChanges();
        }
        return type.Id;
    }
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