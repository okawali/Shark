dotnet publish -c Release -r win-x64 --self-contained Shark.Server
dotnet publish -c Release -r osx-x64 --self-contained Shark.Server
dotnet publish -c Release -r linux-x64 --self-contained Shark.Server

dotnet publish -c Release -r win-x64 --self-contained Shark.Client
dotnet publish -c Release -r osx-x64 --self-contained Shark.Client
dotnet publish -c Release -r linux-x64 --self-contained Shark.Client

dotnet pack -c Release Shark.Client.Tool
dotnet pack -c Release Shark.Server.Tool
dotnet pack -c Release Shark.Commons

Add-Type -A System.IO.Compression.FileSystem

Remove-Item * -Include *.zip,*.nupkg

#win-x64
$inPath = Join-Path (Get-Item -Path "./").FullName 'Shark.Server/bin/Release/net8.0/win-x64/publish'
$outPath = Join-Path (Get-Item -Path "./").FullName 'shark-server-win-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

$inPath = Join-Path (Get-Item -Path "./").FullName 'Shark.Client/bin/Release/net8.0/win-x64/publish'
$outPath = Join-Path (Get-Item -Path "./").FullName 'shark-win-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

#osx-x64
$inPath = Join-Path (Get-Item -Path "./").FullName 'Shark.Server/bin/Release/net8.0/osx-x64/publish'
$outPath = Join-Path (Get-Item -Path "./").FullName 'shark-server-osx-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

$inPath = Join-Path (Get-Item -Path "./").FullName 'Shark.Client/bin/Release/net8.0/osx-x64/publish'
$outPath = Join-Path (Get-Item -Path "./").FullName 'shark-osx-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)


#ubuntu.18.04-x64
$inPath = Join-Path (Get-Item -Path "./").FullName 'Shark.Server/bin/Release/net8.0/linux-x64/publish'
$outPath = Join-Path (Get-Item -Path "./").FullName 'shark-server-linux-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)

$inPath = Join-Path (Get-Item -Path "./").FullName 'Shark.Client/bin/Release/net8.0/linux-x64/publish'
$outPath = Join-Path (Get-Item -Path "./").FullName 'shark-linux-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($inPath, $outPath, [System.IO.Compression.CompressionLevel]::Optimal, $False)
