[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$secretId = 'aspnet-recepcaototem-0d5d7097-983f-4449-9f40-9f998d90c348'
$secretDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData')) "Microsoft\UserSecrets\$secretId"
$secretFile = Join-Path $secretDirectory 'secrets.json'

function Convert-SecureValue([Security.SecureString]$SecureValue) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Read-ExistingSecrets {
    $result = @{}
    if (-not (Test-Path -LiteralPath $secretFile)) { return $result }
    $document = Get-Content -LiteralPath $secretFile -Raw | ConvertFrom-Json
    foreach ($property in $document.PSObject.Properties) { $result[$property.Name] = [string]$property.Value }
    return $result
}

$sourceSecure = Read-Host 'Cole Migration:SourceConnection (entrada oculta)' -AsSecureString
$targetSecure = Read-Host 'Cole Migration:TargetConnection (entrada oculta)' -AsSecureString
$source = Convert-SecureValue $sourceSecure
$target = Convert-SecureValue $targetSecure
try {
    if ([string]::IsNullOrWhiteSpace($source) -or [string]::IsNullOrWhiteSpace($target)) {
        throw 'As duas conexões são obrigatórias.'
    }
    $secrets = Read-ExistingSecrets
    $secrets['Migration:SourceConnection'] = $source
    $secrets['Migration:TargetConnection'] = $target
    $secrets['Migration:TargetProject'] = 'Lumis'
    $secrets['Migration:TargetEnvironment'] = 'Production'
    $secrets.Remove('Migration:TargetFingerprint')
    New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
    $json = $secrets | ConvertTo-Json -Depth 3
    [IO.File]::WriteAllText($secretFile, $json, [Text.UTF8Encoding]::new($false))
    Write-Output 'Configurações de migração armazenadas no User Secrets. DefaultConnection foi preservada.'
    Write-Output 'O fingerprint anterior, se existia, foi removido e deve ser confirmado novamente.'
}
finally {
    $source = $null
    $target = $null
    $sourceSecure.Dispose()
    $targetSecure.Dispose()
}
