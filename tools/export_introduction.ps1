$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.12f1/Editor/Unity.exe'
New-Item -ItemType Directory -Force -Path "$project/tmp" | Out-Null
$process = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-projectPath', ('"' + $project + '"'), '-executeMethod', 'IntroductionStudio.ExportBatch', '-logFile', ('"' + $project + '/tmp/introduction-native-export.log"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Unityでの動画作成に失敗しました。tmp/introduction-native-export.log を確認してください。' }
Write-Output "$project/deliverables/OriginalSummoner_Unity/オリジナルサモナー_UnityNative.mp4"
