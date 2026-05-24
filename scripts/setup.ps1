param(
    [string]$CopilotCliPath = "copilot",
    [string]$CopilotCommand = "chat",
    [string]$CopilotModel,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$communicationTwinDirectory = Join-Path $repoRoot ".message-screener"
$communicationTwinPath = Join-Path $communicationTwinDirectory "communication-twin.json"
$skillsDirectory = Join-Path $communicationTwinDirectory "skills"
$communicationTwinSkillPath = Join-Path $skillsDirectory "communication-twin.skill.md"
$promptsDirectory = Join-Path $communicationTwinDirectory "prompts"
$promptPath = Join-Path $promptsDirectory "communication-twin.workiq.prompt.md"

if (-not (Test-Path $communicationTwinDirectory)) {
    New-Item -Path $communicationTwinDirectory -ItemType Directory | Out-Null
}

if (-not (Test-Path $skillsDirectory)) {
    New-Item -Path $skillsDirectory -ItemType Directory | Out-Null
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

$copilotCommandInfo = Get-Command $CopilotCliPath -ErrorAction SilentlyContinue
if ($null -eq $copilotCommandInfo) {
    throw "Copilot CLI command '$CopilotCliPath' was not found. Install/configure GitHub Copilot CLI, then re-run setup."
}

Write-Host "Generating communication twin via Copilot CLI + WorkIQ..."

try {
    $promptText = Get-Content -Path $promptPath -Raw

    $copilotArgs = @(
        $CopilotCommand,
        "--prompt", $promptText,
        "--silent",
        "--output-format", "text"
    )

    if (-not [string]::IsNullOrWhiteSpace($CopilotModel)) {
        $copilotArgs += @("--model", $CopilotModel)
    }

    $copilotOutput = & $CopilotCliPath @copilotArgs

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
Write-Host "Next step: dotnet build MessageScreener.slnx -warnaserror"
