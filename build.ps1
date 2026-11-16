$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw "C# compiler not found: $compiler" }
& $compiler /nologo /target:winexe /optimize+ /out:LightboxClicker.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /resource:main-mascot.png,LightboxDesktopClicker.main-mascot.png /resource:settings-mascot.png,LightboxDesktopClicker.settings-mascot.png LightboxClicker.cs
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
Write-Host 'Built LightboxClicker.exe'
