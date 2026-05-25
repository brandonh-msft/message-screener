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

$runtimeConfigDirectory = Join-Path $repoRoot "src/MessageScreener.Api/config"
$communicationTwinPath = Join-Path $runtimeConfigDirectory "communication-twin.json"
$communicationTwinSkillPath = Join-Path $runtimeConfigDirectory "communication-twin.skill.md"
$promptsDirectory = Join-Path $repoRoot ".github/prompts"
$promptPath = Join-Path $promptsDirectory "communication-twin.workiq.prompt.md"

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

if (-not (Test-Path $promptsDirectory)) {
    New-Item -Path $promptsDirectory -ItemType Directory | Out-Null
}

if ((Test-Path $communicationTwinPath) -and -not $Force) {
    Write-Host "Communication twin already exists at $communicationTwinPath"
    Write-Host "Use -Force to overwrite it."
    exit 0
}

$prompt = @"
You are generating a communication twin profile for Message Screener.

Focus only on communication style. Use WorkIQ to analyze the user's recent Teams messages and emails.
Also consider communication-related writing patterns visible in public code and docs from brandonh-msft and bc3tech repositories.
Do not create plugins, skills, agents, or implementation plans in this response.
Do not ask the user any persona questions.

Return STRICT JSON only using this exact shape:
{
  "ownerDisplayName": "string",
  "personaSummary": "string",
  "preferredPhrases": ["string", "string", "string"],
  "avoidPhrases": ["string", "string", "string"],
  "tone": "professional|friendly|direct|formal"
}

Constraints:
- Keep `personaSummary` to one sentence.
- Prefer concise, actionable phrasing from observed communication style.
- Avoid speculative details that are not supported by WorkIQ evidence.
- Ensure JSON is valid and contains all required fields.
"@

Set-Content -Path $promptPath -Value $prompt -Encoding utf8

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

$skillBody = @"
# communication-twin

owner: $($persona.ownerDisplayName)
tone: $($persona.tone)

persona summary:
$($persona.personaSummary)

preferred phrases:
$([string]::Join("`n", ($persona.preferredPhrases | ForEach-Object { "- $_" })))

avoid phrases:
$([string]::Join("`n", ($persona.avoidPhrases | ForEach-Object { "- $_" })))

response rules:
- mirror the owner's concise style while remaining professional.
- provide short actionable responses first, then optional detail.
- do not claim actions were taken unless explicitly approved by the owner.
"@

Set-Content -Path $communicationTwinSkillPath -Value $skillBody -Encoding utf8

Write-Host "Communication twin JSON created at: $communicationTwinPath"
Write-Host "Communication twin skill created at: $communicationTwinSkillPath"
Write-Host "These files are deployment-shipped runtime artifacts in a well-known path under src/MessageScreener.Api/config."

if (-not $SkipCopilotRuntimeHook) {
    Write-Host "Configuring runtime Copilot extension hook..."
    & (Join-Path $PSScriptRoot "setup-copilot-runtime.ps1") -Force:$Force
}

Write-Host "Next step: dotnet build MessageScreener.slnx -warnaserror"
