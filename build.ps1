# Always run relative to the script's folder
Set-Location $PSScriptRoot

# Clear screen
Clear-Host

# Show directory contents
Get-ChildItem

# Clean projects
dotnet clean -c Debug
dotnet clean -c Release

# Remove obj and bin folders
Remove-Item -Recurse -Force .\obj, .\bin -ErrorAction SilentlyContinue

# Restore packages
dotnet restore

# Build Debug
dotnet build -c Debug

# Build Release
dotnet build -c Release
