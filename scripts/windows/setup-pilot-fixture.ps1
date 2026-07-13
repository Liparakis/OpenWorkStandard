param(
    [string]$BaseUrl = $(if ($env:OWS_VERIFIER_BASE_URL) { $env:OWS_VERIFIER_BASE_URL } else { "http://127.0.0.1:5078" }),
    [string]$OperatorKey = $env:OWS_VERIFIER_API_KEY,
    [string]$Prefix = "pilot",
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($OperatorKey)) { throw "Operator key required. Pass -OperatorKey or set OWS_VERIFIER_API_KEY." }
$BaseUrl = $BaseUrl.TrimEnd("/")
$headers = @{ "X-OWS-Verifier-Key" = $OperatorKey }
function New-Id([string]$Name) { return "$Prefix-$Name" }
function Invoke-OwsJson([string]$Path, [object]$Body) {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl$Path" -Headers $headers -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 5)
}

$institutionId = New-Id "institution"
$courseId = New-Id "course"
$classGroupId = New-Id "class"
$courseOfferingId = New-Id "offering"
$studentUserId = New-Id "student"
$enrollmentId = New-Id "enrollment"
$assessmentId = New-Id "assessment"
$studentKeyResult = Invoke-OwsJson "/auth/api-keys" @{ role = "StudentClient"; institutionId = $institutionId; studentUserId = $studentUserId }
$reviewerKeyResult = Invoke-OwsJson "/auth/api-keys" @{ role = "InstructorReviewer"; institutionId = $institutionId }

$artifactDir = Join-Path (Get-Location) "artifacts\pilot-demo"
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$metadata = [ordered]@{
    baseUrl = $BaseUrl
    institutionId = $institutionId
    courseId = $courseId
    classGroupId = $classGroupId
    courseOfferingId = $courseOfferingId
    enrollmentId = $enrollmentId
    assessmentId = $assessmentId
    studentUserId = $studentUserId
    studentClientKeyPrefix = $studentKeyResult.metadata.keyPrefix
    instructorReviewerKeyPrefix = $reviewerKeyResult.metadata.keyPrefix
}
$metadataPath = Join-Path $artifactDir "fixture-metadata.json"
$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path $metadataPath -Encoding UTF8
$result = [ordered]@{
    metadataFile = $metadataPath
    baseUrl = $BaseUrl
    institutionId = $institutionId
    courseId = $courseId
    classGroupId = $classGroupId
    courseOfferingId = $courseOfferingId
    enrollmentId = $enrollmentId
    assessmentId = $assessmentId
    studentUserId = $studentUserId
    studentClientKey = $studentKeyResult.apiKey
    studentClientKeyPrefix = $studentKeyResult.metadata.keyPrefix
    instructorReviewerKey = $reviewerKeyResult.apiKey
    instructorReviewerKeyPrefix = $reviewerKeyResult.metadata.keyPrefix
}
if ($AsJson) { $result | ConvertTo-Json -Depth 5; return }
Write-Host "Pilot metadata and verifier keys created (no management records are stored by OWS)."
Write-Host "Metadata file: $metadataPath"
Write-Host "StudentClient key (shown once): $($studentKeyResult.apiKey)"
Write-Host "InstructorReviewer key (shown once): $($reviewerKeyResult.apiKey)"
