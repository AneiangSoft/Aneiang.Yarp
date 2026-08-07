param([string]$Dir, [string]$OldNs, [string]$NewNs)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

Get-ChildItem -Path $Dir -Recurse -Filter '*.cs' | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    if ($content.Contains($OldNs)) {
        $content = $content.Replace($OldNs, $NewNs)
        [System.IO.File]::WriteAllText($_.FullName, $content, $utf8NoBom)
        Write-Host "Fixed: $($_.Name)"
    }
}
Write-Host "Done."
