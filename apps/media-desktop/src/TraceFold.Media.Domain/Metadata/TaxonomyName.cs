using System.Text;

namespace TraceFold.Media.Domain.Metadata;

public sealed record TaxonomyName
{
    private TaxonomyName(string display, string normalized)
    {
        Display = display;
        Normalized = normalized;
    }

    public string Display { get; }

    public string Normalized { get; }

    public static TaxonomyName Create(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        string display = input.Trim();
        string normalized = display.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return new TaxonomyName(display, normalized);
    }
}
