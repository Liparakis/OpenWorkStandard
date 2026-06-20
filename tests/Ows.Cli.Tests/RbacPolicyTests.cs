using FluentAssertions;
using Ows.Verifier.Server;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the role policy normalization and request validation for InstitutionAdmin.
/// </summary>
public sealed class RbacPolicyTests
{
    [Fact]
    public void IsSupportedRole_ShouldRecognizeInstitutionAdmin()
    {
        VerifierRolePolicy.IsSupportedRole("InstitutionAdmin").Should().BeTrue();
        VerifierRolePolicy.IsSupportedRole("institutionadmin").Should().BeTrue();
        VerifierRolePolicy.IsSupportedRole("admin").Should().BeTrue();
    }

    [Fact]
    public void NormalizeRoleName_ShouldAcceptVariants()
    {
        VerifierRolePolicy.NormalizeRoleName("institutionadmin").Should().Be(VerifierRolePolicy.InstitutionAdmin);
        VerifierRolePolicy.NormalizeRoleName("admin").Should().Be(VerifierRolePolicy.InstitutionAdmin);
        VerifierRolePolicy.NormalizeRoleName("reviewer").Should().Be(VerifierRolePolicy.InstructorReviewer);
        VerifierRolePolicy.NormalizeRoleName("instructorreviewer").Should().Be(VerifierRolePolicy.InstructorReviewer);
        VerifierRolePolicy.NormalizeRoleName("operator").Should().Be(VerifierRolePolicy.Operator);
    }

    [Fact]
    public void CreateRequest_RequiresInstitutionId_ForInstitutionAdminAndReviewer()
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

        VerifierRolePolicy.IsInstitutionScopedRole("InstitutionAdmin").Should().BeTrue();
        VerifierRolePolicy.IsInstitutionScopedRole("InstructorReviewer").Should().BeTrue();
        VerifierRolePolicy.IsInstitutionScopedRole("Operator").Should().BeFalse();
    }
}
