dotnet publish -c Release -r win10-x64 Shark
dotnet publish -c Release -r osx.10.12-x64 Shark
dotnet publish -c Release -r ubuntu.16.04-x64 Shark
Add-Type -A System.IO.Compression.FileSystem

#win10-x64
$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark\bin\Release\netcoreapp2.1\win10-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-server-win10-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

#osx.10.12-x64
$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark\bin\Release\netcoreapp2.1\osx.10.12-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-server-osx.10.12-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

#ubuntu.16.04-x64
$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark\bin\Release\netcoreapp2.1\ubuntu.16.04-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-server-ubuntu.16.04-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)