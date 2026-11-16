# Builds native\runtime\win-x64\stockpilot_native.dll (Windows, 64-bit).
# Needs ONE 64-bit C compiler: MinGW-w64 gcc (check: gcc -dumpmachine -> x86_64-...) or MSVC cl (Developer PowerShell for VS).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $root "runtime\win-x64"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dll  = Join-Path $out "stockpilot_native.dll"

if (Get-Command gcc -ErrorAction SilentlyContinue) {
    Write-Host "Compiler: gcc ($(gcc -dumpmachine))"
    gcc -shared -O2 -Wall -Wextra -I"$root\include" -o $dll "$root\src\fees.c"
    if ($LASTEXITCODE -ne 0) { throw "gcc failed" }
}
elseif (Get-Command cl -ErrorAction SilentlyContinue) {
    Write-Host "Compiler: MSVC cl"
    cl /nologo /LD /O2 /I"$root\include" "$root\src\fees.c" /Fe:"$dll" /Fo"$env:TEMP\"
    if ($LASTEXITCODE -ne 0) { throw "cl failed" }
}
else {
    throw "No C compiler found. Install MinGW-w64 (e.g. winget install BrechtSanders.WinLibs.POSIX.UCRT) or Visual Studio Build Tools (C++ workload)."
}
Write-Host "Built $dll"
