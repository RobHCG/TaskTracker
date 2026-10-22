using System.ComponentModel.DataAnnotations;

namespace Tasks.Api.Contracts;

public record UpdateTaskRequest(
    [Required, StringLength(200)] string Title,
    [StringLength(2000)] string? Description,
    bool IsComplete);
