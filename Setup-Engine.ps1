$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$tools = Join-Path $root 'Tools'
$pythonDir = Join-Path $tools 'python'
$log = Join-Path $root 'engine-setup-error.log'
Remove-Item $log -Force -ErrorAction SilentlyContinue
try {
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    $pythonZip = Join-Path $env:TEMP 'psam-python-embed.zip'
    $wheel = Join-Path $env:TEMP 'mymcplusplus-3.2.0-py3-none-any.whl'
    $pythonUrl = 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip'
    $wheelUrl = 'https://files.pythonhosted.org/packages/a5/1b/9d776d2ca98974a5f8425276305560b0285927e280312b031339e38e7d12/mymcplusplus-3.2.0-py3-none-any.whl'

    Write-Host 'Downloading private Python runtime...'
    Invoke-WebRequest -UseBasicParsing -Uri $pythonUrl -OutFile $pythonZip
    if (Test-Path $pythonDir) { Remove-Item $pythonDir -Recurse -Force }
    New-Item -ItemType Directory -Path $pythonDir | Out-Null
    Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonDir -Force

    Write-Host 'Downloading myMC++ 3.2.0 engine...'
    Invoke-WebRequest -UseBasicParsing -Uri $wheelUrl -OutFile $wheel
    $site = Join-Path $pythonDir 'Lib\site-packages'
    New-Item -ItemType Directory -Path $site -Force | Out-Null
    Copy-Item $wheel (Join-Path $env:TEMP 'mymcplusplus-wheel.zip') -Force
    Expand-Archive -LiteralPath (Join-Path $env:TEMP 'mymcplusplus-wheel.zip') -DestinationPath $site -Force

    $pth = Get-ChildItem $pythonDir -Filter 'python*._pth' | Select-Object -First 1
    if (-not $pth) { throw 'Python path configuration file was not found.' }
    @(
      'python312.zip'
      '.'
      'Lib\site-packages'
      'import site'
    ) | Set-Content -LiteralPath $pth.FullName -Encoding ASCII

    $runner = @'
import sys
from mymcplusplus.mymc import main
if __name__ == '__main__':
    main()
'@
    Set-Content -LiteralPath (Join-Path $tools 'mymcplusplus_runner.py') -Value $runner -Encoding ASCII

    & (Join-Path $pythonDir 'python.exe') (Join-Path $tools 'mymcplusplus_runner.py') --version
    if ($LASTEXITCODE -ne 0) { throw 'The myMC++ engine self-test failed.' }

    @'
myMC++ 3.2.0 is licensed under GPLv3.
Project source: https://github.com/Adubbz/mymcplusplus
Package source used by this application: https://pypi.org/project/mymcplusplus/3.2.0/
Python is licensed under the Python Software Foundation License.
'@ | Set-Content -LiteralPath (Join-Path $tools 'THIRD-PARTY-LICENSES.txt') -Encoding UTF8

    Remove-Item $pythonZip,$wheel,(Join-Path $env:TEMP 'mymcplusplus-wheel.zip') -Force -ErrorAction SilentlyContinue
    Write-Host 'Engine setup complete.'
    exit 0
} catch {
    (($_ | Out-String) + [Environment]::NewLine + $_.ScriptStackTrace) | Set-Content $log -Encoding UTF8
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
