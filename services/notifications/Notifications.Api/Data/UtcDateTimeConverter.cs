using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Notifications.Api.Data;

public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value,
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
