using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Normalization;

public sealed record SvgNormalizationResult(
    byte[] Svg,
    string BackgroundColor,
    IReadOnlyList<string> Operations,
    IReadOnlyList<string> ForegroundColors,
    SvgVisualMetrics VisualMetrics);

public static partial class SvgNormalizer
{
    private const int MaximumSvgBytes = 1024 * 1024;
    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    public static SvgNormalizationResult Normalize(
        byte[] input,
        string backgroundColor,
        bool materializeImplicitBlackFill = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length is 0 or > MaximumSvgBytes)
            throw new InputValidationException("SVG normalization input is empty or exceeds the 1 MiB limit.");

        XDocument document;
        try
        {
            using var stream = new MemoryStream(input, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumSvgBytes
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InputValidationException($"SVG normalization failed: {ex.Message}");
        }

        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
            throw new InputValidationException("SVG normalization requires an <svg> root.");

        var operations = new SortedSet<string>(StringComparer.Ordinal);
        ResolveCssVariables(root, operations);
        var viewBox = ReadViewBox(root, operations);
        ResolveGeometryPercentages(root, viewBox, operations);
        var ids = root.DescendantsAndSelf()
            .Select(element => (Element: element, Id: Attribute(element, "id")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single().Element, StringComparer.Ordinal);
        var css = ReadCss(root, operations);
        var gradients = ReadGradients(root, ids, operations);
        var paths = new List<XElement>();
        var activeReferences = new HashSet<string>(StringComparer.Ordinal);
        var initialStyle = materializeImplicitBlackFill
            ? StyleState.Empty with { Fill = "#000000" }
            : StyleState.Empty;
        VisitChildren(root, initialStyle, string.Empty, ids, css, gradients, activeReferences, paths, operations);

        if (paths.Count == 0)
            throw new InputValidationException("SVG normalization produced no visible path geometry.");

        var normalizedBackground = NormalizeColor(backgroundColor)
            ?? throw new InputValidationException($"SVG normalization received invalid background color '{backgroundColor}'.");
        if (TryPromoteBackground(paths, viewBox, out var promoted))
        {
            normalizedBackground = promoted;
            operations.Add("promoted-full-logo-background");
        }

        var foregroundColors = paths
            .SelectMany(path => new[] { (string?)path.Attribute("fill"), (string?)path.Attribute("stroke") })
            .Where(value => value is { Length: > 0 } && !value.Equals("none", StringComparison.OrdinalIgnoreCase))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (normalizedBackground.Equals("#334155", StringComparison.Ordinal)
            && ShouldUseLightNeutral(foregroundColors))
        {
            normalizedBackground = "#FFFFFF";
            operations.Add("selected-light-contrast-background");
        }

        var outputRoot = new XElement(SvgNamespace + "svg",
            new XAttribute("viewBox", FormatViewBox(viewBox)),
            paths);
        var output = new XDocument(outputRoot);
        using var result = new MemoryStream();
        using (var writer = XmlWriter.Create(result, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None
        }))
            output.Save(writer);
        if (result.Length > MaximumSvgBytes)
            throw new InputValidationException("Normalized SVG exceeds the 1 MiB application limit.");
        var normalizedBytes = result.ToArray();
        SvgVisualMetrics visualMetrics;
        try
        {
            visualMetrics = SvgVisualVerifier.Compare(input, normalizedBytes, normalizedBackground);
            if (!visualMetrics.Resolved)
            {
                // Prefer enough path-raster detail to remain crisp when OTP Harbor
                // renders above the 24 px comparison size. Thin or heavily
                // antialiased artwork can require an exact-size fallback to pass
                // the application's strict visual-equivalence gate.
                foreach (var rasterSize in new[] { 96, 48, 24 })
                {
                    var rasterBytes = SvgVisualVerifier.RasterFlatten(input, normalizedBackground, rasterSize);
                    if (rasterBytes.Length > MaximumSvgBytes) continue;
                    ValidateCanonical(rasterBytes, "normalized-raster.svg");
                    var rasterMetrics = SvgVisualVerifier.Compare(input, rasterBytes, normalizedBackground);
                    visualMetrics = rasterMetrics;
                    if (!rasterMetrics.Resolved) continue;
                    normalizedBytes = rasterBytes;
                    operations.Add($"raster-flattened-at-{rasterSize}px");
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not InputValidationException)
        {
            throw new InputValidationException($"SVG visual verification failed: {ex.GetType().Name}.");
        }
        if (!visualMetrics.Resolved)
            throw new InputValidationException($"SVG visual verification remained unresolved (mean difference {visualMetrics.MeanAbsoluteChannelDifference}, material pixels {visualMetrics.MateriallyDifferentPixels}).");
        if (normalizedBytes.Length > MaximumSvgBytes)
            throw new InputValidationException("Visually normalized SVG exceeds the 1 MiB application limit.");
        return new SvgNormalizationResult(normalizedBytes, normalizedBackground, operations.ToArray(), foregroundColors, visualMetrics);
    }

    public static void ValidateCanonical(byte[] input, string path)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(input, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumSvgBytes
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InputValidationException($"Canonical SVG '{path}' is malformed: {ex.Message}");
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "svg")
            throw new InputValidationException($"Canonical SVG '{path}' has an invalid root.");
        var viewBox = ParseNumbers(Attribute(root, "viewBox"));
        if (viewBox.Length != 4 || !viewBox.All(double.IsFinite) || viewBox[2] <= 0 || viewBox[3] <= 0)
            throw new InputValidationException($"Canonical SVG '{path}' has no finite positive viewBox.");
        var elements = root.DescendantsAndSelf().ToArray();
        var disallowed = elements.FirstOrDefault(element => element.Name.LocalName is not ("svg" or "path"));
        if (disallowed is not null)
            throw new InputValidationException($"Canonical SVG '{path}' contains unresolved <{disallowed.Name.LocalName}> content.");
        var paths = elements.Where(element => element.Name.LocalName == "path").ToArray();
        if (paths.Length == 0 || paths.Any(element => string.IsNullOrWhiteSpace(Attribute(element, "d"))))
            throw new InputValidationException($"Canonical SVG '{path}' has no path geometry.");
        foreach (var element in elements)
        {
            if (Attribute(element, "style") is not null || Attribute(element, "class") is not null
                || Attribute(element, "href") is not null)
                throw new InputValidationException($"Canonical SVG '{path}' contains unresolved styling or references.");
            foreach (var paintName in new[] { "fill", "stroke" })
                if (Attribute(element, paintName) is { } paint
                    && !paint.Equals("none", StringComparison.OrdinalIgnoreCase)
                    && NormalizeColor(paint) is null)
                    throw new InputValidationException($"Canonical SVG '{path}' contains unsupported {paintName} paint '{paint}'.");
        }
    }

    private static void VisitChildren(
        XElement parent,
        StyleState inherited,
        string inheritedTransform,
        IReadOnlyDictionary<string, XElement> ids,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> css,
        IReadOnlyDictionary<string, string> gradients,
        ISet<string> activeReferences,
        ICollection<XElement> output,
        ISet<string> operations)
    {
        foreach (var child in parent.Elements())
            Visit(child, inherited, inheritedTransform, ids, css, gradients, activeReferences, output, operations, fromUse: false);
    }

    private static void Visit(
        XElement element,
        StyleState inherited,
        string inheritedTransform,
        IReadOnlyDictionary<string, XElement> ids,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> css,
        IReadOnlyDictionary<string, string> gradients,
        ISet<string> activeReferences,
        ICollection<XElement> output,
        ISet<string> operations,
        bool fromUse)
    {
        var name = element.Name.LocalName;
        if (!fromUse && name.Equals("defs", StringComparison.OrdinalIgnoreCase)) return;
        if (name is "style" or "metadata" or "title" or "desc" or "namedview"
            or "linearGradient" or "radialGradient" or "stop" or "clipPath" or "mask"
            or "filter" or "pattern" or "marker") return;

        var style = ResolveStyle(element, inherited, css, operations);
        if (style.DisplayNone || style.VisibilityHidden || style.Opacity <= 0) return;
        var transform = CombineTransforms(inheritedTransform, Attribute(element, "transform"), operations);

        if (name.Equals("use", StringComparison.OrdinalIgnoreCase))
        {
            var href = Attribute(element, "href");
            if (href is null || !SafeLocalReferenceRegex().IsMatch(href) || !ids.TryGetValue(href[1..], out var target))
                throw new InputValidationException("SVG contains an unresolved or unsafe <use> reference.");
            if (!activeReferences.Add(href[1..])) throw new InputValidationException("SVG contains a cyclic <use> reference.");
            var x = ParseOptionalNumber(Attribute(element, "x"), 0);
            var y = ParseOptionalNumber(Attribute(element, "y"), 0);
            var useTransform = x == 0 && y == 0 ? transform : CombineTransforms(transform,
                $"translate({Format(x)} {Format(y)})", operations);
            Visit(target, style, useTransform, ids, css, gradients, activeReferences, output, operations, fromUse: true);
            activeReferences.Remove(href[1..]);
            operations.Add("expanded-use");
            return;
        }

        var pathData = name.ToLowerInvariant() switch
        {
            "path" => Attribute(element, "d"),
            "circle" => CirclePath(element),
            "ellipse" => EllipsePath(element),
            "rect" => RectanglePath(element),
            "polygon" => PointsPath(element, close: true),
            "polyline" => PointsPath(element, close: false),
            "line" => LinePath(element),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(pathData)) pathData = null;
        if (pathData is not null)
        {
            if (!name.Equals("path", StringComparison.OrdinalIgnoreCase)) operations.Add("converted-basic-shapes");
            var fill = ResolvePaint(style.Fill, style.Color, gradients, operations);
            var stroke = ResolvePaint(style.Stroke, style.Color, gradients, operations);
            if (!string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(stroke) && !stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                var path = new XElement(SvgNamespace + "path", new XAttribute("d", pathData));
                if (!string.IsNullOrWhiteSpace(fill)) path.SetAttributeValue("fill", fill);
                if (!string.IsNullOrWhiteSpace(stroke) && !stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    path.SetAttributeValue("stroke", stroke);
                    path.SetAttributeValue("stroke-width", Format(style.StrokeWidth <= 0 ? 1 : style.StrokeWidth));
                    operations.Add("resolved-stroke-paint");
                }
                var fillRule = style.FillRule;
                if (fillRule is "evenodd" or "nonzero") path.SetAttributeValue("fill-rule", fillRule);
                if (transform.Length > 0) path.SetAttributeValue("transform", transform);
                path.SetAttributeValue("data-otp-source-shape", name.ToLowerInvariant());
                output.Add(path);
            }
        }

        if (name is "svg" or "g" or "symbol" or "a" || pathData is null)
            foreach (var child in element.Elements())
                Visit(child, style, transform, ids, css, gradients, activeReferences, output, operations, fromUse);
    }

    private static StyleState ResolveStyle(
        XElement element,
        StyleState inherited,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> css,
        ISet<string> operations)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var classes = Attribute(element, "class")?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        foreach (var className in classes)
            if (css.TryGetValue(className, out var declarations))
                foreach (var declaration in declarations) values[declaration.Key] = declaration.Value;
        if (classes.Length > 0) operations.Add("resolved-css-classes");
        foreach (var property in PaintProperties)
            if (Attribute(element, property) is { } value) values[property] = value;
        if (Attribute(element, "style") is { } inline)
        {
            foreach (var declaration in ParseDeclarations(inline)) values[declaration.Key] = declaration.Value;
            operations.Add("resolved-inline-style");
        }

        var opacity = ParseOpacity(values.GetValueOrDefault("opacity"), inherited.Opacity);
        var fillOpacity = ParseOpacity(values.GetValueOrDefault("fill-opacity"), inherited.FillOpacity);
        var strokeOpacity = ParseOpacity(values.GetValueOrDefault("stroke-opacity"), inherited.StrokeOpacity);
        return new StyleState(
            values.GetValueOrDefault("fill") ?? inherited.Fill,
            values.GetValueOrDefault("stroke") ?? inherited.Stroke,
            values.GetValueOrDefault("color") ?? inherited.Color,
            ParseLength(values.GetValueOrDefault("stroke-width"), inherited.StrokeWidth),
            (values.GetValueOrDefault("fill-rule") ?? inherited.FillRule)?.ToLowerInvariant(),
            opacity,
            fillOpacity,
            strokeOpacity,
            string.Equals(values.GetValueOrDefault("display"), "none", StringComparison.OrdinalIgnoreCase),
            string.Equals(values.GetValueOrDefault("visibility"), "hidden", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadCss(XElement root, ISet<string> operations)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var style in root.Descendants().Where(x => x.Name.LocalName == "style"))
            foreach (Match match in CssClassRuleRegex().Matches(style.Value))
            {
                var declarations = ParseDeclarations(match.Groups[2].Value);
                foreach (var selector in match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (selector.Length > 1 && selector[0] == '.' && CssClassNameRegex().IsMatch(selector[1..]))
                        result[selector[1..]] = declarations;
            }
        if (result.Count > 0) operations.Add("parsed-css");
        return result;
    }

    private static void ResolveCssVariables(XElement root, ISet<string> operations)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var style in root.Descendants().Where(element => element.Name.LocalName == "style"))
            foreach (Match match in CssVariableDeclarationRegex().Matches(style.Value))
                variables[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        if (variables.Count == 0) return;

        string Resolve(string value)
        {
            for (var iteration = 0; iteration < 16; iteration++)
            {
                var changed = false;
                value = CssVariableReferenceRegex().Replace(value, match =>
                {
                    if (!variables.TryGetValue(match.Groups[1].Value, out var replacement)) return match.Value;
                    changed = true;
                    return replacement;
                });
                if (!changed) break;
            }
            return value;
        }

        foreach (var key in variables.Keys.ToArray()) variables[key] = Resolve(variables[key]);
        foreach (var attribute in root.DescendantsAndSelf().Attributes().ToArray())
            if (attribute.Value.Contains("var(", StringComparison.OrdinalIgnoreCase)) attribute.Value = Resolve(attribute.Value);
        foreach (var style in root.Descendants().Where(element => element.Name.LocalName == "style"))
            if (style.Value.Contains("var(", StringComparison.OrdinalIgnoreCase)) style.Value = Resolve(style.Value);
        operations.Add("resolved-css-variables");
    }

    private static Dictionary<string, string> ParseDeclarations(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf(':');
            if (separator <= 0) continue;
            result[item[..separator].Trim()] = item[(separator + 1)..].Trim().Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim();
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> ReadGradients(
        XElement root,
        IReadOnlyDictionary<string, XElement> ids,
        ISet<string> operations)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var gradient in root.Descendants().Where(x => x.Name.LocalName is "linearGradient" or "radialGradient"))
        {
            var id = Attribute(gradient, "id");
            if (id is null) continue;
            var stops = GradientStops(gradient, ids, new HashSet<string>(StringComparer.Ordinal)).ToArray();
            if (stops.Length == 0) continue;
            var total = stops.Aggregate((R: 0d, G: 0d, B: 0d, W: 0d), (sum, color) =>
                (sum.R + color.R, sum.G + color.G, sum.B + color.B, sum.W + 1));
            result[id] = $"#{(int)Math.Round(total.R / total.W):X2}{(int)Math.Round(total.G / total.W):X2}{(int)Math.Round(total.B / total.W):X2}";
        }
        if (result.Count > 0) operations.Add("flattened-solid-gradients");
        return result;
    }

