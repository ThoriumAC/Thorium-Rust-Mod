param (
    [string]$dotnet = "4.8",
    [string]$platform = "windows",
    [string]$steam_appid = "0",
    [string]$steam_branch = "public",
    [string]$steam_depot = "",
    [string]$steam_access = "anonymous",
    [string]$deps_dir = $null
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Check PowerShell version
$ps_version = $PSVersionTable.PSVersion.Major
if ($ps_version -le 5) {
    Write-Host "Error: PowerShell version 6 or higher required to continue, $ps_version currently installed"
    if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode) }
    exit 1
}

# Format project name and set depot ID if provided
if ($steam_depot) { $steam_depot = "-depot $steam_depot" }

# Set directory/file variables and create directories
$root_dir = $PSScriptRoot
$temp_dir = Join-Path $root_dir "../temp"
$tools_dir = Join-Path $temp_dir "tools"
if ($null -eq $deps_dir) {
    $deps_dir = Join-Path $temp_dir "raw-deps"
}
else {
    $deps_dir = Join-Path $PSScriptRoot $deps_dir
}
$platform_dir = Join-Path $deps_dir $platform
if (!(Test-Path $temp_dir)) {
    New-Item "$temp_dir" -ItemType Directory -Force | Out-Null
}
if (!(Test-Path $tools_dir)) {
    New-Item "$tools_dir" -ItemType Directory -Force | Out-Null
}

# Set URLs of dependencies and tools to download
# $steam_depotdl_url = "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_2.4.7/depotdownloader-2.4.7.zip"
$steam_depotdl_url = "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_3.4.0/DepotDownloader-windows-x64.zip"

function Get-Downloader {
    # Expected location of the EXE instead of DLL
    $steam_depotdl_exe = Join-Path $tools_dir "DepotDownloader.exe"
    $steam_depotdl_zip = Join-Path $tools_dir "DepotDownloader.zip"

    # Check if DepotDownloader.exe exists or is outdated
    if (!(Test-Path $steam_depotdl_exe) -or (Get-Item $steam_depotdl_exe).LastWriteTime -lt (Get-Date).AddDays(-7)) {
        Write-Host "Downloading latest version of DepotDownloader"

        try {
            Invoke-WebRequest $steam_depotdl_url -OutFile $steam_depotdl_zip -UseBasicParsing
        }
        catch {
            Write-Host "Error: Could not download DepotDownloader"
            Write-Host $_.Exception | Format-List -Force
            exit 1
        }

        Write-Host "Extracting DepotDownloader release files"
        Expand-Archive $steam_depotdl_zip -DestinationPath $tools_dir -Force

        # Set last write time on the EXE
        (Get-Item $steam_depotdl_exe).LastWriteTime = (Get-Date)

        Remove-Item $steam_depotdl_zip -Force
    }
    else {
        Write-Host "Recent version of DepotDownloader already downloaded"
    }

    # Expose the EXE globally
    $global:steam_depotdl_exe = $steam_depotdl_exe

    Get-Dependencies
}


function Get-Dependencies {
    # Write file list
    $fileListPath = Join-Path $tools_dir "filelist"
    Set-Content -Path $fileListPath -Value "regex:RustDedicated_Data/Managed/.+\.dll"

    # Build EXE argument list
    $args = @(
        $steam_access
        "-app", $steam_appid
        "-branch", $steam_branch
    )

    if ($steam_depot) {
        $args += "-depot $steam_depot"
    }

    $args += @(
        "-os", $platform
        "-dir", "`"$platform_dir`""
        "-filelist", "`"$fileListPath`""
    )

    Write-Host "$steam_depotdl_exe $($args -join ' ')"

    try {
        Start-Process $steam_depotdl_exe -WorkingDirectory $tools_dir -ArgumentList $args -NoNewWindow -Wait
    }
    catch {
        Write-Host "Error: Could not run DepotDownloader.exe"
        Write-Host $_.Exception | Format-List -Force
        exit 1
    }
}


Get-Downloader