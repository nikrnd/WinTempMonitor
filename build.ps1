# Compila WinTemp e crea l'installer in .\dist
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:PATH = "C:\Program Files\dotnet;$env:PATH"

# Driver PawnIO (incluso nell'installer, non versionato in git): scaricato e verificato via SHA256
$pawnUrl  = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe'
$pawnHash = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
$pawnFile = "$root\installer\redist\PawnIO_setup.exe"
if (-not (Test-Path $pawnFile) -or (Get-FileHash $pawnFile).Hash -ne $pawnHash) {
    New-Item -ItemType Directory -Force (Split-Path $pawnFile) | Out-Null
    Invoke-WebRequest $pawnUrl -OutFile $pawnFile
    if ((Get-FileHash $pawnFile).Hash -ne $pawnHash) {
        Remove-Item $pawnFile
        throw "Hash di PawnIO_setup.exe non valido"
    }
}

dotnet publish "$root\src\WinTemp\WinTemp.csproj" -c Release -o "$root\src\WinTemp\bin\publish"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish non riuscito" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 non trovato (winget install JRSoftware.InnoSetup)" }

& $iscc "$root\installer\WinTemp.iss"
if ($LASTEXITCODE -ne 0) { throw "Compilazione installer non riuscita" }
Write-Host "Installer creato in $root\dist"
