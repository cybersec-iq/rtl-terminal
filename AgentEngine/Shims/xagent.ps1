if ([string]::IsNullOrWhiteSpace($env:XAGENT_ENGINE_ROOT)) {
    [Console]::Error.WriteLine('X Agent CLI requires an active Rtl Terminal project session.')
    $global:LASTEXITCODE = 2
    return
}

$entry = Join-Path $env:XAGENT_ENGINE_ROOT 'xagent.js'
if (-not (Test-Path -LiteralPath $entry -PathType Leaf)) {
    [Console]::Error.WriteLine('X Agent CLI runtime is unavailable for this session.')
    $global:LASTEXITCODE = 2
    return
}

$node = Get-Command node -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $node) {
    [Console]::Error.WriteLine('X Agent CLI requires Node.js 18 or newer on PATH.')
    $global:LASTEXITCODE = 9009
    return
}

& $node.Source -e "process.exit(Number(process.versions.node.split('.')[0]) >= 18 ? 0 : 1)" *> $null
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine('X Agent CLI requires Node.js 18 or newer on PATH.')
    $global:LASTEXITCODE = 9009
    return
}

& $node.Source $entry @args
