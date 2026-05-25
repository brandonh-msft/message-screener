param(
    [string]$CopilotCliPath = "copilot",
    [string]$CopilotModel,
    [switch]$SkipCopilotRuntimeHook,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$runtimeConfigDirectory = Join-Path $repoRoot "src/MessageScreener.Api/copilot-config"
$runtimeSkillDirectory = Join-Path $repoRoot "src/MessageScreener.Api/copilot-config/skills/communication-twin"
$communicationTwinPath = Join-Path $runtimeConfigDirectory "communication-twin.json"
$communicationTwinSkillPath = Join-Path $runtimeSkillDirectory "SKILL.md"
$promptPath = Join-Path $repoRoot "scripts/prompts/communication-twin.workiq.prompt.md"

function Resolve-CopilotCliCommand {
    param(
        [string]$RequestedPath
    )

    function Is-VsCodeCopilotShimPath {
        param(
            [string]$Path
        )

        if ([string]::IsNullOrWhiteSpace($Path)) {
            return $false
        }

        return ($Path -match "[\\/]\.vscode-server(?:-insiders)?[\\/].*github\.copilot-chat[\\/]copilotCli[\\/]copilot")
    }

    function Test-CopilotCandidate {
        param(
            [string]$CandidatePath
        )

        if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
            return $false
        }

        if (Is-VsCodeCopilotShimPath -Path $CandidatePath) {
            return $false
        }

        try {
            $versionOutput = & $CandidatePath --version 2>&1 | Out-String
            $versionText = $versionOutput.Trim()

            if ($LASTEXITCODE -ne 0) {
                return $false
            }

            # VS Code Copilot shim may exist on PATH but fail with this text.
            if ($versionText -match "Cannot find GitHub Copilot CLI") {
                return $false
            }

            return $true
        }
        catch {
            return $false
        }
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates += $RequestedPath
    }
    $candidates += @("/usr/local/bin/copilot", "copilot")

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        $candidateInfo = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($null -ne $candidateInfo -and (Test-CopilotCandidate -CandidatePath $candidateInfo.Source)) {
            return $candidateInfo.Source
        }
    }

    $npmInfo = Get-Command "npm" -ErrorAction SilentlyContinue
    if ($null -ne $npmInfo) {
        try {
            $npmPrefix = (& npm config get prefix 2>$null | Out-String).Trim()
            if (-not [string]::IsNullOrWhiteSpace($npmPrefix)) {
                $npmBins = @(
                    (Join-Path $npmPrefix "bin"),
                    $npmPrefix
                )

                foreach ($binDir in ($npmBins | Select-Object -Unique)) {
                    if (-not (Test-Path $binDir)) {
                        continue
                    }

                    if (($env:PATH -split [IO.Path]::PathSeparator) -notcontains $binDir) {
                        $env:PATH = "$binDir$([IO.Path]::PathSeparator)$env:PATH"
                    }

                    foreach ($exeName in @("copilot", "copilot.cmd")) {
                        $fullPath = Join-Path $binDir $exeName
                        if ((Test-Path $fullPath) -and (Test-CopilotCandidate -CandidatePath $fullPath)) {
                            return $fullPath
                        }
                    }
                }
            }
        }
        catch {
            # Fall through to final error with explicit remediation.
        }
    }

    return $null
}

if (-not (Test-Path $runtimeConfigDirectory)) {
    New-Item -Path $runtimeConfigDirectory -ItemType Directory | Out-Null
}

if (-not (Test-Path $runtimeSkillDirectory)) {
    New-Item -Path $runtimeSkillDirectory -ItemType Directory -Force | Out-Null
}

if (-not (Test-Path $promptPath)) {
    throw "Twin generation prompt was not found at $promptPath"
}

if ((Test-Path $communicationTwinPath) -and -not $Force) {
    Write-Host "Communication twin already exists at $communicationTwinPath"
    Write-Host "Use -Force to overwrite it."
    exit 0
}

$resolvedCopilotCli = Resolve-CopilotCliCommand -RequestedPath $CopilotCliPath
if ([string]::IsNullOrWhiteSpace($resolvedCopilotCli)) {
    throw "No usable Copilot CLI command was found (checked '$CopilotCliPath' and 'copilot'). Re-run '.devcontainer/scripts/bootstrap-copilot-digital-twin.sh' and authenticate with 'gh auth login', then re-run setup."
}

Write-Host "Using Copilot CLI command: $resolvedCopilotCli"
Write-Host "Generating communication twin via Copilot CLI + WorkIQ..."

