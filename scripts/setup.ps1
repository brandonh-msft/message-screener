param(
    [string]$CopilotCliPath = "copilot",
    [string]$CopilotCommand = "chat",
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

Use WorkIQ to analyze the user's recent Teams messages and emails.
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
    & $CopilotCliPath $CopilotCommand --prompt-file $promptPath --output-file $communicationTwinPath
}
catch {
    throw "Copilot CLI persona generation failed. Ensure Copilot CLI is authenticated and WorkIQ is available, then retry. Inner error: $($_.Exception.Message)"
}

if (-not (Test-Path $communicationTwinPath)) {
    throw "Persona generation did not produce $communicationTwinPath"
}

$rawPersona = Get-Content -Path $communicationTwinPath -Raw
$persona = $rawPersona | ConvertFrom-Json

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
