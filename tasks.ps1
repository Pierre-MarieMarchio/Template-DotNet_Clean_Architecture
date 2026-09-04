#Requires -Version 5.1
<#
.SYNOPSIS
    Thin wrappers over the real dotnet and docker commands used by this repository.

.NOTES
    Targets Windows PowerShell 5.1, which is what a stock Windows box ships, and runs unchanged
    on PowerShell 7+ including Linux and macOS. Nothing here uses 7-only syntax — no ternary,
    no null-coalescing, no pipeline chain operators. Keep it that way: requiring 7.0 means the
    script does not start on the machine it exists to help.

.DESCRIPTION
    Every task prints the command it is about to run, so this file doubles as documentation:
    copy the printed line and you get the same result without the script. Nothing here hides a
    flag that changes the meaning of a build — in particular no task passes anything that would
    relax TreatWarningsAsErrors.

.EXAMPLE
    ./tasks.ps1 test
    ./tasks.ps1 test -NoIntegration
    ./tasks.ps1 migration-add AddTodoItemPriority
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet(
        'restore',
        'build',
        'test',
        'coverage',
        'format',
        'format-fix',
        'migration-add',
        'database-update',
        'migration-bundle',
        'run',
        'compose-up',
        'compose-down',
        'bootstrap',
        'hygiene',
        'verify')]
    [string]$Task,

    # Name of the migration, for migration-add.
    [Parameter(Position = 1)]
    [string]$Name,

    # Skip the Testcontainers suite, which needs a running Docker daemon.
    [switch]$NoIntegration,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'AppTemplate.sln'
$persistence = Join-Path $repoRoot 'Src/Infrastructure/AppTemplate.Infrastructure.Persistence'
$api = Join-Path $repoRoot 'Src/Presentation/AppTemplate.Api'
$runsettings = Join-Path $repoRoot 'coverage.runsettings'

# Test projects are discovered from disk, never listed: a hard-coded list went stale once and a
# project's tests silently did not run. The Docker-free set is selected by the property that
# actually makes a project need Docker — a Testcontainers package reference in its csproj — so a
# future project that adopts Testcontainers migrates out of the -NoIntegration set by itself.
function Get-TestProjects {
    param([switch]$DockerFreeOnly, [string]$ExcludePattern)

    $projects = Get-ChildItem (Join-Path $repoRoot 'Tests') -Recurse -Filter *.csproj |
        Where-Object { -not $ExcludePattern -or $_.FullName -notmatch $ExcludePattern }

    if ($DockerFreeOnly) {
        $projects = $projects | Where-Object {
            (Get-Content $_.FullName -Raw) -notmatch 'Testcontainers'
        }
    }

    $paths = @($projects | Select-Object -ExpandProperty FullName | Sort-Object)
    if ($paths.Count -eq 0) {
        throw 'Discovery found no test project under Tests/. A run over nothing is a false green.'
    }

    return $paths
}

function Invoke-Step {
    param([string]$Executable, [string[]]$Arguments)

    Write-Host "> $Executable $($Arguments -join ' ')" -ForegroundColor Cyan
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$Executable $($Arguments -join ' ')' exited with $LASTEXITCODE."
    }
}

function Assert-Name {
    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "The '$Task' task needs a name, e.g. ./tasks.ps1 $Task AddTodoItemPriority"
    }
}

