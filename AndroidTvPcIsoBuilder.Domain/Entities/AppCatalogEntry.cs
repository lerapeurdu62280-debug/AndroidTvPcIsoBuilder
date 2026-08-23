namespace AndroidTvPcIsoBuilder.Domain.Entities;

public sealed record AppCatalogEntry(
    string Id,
    string DisplayName,
    string Description,
    string PackageId,
    string Category);
