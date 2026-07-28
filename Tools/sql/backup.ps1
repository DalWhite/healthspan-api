$backup = Join-Path $PWD ("healthspan-dev-{0}.sql" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

mysqldump `
  --host=healthspan-db-dev-trackden-af76.a.aivencloud.com `
  --port=16505 `
  --user="avnadmin" `
  --password `
  --ssl-mode=REQUIRED `
  --single-transaction `
  --quick `
  --routines `
  --triggers `
  --events `
  --no-tablespaces `
  --set-gtid-purged=OFF `
  --result-file="$backup" `
  healthspan-dev

if ($LASTEXITCODE -ne 0) {
    throw "mysqldump failed with exit code $LASTEXITCODE"
}

$file = Get-Item -LiteralPath $backup
$hash = (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash

$file | Select-Object FullName, Length, LastWriteTime
"mysqldump: $($file.Name); SHA256: $hash"