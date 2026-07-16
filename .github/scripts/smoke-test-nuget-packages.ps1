param(
    [Parameter(Mandatory = $true)]
    [string] $ArtifactsPath,

    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$artifacts = (Resolve-Path -LiteralPath $ArtifactsPath).Path
$packageIds = @(
    'Dignite.Abp.Notifications.Abstractions',
    'Dignite.Abp.Notifications',
    'Dignite.Abp.Notifications.Emailing',
    'Dignite.Abp.Notifications.Emailing.Identity',
    'Dignite.Abp.Notifications.Identity',
    'Dignite.Abp.Notifications.SignalR',
    'Dignite.Abp.NotificationCenter.Domain.Shared',
    'Dignite.Abp.NotificationCenter.Domain',
    'Dignite.Abp.NotificationCenter.Application.Contracts',
    'Dignite.Abp.NotificationCenter.Application',
    'Dignite.Abp.NotificationCenter.HttpApi',
    'Dignite.Abp.NotificationCenter.HttpApi.Client',
    'Dignite.Abp.NotificationCenter.EntityFrameworkCore',
    'Dignite.Abp.NotificationCenter.MongoDB',
    'Dignite.Abp.NotificationCenter.Web'
)

foreach ($packageId in $packageIds) {
    $packagePath = Join-Path $artifacts "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "Expected package was not produced: $packagePath"
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "dignite-notifications-nuget-smoke-$([Guid]::NewGuid().ToString('N'))"
$projectPath = Join-Path $tempRoot 'PackageSmoke.csproj'
$sourcePath = Join-Path $tempRoot 'PackageSmoke.cs'
$nuGetConfigPath = Join-Path $tempRoot 'NuGet.Config'
$previousNuGetPackages = $env:NUGET_PACKAGES

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $env:NUGET_PACKAGES = Join-Path $tempRoot '.nuget-packages'

    $packageReferences = ($packageIds | ForEach-Object {
        "    <PackageReference Include=`"$_`" Version=`"$Version`" />"
    }) -join [Environment]::NewLine

    Set-Content -LiteralPath $projectPath -Encoding utf8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
$packageReferences
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath $sourcePath -Encoding utf8 -Value @'
namespace PackageSmoke;

public static class PackageSurface
{
    public static readonly Type[] ModuleTypes =
    [
        typeof(Dignite.Abp.Notifications.AbpNotificationsAbstractionsModule),
        typeof(Dignite.Abp.Notifications.AbpNotificationsModule),
        typeof(Dignite.Abp.Notifications.Emailing.AbpNotificationsEmailingModule),
        typeof(Dignite.Abp.Notifications.Emailing.Identity.AbpNotificationsEmailingIdentityModule),
        typeof(Dignite.Abp.Notifications.Identity.AbpNotificationsIdentityModule),
        typeof(Dignite.Abp.Notifications.SignalR.AbpNotificationsSignalRModule),
        typeof(Dignite.Abp.NotificationCenter.AbpNotificationCenterDomainSharedModule),
        typeof(Dignite.Abp.NotificationCenter.AbpNotificationCenterDomainModule),
        typeof(Dignite.Abp.NotificationCenter.AbpNotificationCenterApplicationContractsModule),
        typeof(Dignite.Abp.NotificationCenter.AbpNotificationCenterApplicationModule),
        typeof(Dignite.Abp.NotificationCenter.AbpNotificationCenterHttpApiModule),
        typeof(Dignite.Abp.NotificationCenter.AbpNotificationCenterHttpApiClientModule),
        typeof(Dignite.Abp.NotificationCenter.EntityFrameworkCore.AbpNotificationCenterEntityFrameworkCoreModule),
        typeof(Dignite.Abp.NotificationCenter.MongoDB.AbpNotificationCenterMongoDbModule),
        typeof(Dignite.Abp.NotificationCenter.Web.AbpNotificationCenterWebModule)
    ];
}
'@

    $escapedArtifacts = [System.Security.SecurityElement]::Escape($artifacts)
    Set-Content -LiteralPath $nuGetConfigPath -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="package-smoke" value="$escapedArtifacts" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@

    & dotnet restore $projectPath --configfile $nuGetConfigPath
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet package smoke restore failed with exit code $LASTEXITCODE."
    }

    & dotnet build $projectPath --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet package smoke build failed with exit code $LASTEXITCODE."
    }

    Write-Host "Successfully restored and compiled a consumer of all $($packageIds.Count) NuGet packages at version $Version."
}
finally {
    if ($null -eq $previousNuGetPackages) {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }
    else {
        $env:NUGET_PACKAGES = $previousNuGetPackages
    }

    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
