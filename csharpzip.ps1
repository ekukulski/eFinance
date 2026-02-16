Get-ChildItem -Path . -Recurse -File |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  Select-Object -Unique |
  Compress-Archive -DestinationPath MyApp.zip