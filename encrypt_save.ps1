param(
  [Parameter(Mandatory=$true)][string]$JsonDir,
  [string]$OutDir = "",
  [switch]$KeepNames
)

$ErrorActionPreference = "Stop"

$validationKey   = "2a;ad.,&fSf^SX.,:12@D"
$cacheEncryPassword = "5:.A%KL;,.?<aH._=-/DF4s"

$header = [byte[]]@()
foreach ($ch in $validationKey.ToCharArray()) {
  $header += [byte]([byte][char]$ch -bxor 3)
}
$keyBytes = [System.Text.Encoding]::ASCII.GetBytes($cacheEncryPassword)
$keyLen = $keyBytes.Length

function Encrypt-Name([string]$name) {
  $md5 = [System.Security.Cryptography.MD5]::Create()
  $hash = $md5.ComputeHash($keyBytes)
  $des = [System.Security.Cryptography.DES]::Create()
  $des.Mode = [System.Security.Cryptography.CipherMode]::CBC
  $des.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
  $des.Key = $hash[0..7]
  $des.IV  = $hash[8..15]
  $enc = $des.CreateEncryptor()
  $plain = [System.Text.Encoding]::UTF8.GetBytes($name)
  $out = $enc.TransformFinalBlock($plain, 0, $plain.Length)
  $b64 = [Convert]::ToBase64String($out)
  $des.Dispose(); $md5.Dispose()
  return $b64.Replace('/','@')
}

function Compress-GZip([byte[]]$b) {
  $ms = New-Object System.IO.MemoryStream
  $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionLevel]::Optimal, $true)
  $gz.Write($b, 0, $b.Length)
  $gz.Dispose()
  return $ms.ToArray()
}

function Encrypt-Body([byte[]]$b) {
  $out = New-Object byte[] $b.Length
  for ($i=0; $i -lt $b.Length; $i++) {
    $k = $keyBytes[$i % $keyLen]
    $out[$i] = [byte](($b[$i] + $k) -band 0xFF)
  }
  $result = New-Object byte[] ($header.Length + $out.Length)
  [Array]::Copy($header, 0, $result, 0, $header.Length)
  [Array]::Copy($out, 0, $result, $header.Length, $out.Length)
  return $result
}

if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $JsonDir) ("reencrypted_" + (Split-Path -Leaf $JsonDir)) }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$files = Get-ChildItem -Path $JsonDir -File
foreach ($f in $files) {
  if ($f.Extension -ne ".json") { continue }
  $name = $f.BaseName
  $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
  # minify json if possible
  try {
    $doc = $bytes | ConvertFrom-Json -Depth 100 -AsHashtable 2>$null
    if ($null -ne $doc) {
      # PowerShell ConvertTo-Json is lossy for deep nesting; skip minify to be safe
    }
  } catch {}
  $compressed = Compress-GZip $bytes
  $encrypted = Encrypt-Body $compressed
  if ($KeepNames) {
    $outName = $name + ".cache"
  } else {
    $outName = (Encrypt-Name $name) + ".cache"
  }
  [System.IO.File]::WriteAllBytes((Join-Path $OutDir $outName), $encrypted)
  "  $name -> $outName ($($bytes.Length) -> $($encrypted.Length) bytes)"
}
"--- encrypted to: $OutDir ---"
