<#
.SYNOPSIS
Fetch GitHub Actions run metadata and render in TOON-format text.

.DESCRIPTION
Uses `gh run view --json` to fetch run details and jobs/steps information, then prints a compact, token-efficient summary.

.PARAMETER RunId
The GitHub Actions run ID (required).

.PARAMETER Repo
Optional repository `owner/repo`. If omitted, the script uses GITHUB_REPOSITORY or infers from git remote.

.PARAMETER Json
When set, writes raw JSON (from gh) instead of TOON summary.

.PARAMETER Out
Optional output file path.

.PARAMETER Repo
Optional repository `owner/repo`; default from GITHUB_REPOSITORY or git origin remote if omitted.

.PARAMETER Workflow
Optional workflow name for `latest` run resolution.

.PARAMETER JobId
Optional job ID to drill into for detailed job.steps output.

.PARAMETER ExcludeFailedLog
Skip fallback log grep for failed steps.

.PARAMETER FailedLogLines
Number of failed log lines to include when not excluded; 0 (default) includes full extracted logs, >0 limits to last N matching failure keywords.

Behavior:
- `--json` writes raw JSON output to --Out (if provided), else to docs/logs/gh_run_<id>.toon.
- Without `--json`, writes TOON format summary + jobs details.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true, Position = 0)]
    [Alias('run-id')]
    [string]
    $RunId,

    [string]
    $Repo,

    [switch]
    $Latest,

    [string]
    $Workflow,

    [switch]
    $Json,

    [string]
    $JobId,

    [Alias('exclude-failed-log')]
    [switch]
    $ExcludeFailedLog,

    [Alias('failed-log-lines')]
    [int]
    $FailedLogLines = 0,

    [string]
    $Out
)

if ($FailedLogLines -lt 0) {
    Fail 'failed-log-lines must be 0 (unlimited) or greater.'
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail {
    param([string]$Message, [int]$Code = 1)
    Write-Error $Message
    exit $Code
}

function Get-ObjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Object,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    if (-not $Object) { return $null }
    $p = $Object.PSObject.Properties[$PropertyName]
    if ($p) { return $p.Value }
    return $null
}

function Get-RunProperty {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Object,
        [Parameter(Mandatory = $true)]
        [string[]]$Keys
    )

    foreach ($key in $Keys) {
        $value = Get-ObjectProperty -Object $Object -PropertyName $key
        if ($null -ne $value) {
            return $value
        }
    }

    return $null
}

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$ScriptBlock,
        [int]$MaxAttempts = 3,
        [int]$DelaySeconds = 2
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            return & $ScriptBlock
        }
        catch {
            $err = $_.Exception.Message
            if ($attempt -eq $MaxAttempts) {
                throw "Retry failed after $MaxAttempts attempts: $err"
            }
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

function Resolve-Repo {
    param(
        [string]$Repo
    )

    if ($Repo) {
        return $Repo
    }

    if ($env:GITHUB_REPOSITORY) {
        return $env:GITHUB_REPOSITORY
    }

    try {
        Write-Verbose "Attempting gh repo view to resolve repository"
        $ghRepo = gh repo view --json nameWithOwner --jq '.nameWithOwner' 2>$null | Out-String
        if ($LASTEXITCODE -eq 0 -and $ghRepo.Trim()) {
            return $ghRepo.Trim()
        }
    }
    catch {
        Write-Verbose "gh repo view failed: $($_)"
    }

    # Try local repo in current directory or parent folders.
    try {
        $gitDir = $PWD
        while ($gitDir) {
            if (Test-Path (Join-Path $gitDir '.git')) {
                $origin = git -C $gitDir remote get-url origin 2>$null
                if ($origin) {
                    break
                }
            }
            $parent = Split-Path -Parent $gitDir
            if (-not $parent -or $parent -eq $gitDir) { break }
            $gitDir = $parent
        }
    }
    catch {
        $origin = $null
    }

    if ($origin -and $origin -match '[:/]([^/]+/[^/]+)(\.git)?$') {
        return $Matches[1]
    }

    return $null
}

function Test-RepoIdentifier {
    param([string]$value)
    return -not [string]::IsNullOrWhiteSpace($value) -and ($value -match '^[^/]+/[^/]+$')
}

function Resolve-LatestRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repo,
        [string]$Workflow
    )

    if (-not $Repo) {
        Fail 'Cannot resolve latest run: repository is not defined.'
    }

    $ghArgs = @('--repo', $Repo, '--limit', '1', '--json', 'databaseId')
    if ($Workflow) {
        $ghArgs += @('--workflow', $Workflow)
    }

    Write-Verbose "Resolving latest run via gh run list ($([string]::Join(' ', $ghArgs)))"
    $raw = gh run list @ghArgs 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Fail "gh run list failed (exit $LASTEXITCODE): $raw"
    }

    try {
        $runs = $raw | ConvertFrom-Json
    }
    catch {
        Fail "Failed parsing gh run list output as JSON: $($_)"
    }

    if (-not $runs -or $runs.Count -eq 0) {
        Fail "No runs found for workflow '$Workflow' in repository '$Repo'."
    }

    if (-not $runs[0].databaseId) {
        Fail "Latest run entry does not include a databaseId field; cannot resolve run id."
    }

    return $runs[0].databaseId
}

