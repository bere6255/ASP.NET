using PaymentsApi.Extensions;
using PaymentsApi.Helpers;

EnvironmentHelper.LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();
app.UseApplicationPipeline();
app.Run();

public partial class Program { }
