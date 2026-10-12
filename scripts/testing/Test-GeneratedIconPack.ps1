[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-File {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.PSIsContainer -or $item.Length -eq 0) {
        throw "Expected a non-empty file at '$Path'."
    }

    return $item
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    Require-File -Path $Path | Out-Null
    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
}

$pack = Require-File -Path $PackPath
if ($pack.Extension -cne '.otphicons') {
    throw "Expected an .otphicons file, received '$($pack.Name)'."
}

$visualReportPath = [IO.Path]::ChangeExtension($pack.FullName, '.visual-report.json')
$rightsReportPath = [IO.Path]::ChangeExtension($pack.FullName, '.rights-report.json')
$visualReport = Read-JsonFile -Path $visualReportPath
$rightsReport = Read-JsonFile -Path $rightsReportPath

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($pack.FullName)
try {
    $manifestEntries = @($archive.Entries | Where-Object FullName -CEQ 'pack.json')
    if ($manifestEntries.Count -ne 1) {
        throw "The pack must contain exactly one pack.json entry; found $($manifestEntries.Count)."
    }

    $reader = [IO.StreamReader]::new(
        $manifestEntries[0].Open(),
        [Text.UTF8Encoding]::new($false, $true),
        $true)
    try {
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    if ($manifest.formatVersion -ne 1) {
        throw "Unsupported pack format version '$($manifest.formatVersion)'."
    }

    $brands = @($manifest.brands)
    $aliases = @($manifest.issuerAliases)
    if ($brands.Count -le 5000) {
        throw "The generated catalog must contain more than 5,000 brands; found $($brands.Count)."
    }

    if ($aliases.Count -eq 0) {
        throw 'The generated catalog must contain issuer aliases.'
    }

    $expectedProviders = @('aegis', 'dashboard-icons', 'simple-icons')
    $actualProviders = @($manifest.sources.provider | Sort-Object -CaseSensitive)
    $providerDifference = @(Compare-Object $expectedProviders $actualProviders -CaseSensitive)
    if ($providerDifference.Count -ne 0) {
        throw "Unexpected source providers: $($actualProviders -join ', ')."
    }

    $entryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $archive.Entries) {
        if (-not $entryNames.Add($entry.FullName)) {
            throw "Duplicate archive entry '$($entry.FullName)'."
        }
    }

    foreach ($brand in $brands) {
        if (([string]::IsNullOrWhiteSpace($brand.id)) -or
            ([string]::IsNullOrWhiteSpace($brand.icon)) -or
            (-not $entryNames.Contains([string] $brand.icon))) {
            throw "Brand '$($brand.id)' does not reference an existing icon entry."
        }
    }
}
finally {
    $archive.Dispose()
}

if (($visualReport.formatVersion -ne 1) -or
    ($visualReport.unresolvedFailures -ne 0) -or
    ($visualReport.totalBrands -ne $brands.Count) -or
    (@($visualReport.brands).Count -ne $brands.Count)) {
    throw 'The visual verification report does not validate the complete generated catalog.'
}

if (($rightsReport.formatVersion -ne 1) -or
    ($rightsReport.notLegalClearance -ne $true) -or
    ($rightsReport.selectedBrands -ne $brands.Count) -or
    (@($rightsReport.brands).Count -ne $brands.Count)) {
    throw 'The rights assessment report does not cover the complete generated catalog.'
}

Write-Host (
    "Validated '{0}': {1:N0} brands, {2:N0} issuer aliases, {3:N0} bytes." -f `
        $pack.FullName,
        $brands.Count,
        $aliases.Count,
        $pack.Length)