function Get-RunJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunId,
        [Parameter(Mandatory = $true)]
        [string]$Repo
    )

    # gh run view does not expose 'id', use databaseId (alias to run_id) instead.
    $fields = @(
        'databaseId',
        'status',
        'conclusion',
        'event',
        'attempt',
        'createdAt',
        'updatedAt',
        'headBranch',
        'headSha',
        'workflowName',
        'workflowDatabaseId',
        'displayTitle',
        'url',
        'jobs'
    )

    # Ensure field list is as supported by current gh version, fallback duplicates handled.
    Write-Verbose "Fetching run metadata for run ID '$RunId' in repo '$Repo'"

    # Prefer gh run view first (avoids API 404 on odd API path details).
    $result = $null

    if ($Repo) {
        Write-Verbose "gh run view --repo $Repo --json $($fields -join ',') $RunId"
        try {
            $result = Invoke-WithRetry -ScriptBlock {
                gh run view --repo $Repo --json $($fields -join ',') $RunId 2>&1 | Out-String
            }
            if ($LASTEXITCODE -eq 0 -and $result.Trim()) {
                return $result
            }
            Write-Verbose "gh run view (--repo) failed (exit $LASTEXITCODE): $result"
        }
        catch {
            Write-Verbose "gh run view (--repo) retry failed: $($_)"
        }
    }

    Write-Verbose "gh run view --json $($fields -join ',') $RunId"
    try {
        $result = Invoke-WithRetry -ScriptBlock {
            gh run view --json $($fields -join ',') $RunId 2>&1 | Out-String
        }
        if ($LASTEXITCODE -eq 0 -and $result.Trim()) {
            return $result
        }
        Write-Verbose "gh run view (no repo) failed (exit $LASTEXITCODE): $result"
    }
    catch {
        Write-Verbose "gh run view (no repo) retry failed: $($_)"
    }

    # Most cases should now be handled, but if we have a repo claim we can still try API fallback.
    if ($Repo) {
        Write-Warning "gh run view failed; trying fallback to gh api for repo $Repo"
        try {
            Write-Verbose "gh api repos/$Repo/actions/runs/$RunId --jq '{ id, status, conclusion, event, attempt, created_at, updated_at, head_branch, head_sha, workflow_id, workflow_name, workflow_database_id, display_title, name, html_url, jobs }'"
            $result = Invoke-WithRetry -ScriptBlock {
                gh api "repos/$Repo/actions/runs/$RunId" --jq '{ id, status, conclusion, event, attempt, created_at, updated_at, head_branch, head_sha, workflow_id, workflow_name, workflow_database_id, display_title, name, html_url, jobs }' 2>&1 | Out-String
            }
            if ($LASTEXITCODE -eq 0 -and $result.Trim()) {
                return $result
            }
            Fail "gh api failed (exit $LASTEXITCODE). Output: $result"
        }
        catch {
            Fail "Unable to fetch run data from GitHub: $($_)"
        }
    }

    Fail "Unable to fetch run data from GitHub. Last run view output: $result"
}

