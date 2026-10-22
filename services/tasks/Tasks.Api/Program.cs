using Microsoft.EntityFrameworkCore;
using Tasks.Api.Data;
using Tasks.Api.Events;
using Tasks.Api.Outbox;
using Tasks.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<TasksDbContext>(options =>
  options.UseSqlite(builder.Configuration.GetConnectionString("TasksDb")));
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
  using var scope = app.Services.CreateScope();
  scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.Migrate();
}

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
