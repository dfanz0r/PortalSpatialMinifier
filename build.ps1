$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ScriptRoot "JsonMinifier.csproj"
$AppName   = "JsonMinifier"
$BuildRoot = Join-Path $ScriptRoot "bin\build"
$DistRoot  = Join-Path $ScriptRoot "bin\dist"

$Builds = @(
    @{ Rid = "win-x64";        Extra = @() },
    @{ Rid = "win-x86";        Extra = @() },
    @{ Rid = "win-arm64";      Extra = @("--self-contained", "-p:PublishAot=false", "-p:PublishSingleFile=true") },

    @{ Rid = "linux-x64";      Extra = @("--self-contained", "-p:PublishAot=false", "-p:PublishSingleFile=true") },
    @{ Rid = "linux-musl-x64"; Extra = @("--self-contained", "-p:PublishAot=false", "-p:PublishSingleFile=true") },
    @{ Rid = "linux-arm64";    Extra = @("--self-contained", "-p:PublishAot=false", "-p:PublishSingleFile=true") },
    @{ Rid = "linux-arm";      Extra = @("--self-contained", "-p:PublishAot=false", "-p:PublishSingleFile=true") },

    @{ Rid = "osx-x64";        Extra = @("--self-contained", "-p:PublishAot=false", "-p:PublishSingleFile=true") },
    @{ Rid = "osx-arm64";      Extra = @("--self-contained", "-p:PublishAot=false", "-p:PublishSingleFile=true") }
)

# Clean output
Remove-Item $BuildRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $DistRoot  -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $BuildRoot | Out-Null
New-Item -ItemType Directory -Path $DistRoot  | Out-Null

foreach ($build in $Builds) {
    $rid = $build.Rid
    $out = Join-Path $BuildRoot $rid

    Write-Host "`n=== Building $rid ===" -ForegroundColor Cyan

    $dotnetArgs = @(
        "publish",
        $ProjectFile,
        "-c", "Release",
        "-r", $rid,
        "/p:DebugType=None",
        "--output", $out
    ) + $build.Extra

    & dotnet @dotnetArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $rid"
    }

    # Locate executable
    if ($rid -like "win-*") {
        $exe = Join-Path $out "$AppName.exe"
    } else {
        $exe = Join-Path $out $AppName
    }

    if (-not (Test-Path $exe)) {
        throw "Executable not found for $rid"
    }

    # Zip executable
    $zip = Join-Path $DistRoot "$AppName-$rid.zip"
    Compress-Archive -Path $exe -DestinationPath $zip -Force

    Write-Host "Packaged $zip"
}

Write-Host "`nAll builds completed successfully." -ForegroundColor Green
