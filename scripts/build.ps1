param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.1.0',
    [string]$BuildRoot = (Join-Path $PSScriptRoot '..\artifacts'),
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
New-Item -ItemType Directory -Path $BuildRoot -Force | Out-Null
Push-Location $repoRoot
try {
    $project = Join-Path $repoRoot 'src\Autoclicker\Autoclicker.csproj'
    $tests = Join-Path $repoRoot 'tests\Autoclicker.Tests.csproj'
    $properties = @("-p:BuildRoot=$BuildRoot", "-p:Version=$Version")
    & dotnet restore $tests --locked-mode @properties
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & dotnet build $tests -c Release --no-restore @properties
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $testExe = Join-Path $BuildRoot 'bin\Autoclicker.Tests\Release\net48\Autoclicker.Tests.exe'
    & $testExe (Join-Path $BuildRoot 'test-results')
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed. See test-results.' }
    $publish = Join-Path $BuildRoot "publish-$Version"
    & dotnet publish $project -c Release --no-restore -o $publish @properties
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($Package) {
        $release = Join-Path $BuildRoot "releases-$Version"
        New-Item -ItemType Directory -Path $release -Force | Out-Null
        $exe = Join-Path $release 'autoclicker.exe'
        Copy-Item -LiteralPath (Join-Path $publish 'Autoclicker.exe') -Destination $exe
        $manifest = [ordered]@{
            version = $Version
            sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
            size = (Get-Item -LiteralPath $exe).Length
        }
        $manifest | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $release 'autoclicker-update.json') -Encoding ascii
        Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Extension -in '.exe','.json' } |
            ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
            Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
        Write-Output "Standalone release: $exe"
    }
} finally { Pop-Location }