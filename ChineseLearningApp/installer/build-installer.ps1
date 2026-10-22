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

$serverProject = Join-Path (Split-Path -Parent $projectDirectory) "ChineseLearningServer\ChineseLearningServer.csproj"
$serverPublishDirectory = Join-Path $artifactsDirectory "publish-server"

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
# Start from empty publish folders so files from older builds never end up in
# the installer.
foreach ($folder in @($publishDirectory, $serverPublishDirectory)) {
    if (Test-Path -LiteralPath $folder) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}

# Both the desktop app and the study server (website) are self-contained: the
# .NET runtime ships inside each, so they run on a PC that has never had .NET.
foreach ($publish in @(@($projectFile, $publishDirectory), @($serverProject, $serverPublishDirectory))) {
    & dotnet publish $publish[0] `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        "-p:Version=$Version" `
        "-p:PublishDir=$($publish[1])\"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish of $($publish[0]) failed with exit code $LASTEXITCODE."
    }
}

& $compiler "/DMyAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Output "Installer created: $(Join-Path $artifactsDirectory "ChineseLearningApp-Setup-$Version.exe")"
