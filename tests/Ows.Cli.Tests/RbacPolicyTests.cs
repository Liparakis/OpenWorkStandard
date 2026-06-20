using FluentAssertions;
using Ows.Verifier.Server;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the role policy normalization and request validation for InstitutionAdmin and StudentClient.
/// </summary>
public sealed class RbacPolicyTests
{
    [Fact]
    public void IsSupportedRole_ShouldRecognizeInstitutionAdminAndStudentClient()
    {
        VerifierRolePolicy.IsSupportedRole("InstitutionAdmin").Should().BeTrue();
        VerifierRolePolicy.IsSupportedRole("institutionadmin").Should().BeTrue();
        VerifierRolePolicy.IsSupportedRole("admin").Should().BeTrue();
        VerifierRolePolicy.IsSupportedRole("StudentClient").Should().BeTrue();
        VerifierRolePolicy.IsSupportedRole("studentclient").Should().BeTrue();
        VerifierRolePolicy.IsSupportedRole("student").Should().BeTrue();
        VerifierRolePolicy.IsSupportedRole("client").Should().BeTrue();
    }

    [Fact]
    public void NormalizeRoleName_ShouldAcceptVariants()
    {
        VerifierRolePolicy.NormalizeRoleName("institutionadmin").Should().Be(VerifierRolePolicy.InstitutionAdmin);
        VerifierRolePolicy.NormalizeRoleName("admin").Should().Be(VerifierRolePolicy.InstitutionAdmin);
        VerifierRolePolicy.NormalizeRoleName("reviewer").Should().Be(VerifierRolePolicy.InstructorReviewer);
        VerifierRolePolicy.NormalizeRoleName("instructorreviewer").Should().Be(VerifierRolePolicy.InstructorReviewer);
        VerifierRolePolicy.NormalizeRoleName("operator").Should().Be(VerifierRolePolicy.Operator);
        VerifierRolePolicy.NormalizeRoleName("studentclient").Should().Be(VerifierRolePolicy.StudentClient);
        VerifierRolePolicy.NormalizeRoleName("student").Should().Be(VerifierRolePolicy.StudentClient);
        VerifierRolePolicy.NormalizeRoleName("client").Should().Be(VerifierRolePolicy.StudentClient);
    }

    [Fact]
    public void CreateRequest_RequiresInstitutionId_ForInstitutionAdminReviewerAndStudentClient()
    {
        var adminRequest = new VerifierApiKeyCreateRequest
        {
            Role = "InstitutionAdmin",
            InstitutionId = " "
        };
        adminRequest.GetValidationError().Should().Contain("InstitutionId is required");

        var reviewerRequest = new VerifierApiKeyCreateRequest
        {
            Role = "InstructorReviewer",
            InstitutionId = ""
        };
        reviewerRequest.GetValidationError().Should().Contain("InstitutionId is required");

        var studentRequest = new VerifierApiKeyCreateRequest
        {
            Role = "StudentClient",
            InstitutionId = "   "
        };
        studentRequest.GetValidationError().Should().Contain("InstitutionId is required");
    }

    [Fact]
    public void CreateRequest_Succeeds_WhenInstitutionIdProvided()
    {
        var adminRequest = new VerifierApiKeyCreateRequest
        {
            Role = "InstitutionAdmin",
            InstitutionId = "inst-1"
        };
        adminRequest.GetValidationError().Should().BeNull();

        var reviewerRequest = new VerifierApiKeyCreateRequest
        {
            Role = "InstructorReviewer",
            InstitutionId = "inst-1"
        };
        reviewerRequest.GetValidationError().Should().BeNull();

        var studentRequest = new VerifierApiKeyCreateRequest
        {
            Role = "StudentClient",
            InstitutionId = "inst-1"
        };
        studentRequest.GetValidationError().Should().BeNull();
    }

    [Fact]
    public void CreateRequest_Succeeds_WithoutInstitutionId_ForOperator()
    {
        var operatorRequest = new VerifierApiKeyCreateRequest
        {
            Role = "Operator",
            InstitutionId = null
        };
        operatorRequest.GetValidationError().Should().BeNull();
    }

    [Fact]
    public void Predicates_ShouldEvaluateCorrectly()
    {
        VerifierRolePolicy.IsOperatorRole("Operator").Should().BeTrue();
        VerifierRolePolicy.IsOperatorRole("operator").Should().BeTrue();
        VerifierRolePolicy.IsOperatorRole("InstitutionAdmin").Should().BeFalse();

        VerifierRolePolicy.IsInstitutionAdminRole("InstitutionAdmin").Should().BeTrue();
        VerifierRolePolicy.IsInstitutionAdminRole("admin").Should().BeTrue();
        VerifierRolePolicy.IsInstitutionAdminRole("Operator").Should().BeFalse();

        VerifierRolePolicy.IsInstructorReviewerRole("InstructorReviewer").Should().BeTrue();
        VerifierRolePolicy.IsInstructorReviewerRole("reviewer").Should().BeTrue();
        VerifierRolePolicy.IsInstructorReviewerRole("Operator").Should().BeFalse();

        VerifierRolePolicy.IsStudentClientRole("StudentClient").Should().BeTrue();
        VerifierRolePolicy.IsStudentClientRole("student").Should().BeTrue();
        VerifierRolePolicy.IsStudentClientRole("Operator").Should().BeFalse();

        VerifierRolePolicy.IsInstitutionScopedRole("InstitutionAdmin").Should().BeTrue();
        VerifierRolePolicy.IsInstitutionScopedRole("InstructorReviewer").Should().BeTrue();
        VerifierRolePolicy.IsInstitutionScopedRole("StudentClient").Should().BeTrue();
        VerifierRolePolicy.IsInstitutionScopedRole("Operator").Should().BeFalse();
    }
}
