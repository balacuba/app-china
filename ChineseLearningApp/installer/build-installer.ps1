param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$projectDirectory = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectDirectory "ChineseLearningApp.csproj"
$artifactsDirectory = Join-Path $projectDirectory "artifacts"
$publishDirectory = Join-Path $artifactsDirectory "publish"
$installerScript = Join-Path $PSScriptRoot "ChineseLearningApp.iss"

$compiler = (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
if (-not $compiler) {
    $compilerCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe")
    )
    foreach ($candidate in $compilerCandidates) {
        if (Test-Path -LiteralPath $candidate) {
            $compiler = $candidate
            break
        }
    }
}

if (-not $compiler) {
    throw "Inno Setup 6 or 7 is required. Install it from https://jrsoftware.org/isinfo.php and run this script again."
}

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
& dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    "-p:Version=$Version" `
    "-p:PublishDir=$publishDirectory\"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

& $compiler "/DMyAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Output "Installer created: $(Join-Path $artifactsDirectory "ChineseLearningApp-Setup-$Version.exe")"
