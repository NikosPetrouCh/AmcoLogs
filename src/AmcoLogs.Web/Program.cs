using System.Text.Json.Serialization;
using AmcoLogs.Core.Excel;
using AmcoLogs.Core.Logs;
using AmcoLogs.Core.Persistence;
using AmcoLogs.Core.ThingsBoard;
using AmcoLogs.Infrastructure.Excel;
using AmcoLogs.Infrastructure.Logs;
using AmcoLogs.Infrastructure.Persistence;
using AmcoLogs.Infrastructure.ThingsBoard;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<LocalConnectionStore>();
builder.Services.AddSingleton<IExcelImportParser, ClosedXmlExcelImportParser>();
builder.Services.AddSingleton<IAmcoLogsStore, MySqlAmcoLogsStore>();
builder.Services.AddSingleton(new ThingsBoardSettings
{
    Username = builder.Configuration["ThingsBoard:Username"],
    Password = builder.Configuration["ThingsBoard:Password"]
});
builder.Services.AddSingleton<IThingsBoardLookup, PlaywrightThingsBoardLookup>();
builder.Services.AddSingleton(new DeviceAccessSettings
{
    Username = builder.Configuration["DeviceAccess:Username"],
    Password = builder.Configuration["DeviceAccess:Password"],
    Port = int.TryParse(builder.Configuration["DeviceAccess:Port"], out var port) ? port : 22,
    RemoteLogPath = builder.Configuration["DeviceAccess:RemoteLogPath"] ?? "/opt/amco/middleware/TVMserver",
    PingTimeoutMs = int.TryParse(builder.Configuration["DeviceAccess:PingTimeoutMs"], out var pingMs) ? pingMs : 2500,
    WinScpPath = builder.Configuration["DeviceAccess:WinScpPath"]
});
builder.Services.AddSingleton<ILogDownloadJob, RemoteLogDownloader>();
builder.Services.AddSingleton<INlogSaleLocator, NlogSaleLocator>();
builder.Services.AddSingleton<SaleInvestigation>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 314_572_800;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 314_572_800;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
