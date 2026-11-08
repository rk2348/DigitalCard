# Word で deliverables/*.docx を開き、目次・ページ番号を更新して保存し、確認用の PDF を出力する。
# 使い方: powershell -File tools/finalize_docx.ps1 [-PdfDir <出力先>]
param([string]$PdfDir = "")
$ErrorActionPreference = 'Stop'
$dir = Join-Path (Split-Path $PSScriptRoot -Parent) 'deliverables'
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
  Get-ChildItem $dir -Filter '*.docx' | ForEach-Object {
    $doc = $word.Documents.Open($_.FullName)
    foreach ($toc in $doc.TablesOfContents) { $toc.Update() }
    $doc.Fields.Update() | Out-Null
    $doc.Save()
    if ($PdfDir) {
      $pdf = Join-Path $PdfDir ($_.BaseName + '.pdf')
      $doc.ExportAsFixedFormat($pdf, 17)
      Write-Output "pdf: $pdf"
    }
    $doc.Close()
    Write-Output "updated: $($_.Name)"
  }
} finally {
  $word.Quit()
}
