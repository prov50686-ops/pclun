$ErrorActionPreference = 'Stop'
$packageName  = 'pclun'
$url          = 'https://github.com/prov50686-ops/pclun/releases/download/v0.5.0/PcLun-0.5.0.msi'
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
