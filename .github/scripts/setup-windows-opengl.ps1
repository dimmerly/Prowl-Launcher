$ErrorActionPreference = 'Stop'
$archive = Join-Path $env:RUNNER_TEMP 'mesa.7z'
Invoke-WebRequest 'https://github.com/pal1000/mesa-dist-win/releases/download/26.2.4/mesa3d-26.2.4-release-msvc.7z' -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '351fc8c8b695878ffb3eaa044b3ead08672a48b1a045e3c3e3975811df0f6695') {
    throw 'Mesa digest mismatch.'
}
$mesa = Join-Path $env:RUNNER_TEMP 'mesa'
& 'C:/Program Files/7-Zip/7z.exe' x $archive "-o$mesa" -y
if ($LASTEXITCODE -ne 0) { throw 'Mesa extraction failed.' }
"PROWL_SMOKE_OPENGL_DIRECTORY=$mesa/x64" >> $env:GITHUB_ENV
'GALLIUM_DRIVER=llvmpipe' >> $env:GITHUB_ENV
