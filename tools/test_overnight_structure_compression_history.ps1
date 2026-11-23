# Run standalone C# rollover-history regression tests without NinjaTrader.
# Requires Windows/.NET Framework compiler, which is present on NT8 hosts.
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "The .NET Framework csc.exe compiler is not installed at $csc."
}

$source = Join-Path $root "src\Strategies\NinjexOvernightStructurePortfolio\CompressionHistory.cs"
$tests = Join-Path $root "tests\OvernightStructureCompressionHistory.Tests.cs"
$exe = Join-Path $env:TEMP "OvernightStructureCompressionHistory.Tests.exe"

try {
    & $csc /nologo /target:exe "/out:$exe" $source $tests
    if ($LASTEXITCODE -ne 0) {
        throw "CompressionHistory regression test compilation failed."
    }

    & $exe
    if ($LASTEXITCODE -ne 0) {
        throw "CompressionHistory regression tests failed."
    }
}
finally {
    Remove-Item $exe -ErrorAction SilentlyContinue
}