switch ($Task) {

    'restore' {
        Invoke-Step 'dotnet' @('restore', $solution)
    }

    'build' {
        Invoke-Step 'dotnet' @('build', $solution, '--configuration', $Configuration)
    }

    'test' {
        if ($NoIntegration) {
            foreach ($project in (Get-TestProjects -DockerFreeOnly)) {
                Invoke-Step 'dotnet' @('test', $project, '--configuration', $Configuration)
            }
        }
        else {
            Invoke-Step 'dotnet' @('test', $solution, '--configuration', $Configuration)
        }
    }

    'coverage' {
        $results = Join-Path $repoRoot 'TestResults'
        if (Test-Path $results) { Remove-Item $results -Recurse -Force }

        # AppTemplate.Architecture.Tests is excluded on purpose: NetArchTest resolves types through
        # Type.GetType(name, throwOnError: true), which fails against a Coverlet-instrumented
        # assembly, so 7 of its rules throw under the collector and all pass without it.
        # Run './tasks.ps1 test' for those.
        foreach ($project in (Get-TestProjects -ExcludePattern 'AppTemplate\.Architecture\.Tests')) {
            Invoke-Step 'dotnet' @(
                'test', $project,
                '--configuration', $Configuration,
                # Two elements, not '--collect:XPlat Code Coverage': the value contains a space,
                # and Windows PowerShell 5.1's native argument passing mangles the colon form.
                '--collect', 'XPlat Code Coverage',
                '--settings', $runsettings,
                '--results-directory', $results)
        }

        # Same floor CI enforces, read from the same file.
        $minimum = (Get-Content (Join-Path $repoRoot 'coverage.minimum') |
            Where-Object { $_ -notmatch '^\s*#' -and $_.Trim() } |
            Select-Object -First 1).Trim()

        Invoke-Step 'python' @(
            (Join-Path $repoRoot '.github/scripts/coverage-gate.py'),
            '--root', $results,
            '--minimum', $minimum)
    }

    'format' {
        Invoke-Step 'dotnet' @('format', $solution, '--verify-no-changes', '--verbosity', 'normal')
    }

    'format-fix' {
        # Also rewrites *.cs without the UTF-8 BOM that .editorconfig requires. Run this after
        # creating a file by hand, or the next 'format' task fails on encoding alone.
        Invoke-Step 'dotnet' @('format', $solution)
    }

    'migration-add' {
        Assert-Name
        Invoke-Step 'dotnet' @(
            'ef', 'migrations', 'add', $Name,
            '--project', $persistence,
            '--startup-project', $persistence)
    }

    'database-update' {
        Invoke-Step 'dotnet' @(
            'ef', 'database', 'update',
            '--project', $persistence,
            '--startup-project', $persistence)
    }

    'migration-bundle' {
        # Self-contained executable that applies pending migrations. This is how a deployment
        # migrates: the API applies migrations at startup in Development only.
        Invoke-Step 'dotnet' @(
            'ef', 'migrations', 'bundle',
            '--project', $persistence,
            '--startup-project', $persistence,
            '--configuration', $Configuration,
            '--self-contained',
            '--force',
            '--output', (Join-Path $repoRoot 'artifacts/migrate'))
    }

    'run' {
        Invoke-Step 'dotnet' @('run', '--project', $api)
    }

    'compose-up' {
        Invoke-Step 'docker' @('compose', 'up', '-d', '--wait')
    }

    'compose-down' {
        # Volumes are kept: use `docker compose down -v` by hand to discard the database.
        Invoke-Step 'docker' @('compose', 'down')
    }

    'bootstrap' {
        # Run once, immediately after `dotnet new`. Using directives are sorted alphabetically, so
        # the correct order depends on where the generated project's own namespace falls among
        # FluentValidation, Microsoft.* and the rest — which the template cannot know in advance.
        # Until this runs, `format --verify-no-changes` fails, and that is CI's first step.
        Invoke-Step 'dotnet' @('format', $solution)
        Invoke-Step 'dotnet' @('format', $solution, '--verify-no-changes', '--no-restore')
        Write-Host 'Formatting is now stable. Commit this before anything else.' -ForegroundColor Green
    }

    'hygiene' {
        # No SDK needed. Catches a doc path that no longer resolves and a workflow that would
        # fail the first time it ran.
        Invoke-Step 'python' @((Join-Path $repoRoot '.github/scripts/check-doc-paths.py'), $repoRoot)
        Invoke-Step 'python' @((Join-Path $repoRoot '.github/scripts/check-workflows.py'), $repoRoot)
    }

    'verify' {
        # The full gate, in the order CI runs it: formatting first because it is the fastest to fail.
        Invoke-Step 'python' @((Join-Path $repoRoot '.github/scripts/check-doc-paths.py'), $repoRoot)
        Invoke-Step 'python' @((Join-Path $repoRoot '.github/scripts/check-workflows.py'), $repoRoot)
        Invoke-Step 'dotnet' @('restore', $solution)
        Invoke-Step 'dotnet' @('format', $solution, '--verify-no-changes', '--no-restore')
        Invoke-Step 'dotnet' @('build', $solution, '--configuration', $Configuration, '--no-restore')
        Invoke-Step 'dotnet' @('test', $solution, '--configuration', $Configuration, '--no-build')
        Invoke-Step 'dotnet' @(
            'ef', 'migrations', 'has-pending-model-changes',
            '--project', $persistence,
            '--startup-project', $persistence,
            '--no-build')
        Invoke-Step 'dotnet' @('list', 'package', '--vulnerable', '--include-transitive')
    }
}

Write-Host "'$Task' completed." -ForegroundColor Green
