using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

builder.Services.AddDbContext<MainDbContext>(
       options => options.UseMySQL("Server=localhost;Database=HealthspanDb;User ID=root;Password=Passw0rd;"));

builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<WorkoutsRepository>();
builder.Services.AddScoped<ExercisesRepository>();
builder.Services.AddScoped<ExerciseTypesRepository>();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();

app.UseHttpsRedirection();
app.UseCors(o =>
{
    o.AllowAnyOrigin();
    o.AllowAnyHeader();
});


app.Run();

//elephantsql
//felece8350@htoal.com
//Z$eTeAVhgdnM-7z

//scalingo
//joxip21594@mugadget.com
//a*SHVYw@dhF.37a

