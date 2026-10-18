# Builds the stand-alone Windows package (docs/08 §1a): one folder with HAK.exe (API, self-contained .NET), wwwroot (web UI)
# and the start scripts. Usage (from the repository root, PowerShell):  .\tools\package\package.ps1 [-Output <folder>]
param([string]$Output = "artifacts\HAK")

$ErrorActionPreference = "Continue"   # native tools write progress to stderr; failures are checked by exit code
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$out = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
if (Test-Path $out) { throw "Thư mục đích '$out' đã tồn tại — xóa hoặc chọn thư mục khác (-Output)." }

Write-Host "1/4 Build giao diện web"
if (-not (Test-Path (Join-Path $root "web\node_modules"))) {
    npm --prefix (Join-Path $root "web") ci
    if ($LASTEXITCODE) { throw "npm ci lỗi" }
}
npm --prefix (Join-Path $root "web") run build
if ($LASTEXITCODE) { throw "npm run build lỗi" }

Write-Host "2/4 Publish API (win-x64, tự chứa .NET, một tệp)"
& $dotnet publish (Join-Path $root "src\Hak.Api") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE) { throw "dotnet publish lỗi" }
Rename-Item (Join-Path $out "Hak.Api.exe") "HAK.exe"

Write-Host "3/4 Chép giao diện và tệp khởi động"
Copy-Item (Join-Path $root "web\dist") (Join-Path $out "wwwroot") -Recurse
Get-ChildItem $PSScriptRoot -Filter *.cmd | Copy-Item -Destination $out
Copy-Item (Join-Path $PSScriptRoot "DOC-TRUOC.txt") $out
Get-ChildItem $out -Filter *.pdb | Remove-Item

Write-Host "4/4 Mã băm SHA-256"
Get-ChildItem $out -File -Recurse | Where-Object Name -ne "SHA256SUMS.txt" | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($out.Length + 1)
} | Set-Content (Join-Path $out "SHA256SUMS.txt") -Encoding utf8
Write-Host "Xong: $out"
