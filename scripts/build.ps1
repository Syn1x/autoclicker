param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0',
    [string]$BuildRoot = (Join-Path $PSScriptRoot '..\artifacts'),
    [switch]$Package,
    [string]$SigningParameters = $env:AUTOCLICKER_SIGN_PARAMS
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
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Packaging tool restore failed.' }
        $release = Join-Path $BuildRoot "releases-$Version"
        $packArguments = @('pack','--packId','Syn1x.Autoclicker','--packTitle','Autoclicker',
            '--packAuthors','Syn1x','--packVersion',$Version,'--packDir',$publish,
            '--mainExe','Autoclicker.exe','--framework','net48','--runtime','win-x64',
            '--outputDir',$release,'--delta','None')
        if ($SigningParameters) { $packArguments += @('--signParams', $SigningParameters) }
        & dotnet tool run vpk @packArguments
        if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
        Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Extension -in '.exe','.zip','.nupkg','.json' } |
            ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
            Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
        Write-Output "Release files: $release"
    }
} finally { Pop-Location }