function Get-JobsData {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunId,
        [Parameter(Mandatory = $false)]
        [string]$Repo
    )

    try {
        if ($Repo) {
            $jobsRaw = gh api repos/$Repo/actions/runs/$RunId/jobs 2>&1 | Out-String
        }
        else {
            Write-Verbose "No repo context for jobs API; using gh run view --json jobs"
            $jobsRaw = gh run view --json jobs $RunId 2>&1 | Out-String
        }

        if ($LASTEXITCODE -eq 0 -and $jobsRaw.Trim()) {
            $jobsData = $jobsRaw | ConvertFrom-Json
            if (-not $jobsData.jobs -and $jobsData.PSObject.Properties.Name -contains 'job') {
                # Handle plain job list for old formats
                $jobsData = @{ jobs = $jobsData.job }
            }

            $count = $null
            if ($jobsData.total_count) {
                $count = $jobsData.total_count
            }
            elseif ($jobsData.jobs) {
                $count = $jobsData.jobs.Count
            }
            else {
                $count = 0
            }

            Write-Verbose "Fetched jobs list, total: $count"
            return $jobsData
        }

        Write-Warning 'Could not fetch detailed jobs list; using run job summary only.'
        return $null
    }
    catch {
        Write-Warning "Error fetching jobs list: $($_)"
        return $null
    }
}

function ConvertTo-RunFields {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Run
    )

    $createdAt = Get-RunProperty -Object $Run -Keys @('createdAt', 'created_at')
    $updatedAt = Get-RunProperty -Object $Run -Keys @('updatedAt', 'updated_at')
    $attempt = Get-RunProperty -Object $Run -Keys @('attempt')

    $workflow = Get-RunProperty -Object $Run -Keys @('workflow', 'workflow_id')
    $workflowName = Get-RunProperty -Object $Run -Keys @('workflowName', 'workflow_name')
    $workflowDatabaseId = Get-RunProperty -Object $Run -Keys @('workflowDatabaseId', 'workflow_database_id')
    $displayTitle = Get-RunProperty -Object $Run -Keys @('displayTitle', 'display_title')
    $url = Get-RunProperty -Object $Run -Keys @('url', 'html_url')
    $headBranch = Get-RunProperty -Object $Run -Keys @('headBranch', 'head_branch')
    $headSha = Get-RunProperty -Object $Run -Keys @('headSha', 'head_sha')

    $runId = Get-RunProperty -Object $Run -Keys @('id', 'databaseId')
    $status = Get-ObjectProperty -Object $Run -PropertyName 'status'
    $conclusion = Get-ObjectProperty -Object $Run -PropertyName 'conclusion'

    if (-not $status) {
        Write-Warning "Run status is missing in the run payload for $runId"
    }
    if (-not $conclusion) {
        Write-Warning "Run conclusion is missing in the run payload for $runId"
    }

    return [pscustomobject]@{
        id                   = $runId
        status               = $status
        conclusion           = $conclusion
        event                = Get-ObjectProperty -Object $Run -PropertyName 'event'
        attempt              = $attempt
        created_at           = $createdAt
        updated_at           = $updatedAt
        workflow             = $workflow
        workflow_name        = $workflowName
        workflow_database_id = $workflowDatabaseId
        display_title        = $displayTitle
        name                 = Get-ObjectProperty -Object $Run -PropertyName 'name'
        url                  = $url
        head_branch          = $headBranch
        head_sha             = $headSha
        jobs                 = Get-ObjectProperty -Object $Run -PropertyName 'jobs'
    }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Fail "Missing GitHub CLI (gh). Install from https://cli.github.com/ and run 'gh auth login'."
}

$rawRepoInput = $Repo
$Repo = Resolve-Repo -Repo $Repo
if (-not $Repo) {
    Fail "Repository not specified. Use --repo owner/repo or run from a git repository with origin remote, or set GITHUB_REPOSITORY."
}

