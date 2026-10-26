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

$serverDirectory = Join-Path (Split-Path -Parent $projectDirectory) "ChineseLearningServer"

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
# Start from an empty publish folder so files from older builds never end up in
# the installer.
foreach ($folder in @($publishDirectory, (Join-Path $artifactsDirectory "publish-server"))) {
    if (Test-Path -LiteralPath $folder) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}

# Self-contained: the .NET runtime (with ASP.NET Core for the study website the
# app runs) ships inside, so it runs on a PC that has never had .NET.
& dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    "-p:Version=$Version" `
    "-p:PublishDir=$publishDirectory\"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# The local website settings (the Google sign-in client, kept out of git) ship
# with the app, so Google sign-in works on the PC it is installed on; so does
# the guide to opening the website to other places.
$serverSettings = Join-Path $serverDirectory "settings.json"
if (Test-Path -LiteralPath $serverSettings) {
    Copy-Item -LiteralPath $serverSettings -Destination (Join-Path $publishDirectory "settings.json")
}
Copy-Item -LiteralPath (Join-Path $projectDirectory "HUONG-DAN-TRUY-CAP-TU-XA.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $serverDirectory "HUONG-DAN-DANG-NHAP-GOOGLE.md") -Destination $publishDirectory

& $compiler "/DMyAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Output "Installer created: $(Join-Path $artifactsDirectory "ChineseLearningApp-Setup-$Version.exe")"
