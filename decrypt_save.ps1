param(
  [Parameter(Mandatory=$true)][string]$SaveDir,
  [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

# ---- constants from the game (recovered from EncryptTool.cs) ----
$validationKey   = "2a;ad.,&fSf^SX.,:12@D"
$cacheEncryPassword = "5:.A%KL;,.?<aH._=-/DF4s"

# header = each char of validationKey XOR 3
$header = [byte[]]@()
foreach ($ch in $validationKey.ToCharArray()) {
  $header += [byte]([byte][char]$ch -bxor 3)
}

$keyBytes = [System.Text.Encoding]::ASCII.GetBytes($cacheEncryPassword)
$keyLen = $keyBytes.Length

function Decrypt-Name([string]$name) {
  # base64 part = name without ".cache"
  $b64 = $name -replace '\.cache$',''
  $b64 = $b64.Replace('@','/')
  try {
    $bytes = [Convert]::FromBase64String($b64)
  } catch {
    return $null
  }
  $md5 = [System.Security.Cryptography.MD5]::Create()
  $hash = $md5.ComputeHash($keyBytes)
  $des = [System.Security.Cryptography.DES]::Create()
  $des.Mode = [System.Security.Cryptography.CipherMode]::CBC
  $des.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
  $des.Key = $hash[0..7]
  $des.IV  = $hash[8..15]
  $dec = $des.CreateDecryptor()
  try {
    $plain = $dec.TransformFinalBlock($bytes, 0, $bytes.Length)
    return [System.Text.Encoding]::UTF8.GetString($plain)
  } catch {
    return $null
  } finally {
    $des.Dispose(); $md5.Dispose()
  }
}

function Test-Encrypted([byte[]]$b) {
  if ($b.Length -lt $header.Length) { return $false }
  for ($i=0; $i -lt $header.Length; $i++) {
    if ($b[$i] -ne $header[$i]) { return $false }
  }
  return $true
}

function Decrypt-Body([byte[]]$b) {
  $len = $b.Length - $header.Length
  $out = New-Object byte[] $len
  for ($i=0; $i -lt $len; $i++) {
    $k = $keyBytes[$i % $keyLen]
    $out[$i] = [byte](($b[$i + $header.Length] - $k) -band 0xFF)
  }
  return $out
}

function Decompress-GZip([byte[]]$b) {
  $ms = New-Object System.IO.MemoryStream(,$b)
  $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
  $out = New-Object System.IO.MemoryStream
  $gz.CopyTo($out)
  $gz.Dispose()
  $ms.Dispose()
  return $out.ToArray()
}

if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $SaveDir) ("decrypted_" + (Split-Path -Leaf $SaveDir)) }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$files = Get-ChildItem -Path $SaveDir -File
$report = @()

foreach ($f in $files) {
  $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
  if (-not (Test-Encrypted $bytes)) {
    $report += [pscustomobject]@{ file=$f.Name; status="not-encrypted"; realName=""; size=$bytes.Length }
    continue
  }
  $body = Decrypt-Body $bytes
  try {
    $plain = Decompress-GZip $body
    $status = "ok"
  } catch {
    $plain = $body
    $status = "no-gzip"
  }
  $real = Decrypt-Name $f.Name
  if (-not $real) { $real = $f.Name }
  # sanitize filename
  $safe = $real
  foreach ($c in [System.IO.Path]::GetInvalidFileNameChars()) { $safe = $safe.Replace([string]$c, "_") }
  $outPath = Join-Path $OutDir ($safe + ".json")
  [System.IO.File]::WriteAllBytes($outPath, $plain)
  $report += [pscustomobject]@{ file=$f.Name; status=$status; realName=$real; out=$outPath; size=$plain.Length }
}

$report | Format-Table -AutoSize | Out-String -Width 300 | Write-Host
"--- decrypted to: $OutDir ---"