if ($RunId -eq 'latest-release') {
    $Latest = $true
    $RunId = 'latest'
    $Workflow = 'release'
}

if ($Latest -or $RunId -eq 'latest') {
    if (-not $Workflow -and $rawRepoInput -and -not (Test-RepoIdentifier $rawRepoInput)) {
        # Interpret second positional value as workflow name when using syntax: latest <workflow>
        $Workflow = $rawRepoInput
    }

    $RunId = Resolve-LatestRun -Repo $Repo -Workflow $Workflow
    if (-not $RunId) {
        Fail "Unable to resolve the latest run for workflow '$Workflow' in repo '$Repo'."
    }

    Write-Verbose "Using resolved run ID $RunId for latest workflow run."
}

$runJson = Get-RunJson -RunId $RunId -Repo $Repo

if ($Json) {
    if (-not $Out) {
        $Out = Join-Path -Path 'docs/logs' -ChildPath "gh_run_${RunId}.toon"
    }
    $outDir = [System.IO.Path]::GetDirectoryName($Out)
    if (-not [System.IO.Directory]::Exists($outDir)) {
        New-Item -Path $outDir -ItemType Directory -Force | Out-Null
    }
    if ($Out) {
        $runJson | Out-File -FilePath $Out -Encoding utf8
        Write-Verbose "Written JSON output to $Out"
    }
    Write-Output $runJson
    exit 0
}

try {
    $run = $runJson | ConvertFrom-Json
}
catch {
    Fail "Failed parsing JSON output from gh. $($_)"
}

if (-not $run) {
    Fail "No run data returned for run ID $RunId."
}

$normalizedRun = ConvertTo-RunFields -Run $run

# Attempt to get richer job/step information from the jobs endpoint
$jobsData = Get-JobsData -RunId $RunId -Repo $Repo

# Fallback: if run payload lacks jobs but jobs endpoint has them, use that
if (-not $normalizedRun.jobs -and $jobsData -and $jobsData.jobs) {
    $normalizedRun.jobs = $jobsData
    Write-Verbose "run jobs data missing; using jobs endpoint response for job list"
}

if ($JobId -and -not $jobsData) {
    Fail "Requested JobId $JobId but jobsData is unavailable."
}


$jobEntries = @()
$failedJobs = @()
$failedSteps = @()
$jobCount = 0

if ($jobsData -and $jobsData.jobs) {
    $jobCount = $jobsData.total_count
    foreach ($job in $jobsData.jobs) {
        $jobEntries += "$($job.id)|$($job.name)|$($job.status)|$($job.conclusion)|$($job.started_at)|$($job.completed_at)"

        if ($job.conclusion -and $job.conclusion -ne 'success') {
            $failedJobs += "$($job.id)|$($job.name)|$($job.conclusion)|$($job.started_at)|$($job.completed_at)"
        }

        if ($job.steps) {
            foreach ($step in $job.steps) {
                if ($step.conclusion -and $step.conclusion -ne 'success') {
                    $failedSteps += "$($job.name)|$($step.name)|$($step.conclusion)|$($step.number)|$($step.started_at)|$($step.completed_at)"
                }
            }
        }
    }
}
elseif ($normalizedRun.jobs -and $normalizedRun.jobs.jobs) {
    $jobCount = $normalizedRun.jobs.jobs.Count
    foreach ($job in $normalizedRun.jobs.jobs) {
        $jobEntries += "$($job.id)|$($job.name)|$($job.status)|$($job.conclusion)||"

        if ($job.conclusion -and $job.conclusion -ne 'success') {
            $failedJobs += "$($job.id)|$($job.name)|$($job.conclusion)||"
        }

        if ($job.steps) {
            foreach ($step in $job.steps) {
                if ($step.conclusion -and $step.conclusion -ne 'success') {
                    $failedSteps += "$($job.name)|$($step.name)|$($step.conclusion)|$($step.number)||"
                }
            }
        }
    }
}

$failedJobsCount = $failedJobs.Count
$failedStepsCount = $failedSteps.Count