    private static IEnumerable<(byte R, byte G, byte B)> GradientStops(
        XElement gradient,
        IReadOnlyDictionary<string, XElement> ids,
        ISet<string> active)
    {
        foreach (var stop in gradient.Elements().Where(x => x.Name.LocalName == "stop"))
        {
            var declarations = Attribute(stop, "style") is { } style ? ParseDeclarations(style) : [];
            var raw = Attribute(stop, "stop-color") ?? declarations.GetValueOrDefault("stop-color");
            var color = raw is null ? "#000000" : NormalizeColor(raw);
            if (color is { Length: 7 }) yield return ParseHex(color);
        }
        var href = Attribute(gradient, "href");
        if (href is not null && SafeLocalReferenceRegex().IsMatch(href) && active.Add(href[1..])
            && ids.TryGetValue(href[1..], out var referenced))
            foreach (var color in GradientStops(referenced, ids, active)) yield return color;
    }

    private static string? ResolvePaint(
        string? value,
        string? currentColor,
        IReadOnlyDictionary<string, string> gradients,
        ISet<string> operations)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase)
            || value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return "none";
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            operations.Add("resolved-current-color");
            return NormalizeColor(currentColor) ?? "#000000";
        }
        if (value.StartsWith("var(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
        {
            var separator = value.IndexOf(',');
            var fallback = separator > 0 ? value[(separator + 1)..^1].Trim() : null;
            operations.Add("resolved-css-variable-fallback");
            return NormalizeColor(fallback) ?? NormalizeColor(currentColor) ?? "#000000";
        }
        var match = LocalPaintRegex().Match(value);
        if (match.Success && gradients.TryGetValue(match.Groups[1].Value, out var gradient))
        {
            operations.Add("flattened-gradient-paint");
            return gradient;
        }
        if (match.Success)
        {
            operations.Add("flattened-empty-paint-server");
            return "none";
        }
        return NormalizeColor(value)
            ?? throw new InputValidationException($"SVG uses unsupported paint '{value}'.");
    }

    private static string? NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length == 4 && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
            return $"#{char.ToUpperInvariant(value[1])}{char.ToUpperInvariant(value[1])}{char.ToUpperInvariant(value[2])}{char.ToUpperInvariant(value[2])}{char.ToUpperInvariant(value[3])}{char.ToUpperInvariant(value[3])}";
        if (value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit)) return value.ToUpperInvariant();
        if (value.Length == 5 && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
            return value[4] == '0' ? "none" : NormalizeColor(value[..4]);
        if (value.Length == 9 && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
            return value[7..].Equals("00", StringComparison.OrdinalIgnoreCase) ? "none" : value[..7].ToUpperInvariant();
        if (value.Length == 10 && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
            return value[7..].All(character => character == '0') ? "none" : value[..7].ToUpperInvariant();
        if (NamedColors.TryGetValue(value, out var named)) return named;
        var systemColor = System.Drawing.Color.FromName(value);
        if (systemColor.IsKnownColor && systemColor.A > 0)
            return $"#{systemColor.R:X2}{systemColor.G:X2}{systemColor.B:X2}";
        var rgb = RgbColorRegex().Match(value);
        if (rgb.Success)
        {
            var values = Enumerable.Range(1, 3).Select(index => ParseRgbComponent(rgb.Groups[index].Value)).ToArray();
            if (values.All(component => component is >= 0 and <= 255)) return $"#{values[0]:X2}{values[1]:X2}{values[2]:X2}";
        }
        var colorFunction = ColorFunctionRegex().Match(value);
        if (colorFunction.Success)
        {
            var values = Enumerable.Range(1, 3)
                .Select(index => double.Parse(colorFunction.Groups[index].Value, CultureInfo.InvariantCulture))
                .Select(component => (int)Math.Round(Math.Clamp(component, 0, 1) * 255))
                .ToArray();
            return $"#{values[0]:X2}{values[1]:X2}{values[2]:X2}";
        }
        var hsl = HslColorRegex().Match(value);
        if (hsl.Success)
        {
            var hue = double.Parse(hsl.Groups[1].Value, CultureInfo.InvariantCulture) % 360;
            if (hue < 0) hue += 360;
            var saturation = Math.Clamp(double.Parse(hsl.Groups[2].Value, CultureInfo.InvariantCulture) / 100, 0, 1);
            var lightness = Math.Clamp(double.Parse(hsl.Groups[3].Value, CultureInfo.InvariantCulture) / 100, 0, 1);
            var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
            var component = hue switch
            {
                < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x),
                < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
            };
            var m = lightness - chroma / 2;
            return $"#{(int)Math.Round((component.Item1 + m) * 255):X2}{(int)Math.Round((component.Item2 + m) * 255):X2}{(int)Math.Round((component.Item3 + m) * 255):X2}";
        }
        return null;
    }

    private static int ParseRgbComponent(string value)
    {
        if (value.EndsWith('%')
            && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            return (int)Math.Round(Math.Clamp(percentage, 0, 100) * 2.55);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (int)Math.Round(number)
            : -1;
    }

    private static (double X, double Y, double Width, double Height) ReadViewBox(XElement root, ISet<string> operations)
    {
        var values = ParseNumbers(Attribute(root, "viewBox"));
        if (values.Length == 4 && values.All(double.IsFinite) && values[2] > 0 && values[3] > 0)
            return (values[0], values[1], values[2], values[3]);
        var width = ParseLength(Attribute(root, "width"), double.NaN);
        var height = ParseLength(Attribute(root, "height"), double.NaN);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new InputValidationException("SVG has no finite positive viewBox or numeric width/height.");
        operations.Add("derived-viewbox");
        return (0, 0, width, height);
    }

    private static void ResolveGeometryPercentages(
        XElement root,
        (double X, double Y, double Width, double Height) viewBox,
        ISet<string> operations)
    {
        foreach (var element in root.DescendantsAndSelf())
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (!attribute.Value.Trim().EndsWith('%')
                    || !double.TryParse(attribute.Value.Trim()[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
                    continue;
                var ratio = percentage / 100;
                double? resolved = attribute.Name.LocalName.ToLowerInvariant() switch
                {
                    "x" or "cx" or "x1" or "x2" => viewBox.X + ratio * viewBox.Width,
                    "y" or "cy" or "y1" or "y2" => viewBox.Y + ratio * viewBox.Height,
                    "width" or "rx" => ratio * viewBox.Width,
                    "height" or "ry" => ratio * viewBox.Height,
                    "r" => ratio * Math.Min(viewBox.Width, viewBox.Height),
                    _ => null
                };
                if (resolved is null) continue;
                attribute.Value = Format(resolved.Value);
                operations.Add("resolved-percentage-geometry");
            }
    }

    private static bool TryPromoteBackground(
        IList<XElement> paths,
        (double X, double Y, double Width, double Height) viewBox,
        out string color)
    {
        color = string.Empty;
        if (paths.Count < 2) return false;
        var first = paths[0];
        var shape = (string?)first.Attribute("data-otp-source-shape");
        var fill = (string?)first.Attribute("fill");
        if (fill is null || NormalizeColor(fill) is not { } normalized || first.Attribute("transform") is not null) return false;
        var d = (string?)first.Attribute("d") ?? string.Empty;
        var expectedRect = $"M{Format(viewBox.X)} {Format(viewBox.Y)}H{Format(viewBox.X + viewBox.Width)}V{Format(viewBox.Y + viewBox.Height)}H{Format(viewBox.X)}Z";
        var isFullRect = shape == "rect" && d == expectedRect;
        var isFullCircle = shape == "circle" && Math.Abs(viewBox.Width - viewBox.Height) < 0.000001
            && d == CirclePath(viewBox.X + viewBox.Width / 2, viewBox.Y + viewBox.Height / 2, viewBox.Width / 2);
        if (!isFullRect && !isFullCircle) return false;
        paths.RemoveAt(0);
        color = normalized;
        return true;
    }

    private static string? CirclePath(XElement element) => CirclePath(
        ParseRequiredNumber(element, "cx", 0), ParseRequiredNumber(element, "cy", 0), ParseRequiredNumber(element, "r"));

    private static string CirclePath(double cx, double cy, double r)
    {
        if (r <= 0) throw new InputValidationException("SVG circle radius must be positive.");
        return $"M{Format(cx - r)} {Format(cy)}A{Format(r)} {Format(r)} 0 1 0 {Format(cx + r)} {Format(cy)}A{Format(r)} {Format(r)} 0 1 0 {Format(cx - r)} {Format(cy)}Z";
    }

    private static string EllipsePath(XElement element)
    {
        var cx = ParseRequiredNumber(element, "cx", 0);
        var cy = ParseRequiredNumber(element, "cy", 0);
        var rx = ParseRequiredNumber(element, "rx");
        var ry = ParseRequiredNumber(element, "ry");
        if (rx <= 0 || ry <= 0) throw new InputValidationException("SVG ellipse radii must be positive.");
        return $"M{Format(cx - rx)} {Format(cy)}A{Format(rx)} {Format(ry)} 0 1 0 {Format(cx + rx)} {Format(cy)}A{Format(rx)} {Format(ry)} 0 1 0 {Format(cx - rx)} {Format(cy)}Z";
    }

    private static string RectanglePath(XElement element)
    {
        var x = ParseRequiredNumber(element, "x", 0);
        var y = ParseRequiredNumber(element, "y", 0);
        var width = ParseRequiredNumber(element, "width");
        var height = ParseRequiredNumber(element, "height");
        if (width <= 0 || height <= 0) throw new InputValidationException("SVG rectangle dimensions must be positive.");
        var rx = ParseOptionalNumber(Attribute(element, "rx"), 0);
        var ry = ParseOptionalNumber(Attribute(element, "ry"), rx);
        if (rx <= 0 && ry <= 0) return $"M{Format(x)} {Format(y)}H{Format(x + width)}V{Format(y + height)}H{Format(x)}Z";
        if (rx <= 0) rx = ry;
        if (ry <= 0) ry = rx;
        rx = Math.Min(rx, width / 2);
        ry = Math.Min(ry, height / 2);
        return $"M{Format(x + rx)} {Format(y)}H{Format(x + width - rx)}A{Format(rx)} {Format(ry)} 0 0 1 {Format(x + width)} {Format(y + ry)}V{Format(y + height - ry)}A{Format(rx)} {Format(ry)} 0 0 1 {Format(x + width - rx)} {Format(y + height)}H{Format(x + rx)}A{Format(rx)} {Format(ry)} 0 0 1 {Format(x)} {Format(y + height - ry)}V{Format(y + ry)}A{Format(rx)} {Format(ry)} 0 0 1 {Format(x + rx)} {Format(y)}Z";
    }

    private static string PointsPath(XElement element, bool close)
    {
        var values = ParseNumbers(Attribute(element, "points"));
        if (values.Length < 4 || values.Length % 2 != 0) throw new InputValidationException("SVG points geometry is invalid.");
        var builder = new StringBuilder($"M{Format(values[0])} {Format(values[1])}");
        for (var index = 2; index < values.Length; index += 2) builder.Append('L').Append(Format(values[index])).Append(' ').Append(Format(values[index + 1]));
        if (close) builder.Append('Z');
        return builder.ToString();
    }

    private static string LinePath(XElement element) => $"M{Format(ParseRequiredNumber(element, "x1", 0))} {Format(ParseRequiredNumber(element, "y1", 0))}L{Format(ParseRequiredNumber(element, "x2", 0))} {Format(ParseRequiredNumber(element, "y2", 0))}";

    private static string CombineTransforms(string inherited, string? local, ISet<string> operations)
    {
        local = CleanTransform(local);
        if (local.Length == 0) return inherited;
        if (!TransformListRegex().IsMatch(local)) throw new InputValidationException($"SVG contains unsupported transform '{local}'.");
        operations.Add("resolved-transforms");
        return inherited.Length == 0 ? local : inherited + " " + local;
    }

    private static string CleanTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        if (value.StartsWith("transform=\"", StringComparison.OrdinalIgnoreCase) && value.EndsWith('"'))
            value = value[11..^1];
        return value.Trim();
    }

    private static string? Attribute(XElement element, string localName) => element.Attributes()
        .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value;

    private static double ParseRequiredNumber(XElement element, string attribute, double? defaultValue = null)
    {
        var value = Attribute(element, attribute);
        if (value is null && defaultValue is not null) return defaultValue.Value;
        var result = ParseOptionalNumber(value, double.NaN);
        if (!double.IsFinite(result)) throw new InputValidationException($"SVG attribute '{attribute}' is not a finite number.");
        return result;
    }

    private static double ParseOptionalNumber(string? value, double defaultValue)
        => value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result)
            ? result : defaultValue;

    private static double ParseLength(string? value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        var match = LengthRegex().Match(value.Trim());
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result)
            ? result : defaultValue;
    }

    private static double ParseOpacity(string? value, double inherited)
    {
        if (value is null) return inherited;
        var parsed = ParseOptionalNumber(value, inherited);
        return Math.Clamp(parsed, 0, 1) * inherited;
    }

    private static double[] ParseNumbers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var matches = NumberRegex().Matches(value);
        var remainder = NumberRegex().Replace(value, string.Empty);
        if (remainder.Any(character => !char.IsWhiteSpace(character) && character != ',')) return [];
        return matches.Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
    }

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string FormatViewBox((double X, double Y, double Width, double Height) value)
        => $"{Format(value.X)} {Format(value.Y)} {Format(value.Width)} {Format(value.Height)}";
    private static (byte R, byte G, byte B) ParseHex(string value)
        => (byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber), byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber), byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber));

    private static bool ShouldUseLightNeutral(IReadOnlyList<string> foregroundColors)
    {
        if (foregroundColors.Count == 0) return false;
        var darkOrColored = foregroundColors.Count(color => RelativeLuminance(ParseHex(color)) < 0.72);
        // A common full-color badge contains one brand color and one white
        // detail. Treat that balanced palette as suitable for a light tile;
        // requiring a strict majority incorrectly surrounded these badges with
        // the slate last-resort fallback.
        return darkOrColored * 2 >= foregroundColors.Count;
    }

    private static double RelativeLuminance((byte R, byte G, byte B) color)
    {
        static double Linear(byte component)
        {
            var value = component / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static readonly string[] PaintProperties =
    [
        "fill", "stroke", "stroke-width", "fill-rule", "opacity", "fill-opacity", "stroke-opacity", "display", "visibility", "color"
    ];

    private static readonly IReadOnlyDictionary<string, string> NamedColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "#000000", ["white"] = "#FFFFFF", ["red"] = "#FF0000", ["blue"] = "#0000FF",
        ["green"] = "#008000", ["gray"] = "#808080", ["grey"] = "#808080", ["yellow"] = "#FFFF00",
        ["orange"] = "#FFA500", ["purple"] = "#800080", ["transparent"] = "none"
    };

    private sealed record StyleState(
        string? Fill,
        string? Stroke,
        string? Color,
        double StrokeWidth,
        string? FillRule,
        double Opacity,
        double FillOpacity,
        double StrokeOpacity,
        bool DisplayNone,
        bool VisibilityHidden)
    {
        public static StyleState Empty { get; } = new(null, null, "#000000", 1, null, 1, 1, 1, false, false);
    }

    [GeneratedRegex("^#[A-Za-z_][A-Za-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeLocalReferenceRegex();
    [GeneratedRegex("^url\\(\\s*['\"]?#([A-Za-z_][A-Za-z0-9_.:-]{0,127})['\"]?\\s*\\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalPaintRegex();
    [GeneratedRegex("^rgba?\\(\\s*([0-9.]+%?)\\s*[, ]\\s*([0-9.]+%?)\\s*[, ]\\s*([0-9.]+%?)(?:\\s*[,/]\\s*[0-9.]+%?)?\\s*\\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RgbColorRegex();
    [GeneratedRegex("^color\\(\\s*(?:display-p3|srgb)\\s+([0-9.]+)\\s+([0-9.]+)\\s+([0-9.]+)(?:\\s*/\\s*[0-9.]+)?\\s*\\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColorFunctionRegex();
    [GeneratedRegex("^hsla?\\(\\s*([+-]?[0-9.]+)(?:deg)?\\s*[, ]\\s*([0-9.]+)%\\s*[, ]\\s*([0-9.]+)%(?:\\s*[,/]\\s*[0-9.]+%?)?\\s*\\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HslColorRegex();
    [GeneratedRegex("([^{}]+)\\{([^{}]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex CssClassRuleRegex();
    [GeneratedRegex("(--[A-Za-z_][A-Za-z0-9_-]{0,127})\\s*:\\s*([^;}{]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CssVariableDeclarationRegex();
    [GeneratedRegex("var\\(\\s*(--[A-Za-z_][A-Za-z0-9_-]{0,127})\\s*\\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssVariableReferenceRegex();
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CssClassNameRegex();
    [GeneratedRegex("^([+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)(?:[eE][+-]?\\d+)?)(?:px|pt|pc|mm|cm|in)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();
    [GeneratedRegex("[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)(?:[eE][+-]?\\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
    [GeneratedRegex("^(?:(?:matrix|translate|scale|rotate|skewX|skewY)\\s*\\([^)]*\\)\\s*)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TransformListRegex();
}
