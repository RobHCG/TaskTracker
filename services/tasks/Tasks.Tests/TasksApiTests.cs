using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tasks.Api.Contracts;
using Tasks.Api.Data;
using Tasks.Api.Events;
using Tasks.Api.Models;
using Tasks.Api.Services;

namespace Tasks.Tests;

public class TasksApiTests
{
  private SqliteConnection _keepAlive = null!;
  private WebApplicationFactory<Program> _factory = null!;
  private HttpClient _client = null!;

  [SetUp]
  public void SetUp()
  {
    var connectionString = $"Data Source=tests-{Guid.NewGuid()};Mode=Memory;Cache=Shared";
    _keepAlive = new SqliteConnection(connectionString);
    _keepAlive.Open();

    _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
          builder.UseSetting("ConnectionStrings:TasksDb", connectionString);
          builder.ConfigureTestServices(services => services.AddSingleton<IEventPublisher, FakeEventPublisher>());
        });

    using (var scope = _factory.Services.CreateScope())
    {
      scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.EnsureCreated();
    }

    _client = _factory.CreateClient();
  }

  [TearDown]
  public void TearDown()
  {
    _client.Dispose();
    _factory.Dispose();
    _keepAlive.Dispose();
  }

  [Test]
  public async Task Create_Returns201_AndTheTaskCanBeFetched()
  {
    var response = await _client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Prep for tech test", null));

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

    var fetched = await _client.GetFromJsonAsync<TaskItem>(response.Headers.Location);

    Assert.That(fetched!.Title, Is.EqualTo("Prep for tech test"));
  }

  [Test]
  public async Task Create_Returns400_WhenTitleIsEmpty()
  {
    var response = await _client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("", null));

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

    Assert.That(problem!.Errors.Keys, Does.Contain("Title"));
  }

  [Test]
  public async Task GetById_Returns404_WhenTaskDoesNotExist()
  {
    var response = await _client.GetAsync("/api/tasks/999");

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  [Test]
  public async Task Delete_Returns204_AndTheTaskIsGone()
  {
    var created = await _client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Delete me", null));

    var deleteResponse = await _client.DeleteAsync(created.Headers.Location);
    var getResponse = await _client.GetAsync(created.Headers.Location);

    using (Assert.EnterMultipleScope())
    {
      Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
      Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
  }

  [Test]
  public async Task UnexpectedError_Returns500ProblemDetails_WithoutLeakingDetails()
  {
    using var failingFactory = _factory.WithWebHostBuilder(builder =>
    {
      builder.UseEnvironment("Production");
      builder.ConfigureTestServices(services => services.AddScoped<ITaskService, ThrowingTaskService>());
    });
    using var client = failingFactory.CreateClient();

    var response = await client.GetAsync("/api/tasks");
    var body = await response.Content.ReadAsStringAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
      Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
      Assert.That(body, Does.Not.Contain("Database is down"));
    }
  }

  private class ThrowingTaskService : ITaskService
  {
    public Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken) => throw Failure();
    public Task<TaskItem?> GetByIdAsync(int id, CancellationToken cancellationToken) => throw Failure();
    public Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken) => throw Failure();
    public Task<TaskItem?> UpdateAsync(int id, UpdateTaskRequest request, CancellationToken cancellationToken) => throw Failure();
    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken) => throw Failure();

    private static InvalidOperationException Failure() => new("Database is down");
  }
}