# Auto collect failed job ids for focused diagnosis
$failedJobIds = @()
if ($jobsData -and $jobsData.jobs) {
    $failedJobIds = $jobsData.jobs | Where-Object { $_.conclusion -and $_.conclusion -ne 'success' } | Select-Object -ExpandProperty id
}
elseif ($normalizedRun.jobs -and $normalizedRun.jobs.jobs) {
    $failedJobIds = $normalizedRun.jobs.jobs | Where-Object { $_.conclusion -and $_.conclusion -ne 'success' } | Select-Object -ExpandProperty id
}

$failedLogSnippet = @()
if (-not $ExcludeFailedLog) {
    # First pass: structured data from jobs[]/steps[] if available
    if ($jobsData -and $jobsData.jobs) {
        foreach ($job in $jobsData.jobs) {
            if ($job.steps) {
                foreach ($step in $job.steps) {
                    if ($step.conclusion -and $step.conclusion -ne 'success') {
                        $failedLogSnippet += "job=$($job.name);step=$($step.name);conclusion=$($step.conclusion);number=$($step.number)"
                    }
                }
            }
        }
    }
    elseif ($normalizedRun.jobs -and $normalizedRun.jobs.jobs) {
        foreach ($job in $normalizedRun.jobs.jobs) {
            if ($job.steps) {
                foreach ($step in $job.steps) {
                    if ($step.conclusion -and $step.conclusion -ne 'success') {
                        $failedLogSnippet += "job=$($job.name);step=$($step.name);conclusion=$($step.conclusion);number=$($step.number)"
                    }
                }
            }
        }
    }

    # If requested via default mode, include the full failed run logs in the snippet.
    if ($FailedLogLines -eq 0) {
        try {
            $fullLogs = gh run view $RunId --repo $Repo --log-failed 2>&1 | Out-String
            if ($fullLogs.Trim()) {
                $failedLogSnippet = $fullLogs -split "`n"
            }
        }
        catch {
            Write-Warning "Could not fetch failed run logs: $($_)"
        }
    }
    # Otherwise, if we did not have structured details, keep the most relevant lines.
    elseif ($failedLogSnippet.Count -eq 0) {
        try {
            $fullLogs = gh run view $RunId --repo $Repo --log-failed 2>&1 | Out-String
            if ($fullLogs.Trim()) {
                $lines = $fullLogs -split "`n"

                $interesting = $lines | Where-Object { $_ -match '(?i)error|failed|cmake|build|step|failed to solve' }
                if ($interesting.Count -gt $FailedLogLines) {
                    $interesting = $interesting[ - $FailedLogLines..-1]
                }
                $failedLogSnippet = $interesting
            }
        }
        catch {
            Write-Warning "Could not fetch failed run logs: $($_)"
        }
    }
}

function ToonLine { param($k, $v) "${k}:${v}" }

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('TOON:github_actions_run')
$lines.Add((ToonLine -k 'run_id' -v $normalizedRun.id))
$lines.Add((ToonLine -k 'status' -v $normalizedRun.status))
$lines.Add((ToonLine -k 'conclusion' -v $normalizedRun.conclusion))
$lines.Add((ToonLine -k 'event' -v $normalizedRun.event))
$lines.Add((ToonLine -k 'attempt' -v $normalizedRun.attempt))
$lines.Add((ToonLine -k 'workflow_name' -v $normalizedRun.workflow_name))
$lines.Add((ToonLine -k 'workflow_database_id' -v $normalizedRun.workflow_database_id))
$lines.Add((ToonLine -k 'display_title' -v $normalizedRun.display_title))
$lines.Add((ToonLine -k 'created_at' -v $normalizedRun.created_at))
$lines.Add((ToonLine -k 'updated_at' -v $normalizedRun.updated_at))
$lines.Add((ToonLine -k 'workflow' -v $normalizedRun.workflow))
$lines.Add((ToonLine -k 'name' -v $normalizedRun.name))
$lines.Add((ToonLine -k 'url' -v $normalizedRun.url))
$lines.Add((ToonLine -k 'head_branch' -v $normalizedRun.head_branch))
$lines.Add((ToonLine -k 'head_sha' -v $normalizedRun.head_sha))
$lines.Add((ToonLine -k 'job_count' -v $jobCount))
$lines.Add((ToonLine -k 'failed_job_count' -v $failedJobsCount))
$lines.Add((ToonLine -k 'failed_step_count' -v $failedStepsCount))
if ($failedJobIds.Count -gt 0) {
    $lines.Add((ToonLine -k 'failed_job_ids' -v ($failedJobIds -join ',')))
}

