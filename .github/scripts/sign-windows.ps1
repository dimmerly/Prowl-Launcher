param([Parameter(Mandatory)][string[]] $Files)

$ErrorActionPreference = 'Stop'
if (-not $env:WINDOWS_SIGNING_CERTIFICATE -or -not $env:WINDOWS_SIGNING_PASSWORD) {
    throw 'Set WINDOWS_SIGNING_CERTIFICATE and WINDOWS_SIGNING_PASSWORD to sign releases.'
}
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$signTool = Get-ChildItem -LiteralPath $sdkBin -Directory | Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $signTool) { throw 'Install the Windows SDK to obtain SignTool.' }
$certificateFile = Join-Path $env:RUNNER_TEMP ('prowl-signing-' + [guid]::NewGuid().ToString('N') + '.pfx')
$certificates = @()
try {
    [System.IO.File]::WriteAllBytes($certificateFile, [Convert]::FromBase64String($env:WINDOWS_SIGNING_CERTIFICATE))
    $password = ConvertTo-SecureString $env:WINDOWS_SIGNING_PASSWORD -AsPlainText -Force
    $certificates = @(Import-PfxCertificate -FilePath $certificateFile -CertStoreLocation Cert:/CurrentUser/My -Password $password)
    $signingCertificate = $certificates | Where-Object HasPrivateKey | Select-Object -First 1
    if (-not $signingCertificate) { throw 'The signing certificate has no private key.' }
    foreach ($file in $Files) {
        $resolved = (Resolve-Path -LiteralPath $file).Path
        & $signTool sign /sha1 $signingCertificate.Thumbprint /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 $resolved
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $file" }
        & $signTool verify /pa /all $resolved
        if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $file" }
    }
}
finally {
    foreach ($certificate in $certificates) {
        Remove-Item -LiteralPath ('Cert:/CurrentUser/My/' + $certificate.Thumbprint) -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $certificateFile) { Remove-Item -LiteralPath $certificateFile }
}
