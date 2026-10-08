using System.Text;
using System.Text.Json;
using System.Xml;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OtpHarbor.IconPackBuilder.Providers;

internal static partial class ProviderHelpers
{
    public static JsonDocument ParseJson(string json, string provider, string path)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
        }
        catch (JsonException ex)
        {
            throw new InputValidationException($"{provider}: malformed JSON in '{path}': {ex.Message}");
        }
    }

    public static string? ValidateSvg(byte[] bytes, string provider, string path, long maximumBytes)
    {
        if (bytes.Length == 0 || bytes.LongLength > maximumBytes)
            throw new InputValidationException($"{provider}: SVG '{path}' is empty or exceeds the {maximumBytes}-byte limit.");
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = maximumBytes
            });
            reader.MoveToContent();
            if (!reader.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
                throw new XmlException("Root element is not <svg>.");
            var foundGeometry = false;
            string? backgroundColor = null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var localReferences = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase)
                    || reader.LocalName.Equals("foreignObject", StringComparison.OrdinalIgnoreCase))
                    throw new XmlException("Executable SVG content is not supported.");
                var id = reader.GetAttribute("id");
                if (id is { Length: > 0 } && !ids.Add(id))
                    throw new XmlException("SVG contains duplicate identifiers.");
                ValidateSvgReference(reader.GetAttribute("href"), localReferences);
                ValidateSvgReference(
                    reader.GetAttribute("href", "http://www.w3.org/1999/xlink"),
                    localReferences);
                if (reader.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(reader.GetAttribute("d")))
                    foundGeometry = true;
                if (reader.LocalName is "circle" or "ellipse" or "rect" or "polygon" or "polyline" or "line" or "use")
                    foundGeometry = true;
                if (backgroundColor is null
                    && reader.LocalName.Equals("circle", StringComparison.OrdinalIgnoreCase)
                    && reader.GetAttribute("fill") is { } fill
                    && fill.StartsWith('#')
                    && IsHexColor(fill[1..]))
                    backgroundColor = fill.ToUpperInvariant();
            } while (reader.Read());
            if (!foundGeometry)
                throw new XmlException("SVG does not contain supported vector geometry.");
            if (localReferences.Any(reference => !ids.Contains(reference)))
                throw new XmlException("SVG contains an unresolved local reference.");
            return backgroundColor;
        }
        catch (XmlException ex)
        {
            throw new InputValidationException($"{provider}: malformed or unsafe SVG '{path}': {ex.Message}");
        }
    }

    public static IReadOnlyList<SourceLicense> ReadLicenses(SecureZipArchive archive, CancellationToken cancellationToken)
    {
        var paths = archive.Paths.Where(x =>
        {
            var name = Path.GetFileName(x);
            return name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LICENCE", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase)
                || name.Equals("DISCLAIMER.md", StringComparison.OrdinalIgnoreCase);
        }).OrderBy(x => x, StringComparer.Ordinal).Take(20).ToArray();

        var result = new List<SourceLicense>();
        foreach (var path in paths)
            result.Add(new SourceLicense(Path.GetFileName(path), archive.ReadAsync(path, 1_048_576, cancellationToken).GetAwaiter().GetResult()));
        return result;
    }

    public static string HumanizeSlug(string slug)
        => string.Join(' ', slug.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));

    public static bool IsSafeDisplayText(string? value, int maximumLength = 256)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);

    public static bool IsHexColor(string value)
        => value.Length == 6 && value.All(character => character is >= '0' and <= '9'
            or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    public static string SimpleSlug(string title)
    {
        var replaced = new StringBuilder(title.Length);
        foreach (var character in title.ToLowerInvariant())
        {
            replaced.Append(character switch
            {
                '+' => "plus",
                '.' => "dot",
                '&' => "and",
                'đ' => "d",
                'ħ' => "h",
                'ı' => "i",
                'ĸ' => "k",
                'ŀ' => "l",
                'ł' => "l",
                'ß' => "ss",
                'ŧ' => "t",
                'ø' => "o",
                _ => character.ToString()
            });
        }
        var decomposed = replaced.ToString().Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(character);
        }
        return slug.ToString();
    }

    private static void ValidateSvgReference(string? value, ISet<string> localReferences)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!SafeLocalSvgReferenceRegex().IsMatch(value))
            throw new XmlException("externally referenced SVG content is not supported.");
        localReferences.Add(value[1..]);
    }

    [GeneratedRegex("^#[A-Za-z_][A-Za-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeLocalSvgReferenceRegex();
}
