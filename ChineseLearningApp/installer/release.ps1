param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    # What changed, shown to learners when the app offers the update.
    [string]$Notes = "",
    # Publish an installer already built for this version instead of building it.
    [switch]$SkipBuild
)

# Publishes a new version for the app's built-in updater: builds the installer
# (build-installer.ps1), then makes a GitHub release "v<Version>" on the
# project's repository with the installer attached and its SHA-256 in the
# notes. Installed apps find it under Settings → Cập nhật (or on their own,
# once a day) and update themselves.
#
# Needs permission to publish on GitHub, taken from (in order): the GitHub CLI
# (gh) if signed in, the GITHUB_TOKEN environment variable (a token with
# "Contents: read and write" on the repository), or the GitHub sign-in Git
# already uses for pushing.
#
#   .\release.ps1 -Version 1.7.0 -Notes "Sửa lỗi luyện gõ; thêm 200 từ mới."

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must look like 1.7.0."
}

$projectDirectory = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $projectDirectory "artifacts\ChineseLearningApp-Setup-$Version.exe"

# The repository the app updates from (UpdateService.Repository) must be the one
# this checkout pushes to.
$appRepository = [regex]::Match((Get-Content -Raw (Join-Path $projectDirectory "UpdateService.cs")), 'Repository = "([^"]+)"').Groups[1].Value
$remote = (& git -C $projectDirectory remote get-url origin)
$remoteRepository = [regex]::Match($remote, 'github\.com[/:]([^/]+/[^/.]+)').Groups[1].Value
if ($appRepository -ne $remoteRepository) {
    throw "The app updates from '$appRepository' but this checkout pushes to '$remoteRepository'. Change UpdateService.Repository first."
}

$api = "https://api.github.com/repos/$appRepository"

# --- Permission to publish ---------------------------------------------------
function Get-GitHubToken {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        $token = (& gh auth token 2>$null)
        if ($LASTEXITCODE -eq 0 -and $token) { return $token.Trim() }
    }
    if ($env:GITHUB_TOKEN) { return $env:GITHUB_TOKEN }
    # The credential Git keeps for github.com (Git Credential Manager). The
    # request goes in from a file: PowerShell 5 mangles what it pipes to git.
    $request = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($request, "protocol=https`nhost=github.com`nusername=$($appRepository.Split('/')[0])`n`n", [Text.Encoding]::ASCII)
        $ErrorActionPreference = "Continue"
        $answer = & cmd.exe /c "git credential fill < `"$request`" 2>nul"
        $ErrorActionPreference = "Stop"
    } finally {
        Remove-Item -LiteralPath $request -ErrorAction SilentlyContinue
    }
    $line = $answer | Where-Object { $_ -like "password=*" } | Select-Object -First 1
    if ($line) { return $line.Substring(9) }
    throw "No GitHub permission found. Sign in with 'gh auth login', or set GITHUB_TOKEN."
}

$token = Get-GitHubToken
$headers = @{
    Authorization          = "Bearer $token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
    "User-Agent"           = "ChineseLearningApp-release"
}

# --- Is this version new? -------------------------------------------------------
$tag = "v$Version"
try {
    Invoke-RestMethod -Uri "$api/releases/tags/$tag" -Headers $headers | Out-Null
    throw "Release $tag already exists on GitHub. Pick a higher version."
} catch [System.Net.WebException] {
    if ($_.Exception.Response.StatusCode -ne [System.Net.HttpStatusCode]::NotFound) { throw }
}

try {
    $latest = Invoke-RestMethod -Uri "$api/releases/latest" -Headers $headers
    $latestVersion = [version]($latest.tag_name.TrimStart('v', 'V'))
    if ([version]$Version -le $latestVersion) {
        throw "Version $Version is not newer than the latest release ($($latest.tag_name)); installed apps would not update to it."
    }
} catch [System.Net.WebException] {
    if ($_.Exception.Response.StatusCode -ne [System.Net.HttpStatusCode]::NotFound) { throw }
    # No release yet: this is the first.
}

# --- Build ----------------------------------------------------------------------
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build-installer.ps1") -Version $Version
}
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer not found: $installer"
}

$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$body = if ($Notes) { "$Notes`n`nsha256: $hash" } else { "sha256: $hash" }

# --- Publish --------------------------------------------------------------------
$json = @{ tag_name = $tag; name = "Học tiếng Trung $Version"; body = $body; draft = $true } | ConvertTo-Json
$release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers `
    -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($json))

try {
    $name = Split-Path -Leaf $installer
    Write-Output "Uploading $name ($([math]::Round((Get-Item -LiteralPath $installer).Length / 1MB, 1)) MB)..."
    Invoke-RestMethod -Method Post -Uri "https://uploads.github.com/repos/$appRepository/releases/$($release.id)/assets?name=$name" `
        -Headers $headers -ContentType "application/octet-stream" -InFile $installer | Out-Null

    # Published only once the installer is there, so apps never see a release without it.
    $publish = @{ draft = $false; make_latest = "true" } | ConvertTo-Json
    $release = Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $headers `
        -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($publish))
} catch {
    Invoke-RestMethod -Method Delete -Uri "$api/releases/$($release.id)" -Headers $headers | Out-Null
    throw
}

Write-Output "Released $tag : $($release.html_url)"
Write-Output "Installed apps will offer this update (Settings -> Cập nhật, or on their own within a day)."
