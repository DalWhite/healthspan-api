using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

var dbConnectionString = builder.Configuration.GetSection("DbConnectionString").Value;
builder.Services.AddDbContext<MainDbContext>(
       options => options.UseMySQL(dbConnectionString));

builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<WorkoutsRepository>();
builder.Services.AddScoped<ExercisesRepository>();
builder.Services.AddScoped<ExerciseTypesRepository>();

var app = builder.Build();


app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.UseHttpsRedirection();
app.UseCors(o =>
{
    o.AllowAnyOrigin();
    o.AllowAnyHeader();
});


app.Run();
