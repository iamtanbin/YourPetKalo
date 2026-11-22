# Builds CatPet.exe with the C# compiler that ships with Windows (no installs needed).
# The meow sounds in sounds\ are built into the exe. To change them, run tools\make-sounds.ps1 first.
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { Write-Error "C# compiler not found at $csc (needs .NET Framework 4, built into Windows 10/11)."; exit 1 }
Set-Location $PSScriptRoot
$src = Get-ChildItem src\*.cs | ForEach-Object { $_.FullName }
$res = @()
foreach ($n in "meow", "sad", "mrrp") {
    if (Test-Path "sounds\$n.wav") { $res += "/resource:sounds\$n.wav,$n.wav" }
}
& $csc /nologo /optimize+ /codepage:65001 /target:winexe /out:CatPet.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll @res $src
if ($LASTEXITCODE -eq 0) { Write-Host "Built CatPet.exe" }
exit $LASTEXITCODE
