# Cozy Orbit (Windows) — 배포본 만들기
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1            (테스트 → 빌드 → 압축)
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -SkipTests
#
# 결과 (artifacts\, .gitignore 에 있음):
#   CozyOrbit-<버전>-win-x64.zip   단일 exe(CozyOrbit.exe), .NET 런타임 포함 (설치할 것 없음)
# 자동 업데이트는 zip 안의 exe 하나를 찾는다 (UpdateInstaller) — zip 에 다른 exe 를 넣지 않는다.
# 버전은 src\CozyOrbit.App\CozyOrbit.App.csproj 의 <Version> 하나 (릴리스 태그 release/v<버전> 과 맞출 것).

param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csproj = 'src\CozyOrbit.App\CozyOrbit.App.csproj'
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "$csproj 에서 <Version> 을 찾지 못했어요" }
$rid = 'win-x64'
$exeName = 'CozyOrbit.exe'
Write-Host "Cozy Orbit (Windows) $version"

if (-not $SkipTests) {
    dotnet test tests\CozyOrbit.Core.Tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw '테스트 실패 — 배포본을 만들지 않아요' }
}

$out = Join-Path $root 'artifacts'
$dir = Join-Path $out "publish-$rid"
$stage = Join-Path $out "stage-$rid"
$zip = Join-Path $out "CozyOrbit-$version-$rid.zip"
foreach ($p in @($dir, $stage, $zip)) { if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
New-Item -ItemType Directory $out -Force | Out-Null

$pubArgs = @('publish', $csproj, '-c', 'Release', '-r', $rid, '--nologo', '-o', $dir,
    '--self-contained=true',
    '-p:PublishSingleFile=true',
    # Skia·HarfBuzz·ANGLE(Avalonia)·SQLite(e_sqlite3.dll) 의 네이티브 DLL 까지 exe 하나에 넣는다 (실행 시 임시 폴더로 풀림)
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none', '-p:GenerateDocumentationFile=false',
    # 자동 업데이트: 이 exe 가 어느 배포판인지 (매니페스트의 키). 없으면 개발 빌드로 보고 설치하지 않는다
    "-p:CozyUpdateFlavor=$rid")
dotnet @pubArgs
if ($LASTEXITCODE -ne 0) { throw "publish 실패: $rid" }

$extra = Get-ChildItem $dir -File | Where-Object { $_.Name -ne $exeName }
# 남는 파일이 있으면 단일 파일이 아니게 됐다는 뜻 — 자동 업데이트는 exe 하나만 바꾸므로 멈춘다
if ($extra) { throw ("단일 파일 밖에 남은 파일: " + (($extra | ForEach-Object Name) -join ', ')) }

New-Item -ItemType Directory $stage | Out-Null
Copy-Item (Join-Path $dir $exeName) $stage
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force

$exe = Get-Item (Join-Path $dir $exeName)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Write-Host ("{0}: exe {1:N1} MB, zip {2:N1} MB" -f $rid, ($exe.Length / 1MB), ((Get-Item $zip).Length / 1MB))
Write-Host "$hash  $(Split-Path -Leaf $zip)"
Write-Host "완료: $zip"
