using System.Text.Json.Serialization;
using AmcoLogs.Core.Excel;
using AmcoLogs.Core.Persistence;
using AmcoLogs.Core.ThingsBoard;
using AmcoLogs.Infrastructure.Excel;
using AmcoLogs.Infrastructure.Persistence;
using AmcoLogs.Infrastructure.ThingsBoard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<LocalConnectionStore>();
builder.Services.AddSingleton<IExcelImportParser, ClosedXmlExcelImportParser>();
builder.Services.AddSingleton<IAmcoLogsStore, MySqlAmcoLogsStore>();
builder.Services.AddSingleton<IThingsBoardLookup, PlaywrightThingsBoardLookup>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 52_428_800;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