if ($failedLogSnippet.Count -gt 0) {
    $lines.Add("failed_log_snippet[$($failedLogSnippet.Count)]:")
    foreach ($line in $failedLogSnippet) {
        $lines.Add("  $line")
    }
}

if ($failedJobsCount -gt 0) {
    $lines.Add("failed_jobs[$failedJobsCount]{id,name,conclusion,started_at,completed_at}:")
    foreach ($entry in $failedJobs) {
        $parts = $entry -split '\|'
        $lines.Add("  $($parts[0]),$($parts[1]),$($parts[2]),$($parts[3]),$($parts[4])")
    }
}

if ($failedStepsCount -gt 0) {
    $lines.Add("failed_steps[$failedStepsCount]{job,step,conclusion,step_number,started_at,completed_at}:")
    foreach ($entry in $failedSteps) {
        $parts = $entry -split '\|'
        $lines.Add("  $($parts[0]),$($parts[1]),$($parts[2]),$($parts[3]),$($parts[4]),$($parts[5])")
    }
}

$autoDrillJobIds = @()
if (-not $JobId -and $failedJobIds.Count -gt 0) {
    $autoDrillJobIds = $failedJobIds
}
elseif ($JobId) {
    $autoDrillJobIds = @($JobId)
}

foreach ($drillId in $autoDrillJobIds) {
    $selectedJob = $null
    if ($jobsData -and $jobsData.jobs) {
        $selectedJob = $jobsData.jobs | Where-Object { $_.id -eq [long]$drillId }
    }
    elseif ($normalizedRun.jobs -and $normalizedRun.jobs.jobs) {
        $selectedJob = $normalizedRun.jobs.jobs | Where-Object { $_.id -eq [long]$drillId }
    }

    if (-not $selectedJob) {
        Write-Warning "Job ID $drillId not found in run $RunId; skipping deep drill."
        continue
    }

    $lines.Add("job_detail:${drillId}")
    $lines.Add("job_id:$($selectedJob.id)")
    $lines.Add("job_name:$($selectedJob.name)")
    $lines.Add("job_status:$($selectedJob.status)")
    $lines.Add("job_conclusion:$($selectedJob.conclusion)")
    $lines.Add("job_started_at:$($selectedJob.started_at)")
    $lines.Add("job_completed_at:$($selectedJob.completed_at)")

    if ($selectedJob.steps) {
        $lines.Add("job_steps[$($selectedJob.steps.Count)]{name,status,conclusion,number,started_at,completed_at}:")
        foreach ($step in $selectedJob.steps) {
            $lines.Add("  $($step.name),$($step.status),$($step.conclusion),$($step.number),$($step.started_at),$($step.completed_at)")
        }
    }
}

$lines.Add("jobs[$jobCount]{id,name,status,conclusion,started_at,completed_at}:")
foreach ($entry in $jobEntries) {
    $parts = $entry -split '\|'
    $lines.Add("  $($parts[0]),$($parts[1]),$($parts[2]),$($parts[3]),$($parts[4]),$($parts[5])")
}

$outString = $lines -join "`n"

if (-not $Out) {
    $Out = Join-Path -Path 'docs/logs' -ChildPath "gh_run_${RunId}.toon"
}
$outDir = [System.IO.Path]::GetDirectoryName($Out)
if (-not [System.IO.Directory]::Exists($outDir)) {
    New-Item -Path $outDir -ItemType Directory -Force | Out-Null
}

if ($Out) {
    try {
        $outString | Out-File -FilePath $Out -Encoding utf8
        Write-Verbose "Written output to $Out"
    }
    catch {
        $err = $_.Exception.Message
        Fail ("Failed to write output file ${Out}: " + $err)
    }
}

Write-Output $outString
exit 0