try {
    $promptText = Get-Content -Path $promptPath -Raw

    $copilotArgs = @(
        "--prompt", $promptText,
        "--silent",
        "--output-format", "text"
    )

    if (-not [string]::IsNullOrWhiteSpace($CopilotModel)) {
        $copilotArgs += @("--model", $CopilotModel)
    }

    $copilotOutput = & $resolvedCopilotCli @copilotArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Copilot CLI exited with code $LASTEXITCODE."
    }

    $responseText = ($copilotOutput | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($responseText)) {
        throw "Copilot CLI returned empty output."
    }

    $jsonMatch = [System.Text.RegularExpressions.Regex]::Match(
        $responseText,
        '(?s)```(?:json)?\s*(\{.*?\})\s*```|(?s)(\{.*\})')

    if (-not $jsonMatch.Success) {
        throw "Copilot CLI output did not contain a JSON object."
    }

    $jsonPayload = if ($jsonMatch.Groups[1].Success) { $jsonMatch.Groups[1].Value } else { $jsonMatch.Groups[2].Value }
    Set-Content -Path $communicationTwinPath -Value $jsonPayload -Encoding utf8
}
catch {
    throw "Copilot CLI persona generation failed. Ensure Copilot CLI is authenticated and WorkIQ is available, then retry. Inner error: $($_.Exception.Message)"
}

if (-not (Test-Path $communicationTwinPath)) {
    throw "Persona generation did not produce $communicationTwinPath"
}

$rawPersona = Get-Content -Path $communicationTwinPath -Raw
$persona = $rawPersona | ConvertFrom-Json -ErrorAction Stop

if ([string]::IsNullOrWhiteSpace($persona.ownerDisplayName)) {
    throw "Generated persona is missing ownerDisplayName."
}

if ([string]::IsNullOrWhiteSpace($persona.personaSummary)) {
    throw "Generated persona is missing personaSummary."
}

function Format-BulletSection {
    param(
        [object]$Items,
        [string]$Fallback
    )

    $values = @($Items | ForEach-Object { "$_".Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($values.Count -eq 0) {
        return "- $Fallback"
    }

    return [string]::Join("`n", ($values | ForEach-Object { "- $_" }))
}

$personaNarrative = if ([string]::IsNullOrWhiteSpace($persona.personaNarrative)) {
    $persona.personaSummary
}
else {
    $persona.personaNarrative
}

$skillBody = @"
# communication-twin

Use this skill to emulate the operating user's authentic communication style with high fidelity.

owner: $($persona.ownerDisplayName)
tone: $($persona.tone)

persona summary:
$($persona.personaSummary)

persona narrative:
$personaNarrative

preferred phrases:
$(Format-BulletSection -Items $persona.preferredPhrases -Fallback "Use direct, practical phrasing that lowers friction for next steps.")

avoid phrases:
$(Format-BulletSection -Items $persona.avoidPhrases -Fallback "Avoid vague or non-committal phrasing.")

communication principles:
$(Format-BulletSection -Items $persona.communicationPrinciples -Fallback "Prioritize clarity, accountability, and concrete next actions.")

response style guidelines:
$(Format-BulletSection -Items $persona.responseStyleGuidelines -Fallback "Front-load decisions, owners, and dates when relevant.")

relationship signals:
$(Format-BulletSection -Items $persona.relationshipSignals -Fallback "Acknowledge context quickly, then move to action.")

escalation boundaries:
$(Format-BulletSection -Items $persona.escalationBoundaries -Fallback "Do not imply commitments or approvals that were not explicitly granted.")

response rules:
- mirror the owner's voice with specificity rather than generic corporate phrasing.
- keep replies practical and decision-oriented, with clear owners and next steps when appropriate.
- do not invent facts, commitments, or approvals.
- use available MCP context when it materially improves response quality.
"@

Set-Content -Path $communicationTwinSkillPath -Value $skillBody -Encoding utf8

Write-Host "Communication twin JSON created at: $communicationTwinPath"
Write-Host "Communication twin skill created at: $communicationTwinSkillPath"
Write-Host "Prompt source: $promptPath"
Write-Host "Generated runtime twin artifacts are under src/MessageScreener.Api/copilot-config."

if (-not $SkipCopilotRuntimeHook) {
    Write-Host "Configuring runtime Copilot extension hook..."
    & (Join-Path $PSScriptRoot "setup-copilot-runtime.ps1") -Force:$Force
}

Write-Host "Next step: dotnet build MessageScreener.slnx -warnaserror"
