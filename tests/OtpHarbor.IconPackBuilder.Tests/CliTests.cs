using OtpHarbor.IconPackBuilder.Acquisition;
using OtpHarbor.IconPackBuilder.Commands;
using OtpHarbor.IconPackBuilder.Packaging;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class CliTests
{
    [Fact]
    public async Task NoArgumentInvocationStartsAutomaticBuildAndUsesDefaultOutput()
    {
        using var fixture = new CliFixture();
        var outputPath = fixture.FilePath("default.otphicons");

        var result = await RunAsync([], fixture, defaultOutputPath: () => outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, fixture.Acquirer.CallCount);
        Assert.Empty(fixture.Acquirer.LastOverrides!);
        Assert.True(File.Exists(outputPath));
        var visualReportPath = Path.ChangeExtension(outputPath, ".visual-report.json");
        Assert.True(File.Exists(visualReportPath));
        using (var report = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(visualReportPath)))
        {
            Assert.Equal(1, report.RootElement.GetProperty("totalBrands").GetInt32());
            Assert.Equal(0, report.RootElement.GetProperty("unresolvedFailures").GetInt32());
            var brand = report.RootElement.GetProperty("brands")[0];
            Assert.True(brand.GetProperty("resolved").GetBoolean());
            Assert.Equal(40 * 40, brand.GetProperty("totalPixels").GetInt32());
        }
        Assert.DoesNotContain("Usage:", result.Stdout);
    }

    [Fact]
    public async Task BuildWithoutProviderOptionsUsesAutomaticAcquisition()
    {
        using var fixture = new CliFixture();
        var outputPath = fixture.FilePath("automatic.otphicons");

        var result = await RunAsync(["build", "--output", outputPath, "--non-interactive"], fixture);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(fixture.Acquirer.LastOverrides!);
        Assert.DoesNotContain("━", result.Stdout);
        Assert.DoesNotContain("100.0%", result.Stdout);
        var pack = await PackReader.ReadAndValidateAsync(outputPath);
        Assert.Equal(["aegis", "dashboard-icons", "simple-icons"], pack.Document.Sources.Select(x => x.Provider));
        Assert.Equal("github", Assert.Single(pack.Document.Brands).Id);
        Assert.Equal("simple-icons", Assert.Single(pack.Document.Brands).SelectedSource.Provider);
    }

    [Fact]
    public async Task InteractiveEnterAcceptsProposedDefaultPath()
    {
        using var fixture = new CliFixture();
        var outputPath = fixture.FilePath("accepted.otphicons");

        var result = await RunAsync([], fixture, input: new StringReader(Environment.NewLine), interactive: true,
            defaultOutputPath: () => outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("sources pinned to this builder release", result.Stdout);
        Assert.Contains("Use this location? [Y/n]", result.Stdout);
        Assert.Contains("100.0%", result.Stdout);
        Assert.Contains("+ [====================]", result.Stdout);
    }

    [Fact]
    public async Task InteractiveCustomDirectoryAppendsDefaultFileName()
    {
        using var fixture = new CliFixture();
        var directory = fixture.FilePath("custom-directory");
        Directory.CreateDirectory(directory);
        var responses = new StringReader($"n{Environment.NewLine}{directory}{Environment.NewLine}");

        var result = await RunAsync([], fixture, input: responses, interactive: true,
            defaultOutputPath: () => fixture.FilePath("unused.otphicons"));

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(directory, "otp-harbor-icons.otphicons")));
    }

    [Fact]
    public async Task InteractiveCustomFilePathNormalizesExtension()
    {
        using var fixture = new CliFixture();
        var requested = fixture.FilePath("named.zip");
        var expected = fixture.FilePath("named.otphicons");
        var responses = new StringReader($"no{Environment.NewLine}{requested}{Environment.NewLine}");

        var result = await RunAsync([], fixture, input: responses, interactive: true,
            defaultOutputPath: () => fixture.FilePath("unused.otphicons"));

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(expected));
        Assert.False(File.Exists(requested));
    }

    [Fact]
    public async Task InteractiveEnterConfirmsReplacingExistingOutput()
    {
        using var fixture = new CliFixture();
        var outputPath = fixture.FilePath("existing.otphicons");
        var original = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(outputPath, original);

        var result = await RunAsync(["build", "--output", outputPath], fixture,
            input: new StringReader(Environment.NewLine), interactive: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, fixture.Acquirer.CallCount);
        Assert.NotEqual(original, await File.ReadAllBytesAsync(outputPath));
        await PackReader.ReadAndValidateAsync(outputPath);
        Assert.Contains("Replace it? [Y/n]", result.Stdout);
    }

    [Fact]
    public async Task NonInteractiveExistingOutputRequiresForce()
    {
        using var fixture = new CliFixture();
        var outputPath = fixture.FilePath("existing.otphicons");
        await File.WriteAllTextAsync(outputPath, "old");

        var refused = await RunAsync(["build", "--output", outputPath, "--non-interactive"], fixture);
        var replaced = await RunAsync(["build", "--output", outputPath, "--non-interactive", "--force"], fixture);

        Assert.Equal(3, refused.ExitCode);
        Assert.Contains("Use --force", refused.Stderr);
        Assert.Equal(0, replaced.ExitCode);
        await PackReader.ReadAndValidateAsync(outputPath);
    }

    [Fact]
    public async Task OfflineAndLocalOverrideOptionsReachAcquisition()
    {
        using var fixture = new CliFixture();
        var outputPath = fixture.FilePath("offline.otphicons");

        var result = await RunAsync([
            "build", "--offline", "--aEgIs", fixture.Aegis.Path, "--output", outputPath, "--non-interactive"], fixture);

        Assert.Equal(0, result.ExitCode);
        Assert.True(fixture.Acquirer.LastOptions!.Offline);
        Assert.Equal(fixture.Aegis.Path, fixture.Acquirer.LastOverrides!["aegis"]);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("aegis,AEGIS")]
    public async Task ProviderPreferenceRejectsUnknownAndDuplicateIds(string preference)
    {
        using var fixture = new CliFixture();

        var result = await RunAsync([
            "build", "--provider-preference", preference, "--output", fixture.FilePath("output.otphicons"), "--non-interactive"], fixture);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("provider", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Acquirer.CallCount);
    }

    [Fact]
    public async Task SuccessfulBuildRemovesStaleConflictReport()
    {
        using var fixture = new CliFixture();
        var outputPath = fixture.FilePath("output.otphicons");
        var conflictPath = Path.ChangeExtension(outputPath, ".conflicts.json");
        await File.WriteAllTextAsync(conflictPath, "stale");

        var result = await RunAsync(["build", "--output", outputPath, "--non-interactive"], fixture);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(conflictPath));
    }

    [Fact]
    public async Task FailedReplacementLeavesExistingOutputUntouched()
    {
        using var fixture = new CliFixture();
        using var invalid = new TestArchive(new Dictionary<string, string> { ["not-pack.txt"] = "invalid" });
        var outputPath = fixture.FilePath("preserved.otphicons");
        var original = new byte[] { 7, 8, 9 };
        await File.WriteAllBytesAsync(outputPath, original);
        fixture.Acquirer.Inputs = fixture.Acquirer.Inputs
            .Select(x => x.Provider == "aegis" ? x with { ArchivePath = invalid.Path } : x)
            .ToArray();

        var result = await RunAsync([
            "build", "--output", outputPath, "--non-interactive", "--force"], fixture);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(outputPath));
    }

    [Fact]
    public async Task CancellationReturnsDedicatedExitCode()
    {
        using var fixture = new CliFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await RunAsync(["build", "--output", fixture.FilePath("cancelled.otphicons"), "--non-interactive"],
            fixture, cancellationToken: cancellation.Token);

        Assert.Equal(130, result.ExitCode);
        Assert.Contains("cancelled", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulInteractiveBuildEmphasizesFinalStatusInGreen()
    {
        using var fixture = new CliFixture();
        var outputPath = fixture.FilePath("colored-success.otphicons");

        var result = await RunAsync(["build", "--output", outputPath], fixture,
            interactive: true, useColor: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\u001b[32m[+] Icon pack created successfully.\u001b[0m", result.Stdout);
        Assert.Contains($"\u001b[1;92m[OUTPUT] {Path.GetFullPath(outputPath)}\u001b[0m", result.Stdout);
        Assert.Contains("Third-party icons, names, and trademarks", result.Stdout);
        Assert.Contains("License, attribution, and provenance files are included", result.Stdout);
        Assert.Contains("#legal-and-distribution-notice", result.Stdout);
    }

    [Fact]
    public async Task BuildFailureEmphasizesFinalStatusInRed()
    {
        using var fixture = new CliFixture();

        var result = await RunAsync(["build", "--unknown"], fixture, useColor: true);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("\u001b[31m[X] Build failed:", result.Stderr);
        Assert.Contains("\u001b[0m", result.Stderr);
    }

    [Fact]
    public void OutputDirectorySelectionFallsBackDeterministically()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var existing = fixture.FilePath("exists");
        Directory.CreateDirectory(existing);

        Assert.Equal(existing, PlatformPaths.SelectFirstExistingDirectory([fixture.FilePath("missing"), existing], fixture.DirectoryPath));
        Assert.Equal(fixture.DirectoryPath,
            PlatformPaths.SelectFirstExistingDirectory([fixture.FilePath("missing")], fixture.DirectoryPath));
    }

    private static async Task<CliResult> RunAsync(
        string[] args,
        CliFixture fixture,
        TextReader? input = null,
        bool? interactive = false,
        Func<string>? defaultOutputPath = null,
        CancellationToken cancellationToken = default,
        bool useColor = false)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = await CliApplication.RunAsync(args, stdout, stderr, cancellationToken,
            fixture.Acquirer, input, interactive, defaultOutputPath,
            () => fixture.FilePath("cache"), useColor);
        return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    private sealed class CliFixture : IDisposable
    {
        public CliFixture()
        {
            Aegis = new TestArchive(new Dictionary<string, string>
            {
                ["fixture/pack.json"] = "{\"uuid\":\"c553f06f-2a17-46ca-87f5-56af90dd0500\",\"name\":\"Fixture\",\"version\":7,\"icons\":[{\"name\":\"GitHub\",\"filename\":\"icons/1_Primary/GitHub.svg\",\"issuer\":[\"github.com\"]}]}",
                ["fixture/icons/1_Primary/GitHub.svg"] = TestData.Svg
            });
            SimpleIcons = new TestArchive(new Dictionary<string, string>
            {
                ["simple-icons-1.2.3/package.json"] = "{\"name\":\"simple-icons\",\"version\":\"1.2.3\"}",
                ["simple-icons-1.2.3/data/simple-icons.json"] = "[{\"title\":\"GitHub\",\"slug\":\"github\",\"hex\":\"181717\"}]",
                ["simple-icons-1.2.3/icons/github.svg"] = TestData.Svg
            });
            Dashboard = new TestArchive(new Dictionary<string, string>
            {
                ["metadata.json"] = "{\"github\":{\"base\":\"svg\",\"aliases\":[\"GitHub\"]}}",
                ["svg/github.svg"] = TestData.Svg
            });
            Acquirer = new FakeAcquirer
            {
                Inputs =
                [
                    new IconSourceInput("aegis", Aegis.Path, Version: "7", SourceUrl: "https://example.invalid/aegis/7"),
                    new IconSourceInput("simple-icons", SimpleIcons.Path, Version: "1.2.3", SourceUrl: "https://example.invalid/simple-icons/1.2.3"),
                    new IconSourceInput("dashboard-icons", Dashboard.Path, Revision: new string('a', 40), SourceUrl: "https://example.invalid/dashboard/commit")
                ]
            };
        }

        public TestArchive Aegis { get; }
        public TestArchive SimpleIcons { get; }
        public TestArchive Dashboard { get; }
        public FakeAcquirer Acquirer { get; }

        public string FilePath(string name) => Aegis.FilePath(name);

        public void Dispose()
        {
            Dashboard.Dispose();
            SimpleIcons.Dispose();
            Aegis.Dispose();
        }
    }

    private sealed class FakeAcquirer : ISourceAcquirer
    {
        public IReadOnlyList<IconSourceInput> Inputs { get; set; } = [];
        public int CallCount { get; private set; }
        public IReadOnlyDictionary<string, string>? LastOverrides { get; private set; }
        public SourceAcquisitionOptions? LastOptions { get; private set; }

        public Task<IReadOnlyList<IconSourceInput>> AcquireAsync(
            IReadOnlyDictionary<string, string> localOverrides,
            SourceAcquisitionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastOverrides = localOverrides;
            LastOptions = options;
            IReadOnlyList<IconSourceInput> result = Inputs.Select(source =>
                localOverrides.TryGetValue(source.Provider, out var path)
                    ? new IconSourceInput(source.Provider, path)
                    : source).ToArray();
            return Task.FromResult(result);
        }
    }
}
