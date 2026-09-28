$ErrorActionPreference = 'Stop'

$stagedFiles = @(git diff --cached --name-only --diff-filter=ACMR)
$blockedPaths = $stagedFiles | Where-Object {
    $_ -match '(^|/)appsettings\.json$' -or
    $_ -match '(^|/)(client_secret|credentials).*\.json$' -or
    $_ -match '(^|/).*oauth.*\.json$' -or
    $_ -match '(^|/).*token.*\.json$' -or
    $_ -match '^resources/app-data/'
}

if ($blockedPaths) {
    Write-Error "Blocked local credential/state files are staged:`n$($blockedPaths -join "`n")"
}

$secretPatterns = @(
    '"Password"\s*:\s*"(?!REPLACE_ME|YOUR_|<)[^"\r\n]+"',
    '"client_secret"\s*:\s*"[^"\r\n]+"',
    '"refresh_token"\s*:\s*"[^"\r\n]+"',
    '"access_token"\s*:\s*"[^"\r\n]+"'
)

foreach ($file in $stagedFiles) {
    if ($file -eq 'scripts/check-staged-secrets.ps1') { continue }
    $content = git show ":$file" 2>$null
    if ($LASTEXITCODE -ne 0) { continue }
    foreach ($pattern in $secretPatterns) {
        if ($content -match $pattern) {
            Write-Error "Possible credential detected in staged file: $file"
        }
    }
}

Write-Output 'Staged secret check passed.'
