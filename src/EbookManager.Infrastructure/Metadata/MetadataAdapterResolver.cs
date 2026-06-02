using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;

namespace EbookManager.Infrastructure.Metadata;

public sealed class MetadataAdapterResolver : IMetadataAdapterResolver
{
    private readonly IMetadataAdapter fallbackAdapter;
    private readonly IReadOnlyDictionary<EbookFormat, IMetadataAdapter> specificAdaptersByFormat;

    public MetadataAdapterResolver(IEnumerable<IMetadataAdapter> adapters)
    {
        var adapterList = adapters.ToList();
        var fallbackAdapters = adapterList.OfType<FallbackMetadataAdapter>().ToArray();
        if (fallbackAdapters.Length != 1)
        {
            throw new InvalidOperationException("Exactly one fallback metadata adapter must be registered.");
        }

        fallbackAdapter = fallbackAdapters[0];
        var specificAdapters = adapterList.Where(adapter => adapter is not FallbackMetadataAdapter).ToArray();
        var mappings = new Dictionary<EbookFormat, IMetadataAdapter>();

        foreach (var adapter in specificAdapters)
        {
            foreach (var format in EbookFormatExtensions.Supported.Where(adapter.CanHandle))
            {
                if (mappings.TryGetValue(format, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Multiple metadata adapters claim format '{format}': " +
                        $"{existing.GetType().Name} and {adapter.GetType().Name}.");
                }

                mappings[format] = adapter;
            }
        }

        specificAdaptersByFormat = mappings;
    }

    public IMetadataAdapter Resolve(EbookFormat format) =>
        specificAdaptersByFormat.TryGetValue(format, out var adapter)
            ? adapter
            : fallbackAdapter;
}
