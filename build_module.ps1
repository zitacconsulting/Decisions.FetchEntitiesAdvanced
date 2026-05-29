Write-Host "Building Fetch Entities Advanced Module" -ForegroundColor Green

Write-Host "Compiling the project..." -ForegroundColor Yellow
dotnet build build.proj

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Creating Decisions module package..." -ForegroundColor Yellow
dotnet msbuild build.proj -t:build_module

if ($LASTEXITCODE -ne 0) {
    Write-Host "Module packaging failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Module built successfully!" -ForegroundColor Green
Write-Host "Output: Decisions.FetchEntitiesAdvanced.zip" -ForegroundColor Cyan
