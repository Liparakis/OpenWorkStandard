param(
    [string]$BaseUrl = $(if ($env:OWS_VERIFIER_BASE_URL) { $env:OWS_VERIFIER_BASE_URL } else { "http://127.0.0.1:5078" }),
    [string]$OperatorKey = $env:OWS_VERIFIER_API_KEY,
    [string]$Prefix = "pilot"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OperatorKey)) {
    throw "Operator key required. Pass -OperatorKey or set OWS_VERIFIER_API_KEY."
}

$BaseUrl = $BaseUrl.TrimEnd("/")
$now = (Get-Date).ToUniversalTime().ToString("o")
$headers = @{ "X-OWS-Verifier-Key" = $OperatorKey }

function New-Id([string]$Name) {
    return "$Prefix-$Name"
}

function Invoke-OwsJson([string]$Method, [string]$Path, [object]$Body, [hashtable]$HeadersOverride = $headers) {
    $json = $Body | ConvertTo-Json -Depth 10
    return Invoke-RestMethod -Method $Method -Uri "$BaseUrl$Path" -Headers $HeadersOverride -ContentType "application/json" -Body $json
}

function New-WrappedId([string]$Value) {
    return @{ value = $Value }
}

$institutionId = New-Id "institution"
$courseId = New-Id "course"
$classGroupId = New-Id "class"
$courseOfferingId = New-Id "offering"
$studentUserId = New-Id "student"
$enrollmentId = New-Id "enrollment"
$assessmentId = New-Id "assessment"

Invoke-OwsJson POST "/education/institutions" @{
    id = New-WrappedId $institutionId
    name = "OWS Pilot Institution"
    slug = $institutionId
    createdAt = $now
} | Out-Null

Invoke-OwsJson POST "/education/courses" @{
    id = New-WrappedId $courseId
    institutionId = New-WrappedId $institutionId
    code = "OWS101"
    title = "Open Work Standard Pilot"
    createdAt = $now
} | Out-Null

Invoke-OwsJson POST "/education/class-groups" @{
    id = New-WrappedId $classGroupId
    institutionId = New-WrappedId $institutionId
    name = "Pilot Group A"
    createdAt = $now
} | Out-Null

Invoke-OwsJson POST "/education/users" @{
    id = New-WrappedId $studentUserId
    institutionId = New-WrappedId $institutionId
    displayName = "Pilot Student"
    externalId = $studentUserId
    email = "pilot.student@example.edu"
    createdAt = $now
} | Out-Null

Invoke-OwsJson POST "/education/course-offerings" @{
    id = New-WrappedId $courseOfferingId
    institutionId = New-WrappedId $institutionId
    courseId = New-WrappedId $courseId
    classGroupId = New-WrappedId $classGroupId
    term = "Pilot"
    year = [int](Get-Date).Year
    createdAt = $now
} | Out-Null

Invoke-OwsJson POST "/education/enrollments" @{
    id = New-WrappedId $enrollmentId
    courseOfferingId = New-WrappedId $courseOfferingId
    userId = New-WrappedId $studentUserId
    role = 0
    createdAt = $now
} | Out-Null

Invoke-OwsJson POST "/education/assessments" @{
    id = New-WrappedId $assessmentId
    institutionId = New-WrappedId $institutionId
    courseOfferingId = New-WrappedId $courseOfferingId
    title = "Pilot Assignment"
    startsAt = $null
    endsAt = $null
    policyId = $null
    createdAt = $now
} | Out-Null

$studentKeyResult = Invoke-OwsJson POST "/auth/api-keys" @{
    role = "StudentClient"
    institutionId = $institutionId
    studentUserId = $studentUserId
}

$reviewerKeyResult = Invoke-OwsJson POST "/auth/api-keys" @{
    role = "InstructorReviewer"
    institutionId = $institutionId
}

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

$artifactDir = Join-Path (Get-Location) "artifacts\pilot-demo"
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$metadataPath = Join-Path $artifactDir "fixture-metadata.json"
$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path $metadataPath -Encoding UTF8

Write-Host "Pilot fixture created."
Write-Host "Metadata file: $metadataPath"
Write-Host ""
Write-Host "institutionId=$institutionId"
Write-Host "courseId=$courseId"
Write-Host "courseOfferingId=$courseOfferingId"
Write-Host "assessmentId=$assessmentId"
Write-Host "studentUserId=$studentUserId"
Write-Host ""
Write-Host "StudentClient key (shown once): $($studentKeyResult.apiKey)"
Write-Host "InstructorReviewer key (shown once): $($reviewerKeyResult.apiKey)"
