param(
    [string]$WildShiftPath='',
    [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyDir='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies',
    [string]$DotNet='',
    [string]$Output=''
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$workspace=Split-Path $root -Parent
if(!$WildShiftPath){$WildShiftPath=Join-Path $root '1.7\Assemblies\WildShift.dll'}
if(!$DotNet){$DotNet=Join-Path $workspace '.tools\dotnet10\dotnet.exe'}
if(!$Output){$Output=Join-Path $workspace ('.tmp\wildshift-optional-types-'+[guid]::NewGuid().ToString('N'))}
$Output=[IO.Path]::GetFullPath($Output)
if($Output.Equals($root,[StringComparison]::OrdinalIgnoreCase)-or$Output.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be outside the repository.'}
if(Test-Path -LiteralPath $Output){throw 'Use a fresh isolated output directory.'}
$null=New-Item -ItemType Directory -Path $Output
$build=Join-Path $Output 'build'
$obj=(Join-Path $Output 'obj')+[IO.Path]::DirectorySeparatorChar
& $DotNet build (Join-Path $PSScriptRoot 'OptionalModTypes\WildShift.OptionalModTypesTests.csproj') -c Release -o $build "-p:BaseIntermediateOutputPath=$obj" --nologo -v:minimal
if($LASTEXITCODE){throw "Optional type fixture build failed: $LASTEXITCODE"}
& $DotNet (Join-Path $build 'WildShift.OptionalModTypesTests.dll') $WildShiftPath $GameDir $HarmonyDir 2>&1 | Tee-Object -FilePath (Join-Path $Output 'results.txt')
if($LASTEXITCODE){throw "Optional type checks failed: $LASTEXITCODE"}
