using Microsoft.EntityFrameworkCore;
using SampleService.Models;
using SampleService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<PeopleDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString(PeopleDbContext.ConnectionStringName);
    options.UseNpgsql(connectionString);
});

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapGrpcService<GreeterService>();
app.MapGet("/",
    () =>
        "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
