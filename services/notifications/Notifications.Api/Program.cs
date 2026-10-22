using Microsoft.EntityFrameworkCore;
using Notifications.Api.Messaging;
using Notifications.Api.Services;
using Notifications.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<NotificationsDbContext>(options =>
  options.UseSqlite(builder.Configuration.GetConnectionString("NotificationsDb")));
builder.Services.AddScoped<NotificationRecorder>();
builder.Services.AddScoped<NotificationReader>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddHostedService<TaskCompletedConsumer>();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
  {
    using var scope = app.Services.CreateScope();
  scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.Migrate();
  }

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
