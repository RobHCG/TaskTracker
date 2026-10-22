using System.ComponentModel.DataAnnotations;

namespace Tasks.Api.Contracts;

public record CreateTaskRequest(
    [Required, StringLength(200)] string Title,
    [StringLength(2000)] string? Description);
