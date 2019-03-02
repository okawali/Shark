dotnet publish -c Release -r win10-x64 Shark.Server
dotnet publish -c Release -r osx.10.12-x64 Shark.Server
dotnet publish -c Release -r ubuntu.18.04-x64 Shark.Server

dotnet publish -c Release -r win10-x64 Shark.Client
dotnet publish -c Release -r osx.10.12-x64 Shark.Client
dotnet publish -c Release -r ubuntu.18.04-x64 Shark.Client

dotnet pack -c Release Shark.Client.Tool
dotnet pack -c Release Shark.Server.Tool

Add-Type -A System.IO.Compression.FileSystem

Remove-Item * -Include *.zip,*.nupkg

#win10-x64
$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark.Server\bin\Release\netcoreapp2.2\win10-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-server-win10-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark.Client\bin\Release\netcoreapp2.2\win10-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-win10-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

#osx.10.12-x64
$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark.Server\bin\Release\netcoreapp2.2\osx.10.12-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-server-osx.10.12-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark.Client\bin\Release\netcoreapp2.2\osx.10.12-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-osx.10.12-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)


#ubuntu.18.04-x64
$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark.Server\bin\Release\netcoreapp2.2\ubuntu.18.04-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-server-ubuntu.18.04-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

$inPath = Join-Path (Get-Item -Path ".\").FullName 'Shark.Client\bin\Release\netcoreapp2.2\ubuntu.18.04-x64\publish'
$outPath = Join-Path (Get-Item -Path ".\").FullName 'shark-ubuntu.18.04-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

#tool packages
Move-Item 'Shark.Client.Tool\bin\Release\*.nupkg' .\
Move-Item 'Shark.Server.Tool\bin\Release\*.nupkg' .\