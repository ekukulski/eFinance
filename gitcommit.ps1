<#
git.ps1 — “smart” Git script
- If repo is NOT initialized (.git missing): init + first commit + set branch + add remote + push -u
- If repo IS initialized: add + commit + push

Usage:
  .\git.ps1 KukiDinner "Initial commit"
  .\git.ps1 KukiDinner "Added dessert menu"

Tip (if scripts are blocked):
  Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
#>

param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$ProjectName,

    [Parameter(Position = 1)]
    [string]$CommitMessage = "Update",

    # Change defaults if you want
    [string]$BasePath = "C:\Users\Edward Kukulski\Projects",
    [string]$GitHubUser = "ekukulski",
    [string]$DefaultBranch = "master"
)

$ErrorActionPreference = "Stop"

function Assert-CommandExists($cmd) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $cmd. Install it and try again."
    }
}

function Run($commandLine) {
    Write-Host ">> $commandLine"
    & powershell -NoProfile -Command $commandLine
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed (exit code $LASTEXITCODE): $commandLine"
    }
}

Assert-CommandExists "git"

$repoPath = Join-Path $BasePath $ProjectName

if (-not (Test-Path -Path $repoPath -PathType Container)) {
    throw "Folder not found: $repoPath"
}

Set-Location $repoPath

$gitDir = Join-Path $repoPath ".git"
$remoteUrl = "https://github.com/$GitHubUser/$ProjectName"

if (-not (Test-Path $gitDir)) {
    Write-Host "No .git folder found. Initializing new repository in $repoPath..."

    Run "git init"
    Run "git add ."

    # Commit might fail if nothing to commit; keep it simple and let it error if truly empty.
    Run "git commit -m `"$CommitMessage`""

    Run "git branch -M $DefaultBranch"

    # Add remote only if not already present
    $hasOrigin = (git remote) -contains "origin"
    if (-not $hasOrigin) {
        Run "git remote add origin $remoteUrl"
    } else {
        Write-Host "Remote 'origin' already exists; not adding."
    }

    Run "git push -u origin $DefaultBranch"
    Write-Host "Done: repo initialized and pushed to $remoteUrl"
}
else {
    Write-Host ".git folder found. Doing normal add/commit/push in $repoPath..."

    Run "git add ."

    # Avoid failing the whole script when there is nothing to commit
    Write-Host ">> git commit -m `"$CommitMessage`""
    git commit -m $CommitMessage | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Nothing to commit (or commit failed). Skipping push."
        exit 0
    }

    Run "git push"
    Write-Host "Done: committed and pushed."
}
