var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseRouting();

app.MapStaticAssets();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
