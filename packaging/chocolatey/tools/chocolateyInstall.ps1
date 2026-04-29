$ErrorActionPreference = 'Stop'
$packageName  = 'pclun'
$url          = 'https://github.com/prov50686-ops/pclun/releases/download/v0.5.1/PcLun-0.5.1.msi'
$checksumType = 'sha256'

$packageArgs = @{
  packageName    = $packageName
  fileType       = 'msi'
  url            = $url
  checksumType   = $checksumType
  silentArgs     = '/quiet /norestart'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
