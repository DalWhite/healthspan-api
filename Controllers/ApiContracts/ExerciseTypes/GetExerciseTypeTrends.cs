public class GetExerciseTypeTrends
{
    public List<DataPoint> Points {get;set;}
}

public class DataPoint
{
    public float Data { get; set; }
    public DateTime Time { get; set; }
}