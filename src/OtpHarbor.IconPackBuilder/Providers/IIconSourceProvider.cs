using OtpHarbor.IconPackBuilder.Domain;

namespace OtpHarbor.IconPackBuilder.Providers;

public interface IIconSourceProvider
{
    string Id { get; }
    Task<ProviderCatalog> LoadAsync(IconSourceInput input, CancellationToken cancellationToken = default);
}
