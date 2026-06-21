using System;
using System.Text.Json;
using FluentAssertions;
using Ows.Core.Education;
using Ows.Core.Notarization;
using Ows.Core.Verification;
using Xunit;

namespace Ows.Core.Tests;

/// <summary>
/// Verifies initialization, constructor validations, and JSON serialization of education domain models.
/// </summary>
public sealed class EducationDomainTests {
    private readonly DateTimeOffset _testTime = DateTimeOffset.Parse("2026-06-20T00:00:00Z");

    [Fact]
    public void Institution_ShouldInitializeAndValidateCorrectly() {
        var id = InstitutionId.Create();
        var name = "University of Science";
        var slug = "uni-science";

        // Success Path
        var inst = new Institution(id, name, slug, _testTime);
        inst.Id.Should().Be(id);
        inst.Name.Should().Be(name);
        inst.Slug.Should().Be(slug);
        inst.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new Institution(new InstitutionId(""), name, slug, _testTime));
        Assert.Throws<ArgumentException>(() => new Institution(id, " ", slug, _testTime));
        Assert.Throws<ArgumentException>(() => new Institution(id, name, "", _testTime));
        Assert.Throws<ArgumentException>(() => new Institution(id, name, slug, default));
    }

    [Fact]
    public void Course_ShouldInitializeAndValidateCorrectly() {
        var id = CourseId.Create();
        var instId = InstitutionId.Create();
        var code = "CS301";
        var title = "Distributed Systems";

        // Success Path
        var course = new Course(id, instId, code, title, _testTime);
        course.Id.Should().Be(id);
        course.InstitutionId.Should().Be(instId);
        course.Code.Should().Be(code);
        course.Title.Should().Be(title);
        course.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new Course(new CourseId(""), instId, code, title, _testTime));
        Assert.Throws<ArgumentException>(() => new Course(id, new InstitutionId(""), code, title, _testTime));
        Assert.Throws<ArgumentException>(() => new Course(id, instId, "   ", title, _testTime));
        Assert.Throws<ArgumentException>(() => new Course(id, instId, code, "", _testTime));
        Assert.Throws<ArgumentException>(() => new Course(id, instId, code, title, default));
    }

    [Fact]
    public void ClassGroup_ShouldInitializeAndValidateCorrectly() {
        var id = ClassGroupId.Create();
        var instId = InstitutionId.Create();
        var name = "CS Section A";

        // Success Path
        var group = new ClassGroup(id, instId, name, _testTime);
        group.Id.Should().Be(id);
        group.InstitutionId.Should().Be(instId);
        group.Name.Should().Be(name);
        group.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new ClassGroup(new ClassGroupId(""), instId, name, _testTime));
        Assert.Throws<ArgumentException>(() => new ClassGroup(id, new InstitutionId(""), name, _testTime));
        Assert.Throws<ArgumentException>(() => new ClassGroup(id, instId, "", _testTime));
        Assert.Throws<ArgumentException>(() => new ClassGroup(id, instId, name, default));
    }

    [Fact]
    public void CourseOffering_ShouldInitializeAndValidateCorrectly() {
        var id = CourseOfferingId.Create();
        var instId = InstitutionId.Create();
        var courseId = CourseId.Create();
        var groupId = ClassGroupId.Create();
        var term = "Spring";
        var year = 2026;

        // Success Path
        var offering = new CourseOffering(id, instId, courseId, groupId, term, year, _testTime);
        offering.Id.Should().Be(id);
        offering.InstitutionId.Should().Be(instId);
        offering.CourseId.Should().Be(courseId);
        offering.ClassGroupId.Should().Be(groupId);
        offering.Term.Should().Be(term);
        offering.Year.Should().Be(year);
        offering.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new CourseOffering(new CourseOfferingId(""), instId, courseId, groupId, term, year, _testTime));
        Assert.Throws<ArgumentException>(() => new CourseOffering(id, instId, courseId, groupId, "", year, _testTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CourseOffering(id, instId, courseId, groupId, term, -1, _testTime));
        Assert.Throws<ArgumentException>(() => new CourseOffering(id, instId, courseId, groupId, term, year, default));
    }

    [Fact]
    public void User_ShouldInitializeAndValidateCorrectly() {
        var id = UserId.Create();
        var instId = InstitutionId.Create();
        var name = "Alice Student";

        // Success Path
        var user = new User(id, instId, name, "ext-123", "alice@school.edu", _testTime);
        user.Id.Should().Be(id);
        user.InstitutionId.Should().Be(instId);
        user.DisplayName.Should().Be(name);
        user.ExternalId.Should().Be("ext-123");
        user.Email.Should().Be("alice@school.edu");
        user.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new User(new UserId(""), instId, name, null, null, _testTime));
        Assert.Throws<ArgumentException>(() => new User(id, instId, "   ", null, null, _testTime));
        Assert.Throws<ArgumentException>(() => new User(id, instId, name, null, null, default));
    }

    [Fact]
    public void StudentEnrollment_ShouldInitializeAndValidateCorrectly() {
        var id = EnrollmentId.Create();
        var offeringId = CourseOfferingId.Create();
        var studentUserId = UserId.Create();

        // Success Path
        var studentEnrollment = new StudentEnrollment(id, offeringId, studentUserId, _testTime);
        studentEnrollment.Id.Should().Be(id);
        studentEnrollment.CourseOfferingId.Should().Be(offeringId);
        studentEnrollment.StudentUserId.Should().Be(studentUserId);
        studentEnrollment.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new StudentEnrollment(new EnrollmentId(""), offeringId, studentUserId, _testTime));
        Assert.Throws<ArgumentException>(() => new StudentEnrollment(id, offeringId, studentUserId, default));
    }

    [Fact]
    public void Assessment_ShouldInitializeAndValidateCorrectly() {
        var id = AssessmentId.Create();
        var instId = InstitutionId.Create();
        var offeringId = CourseOfferingId.Create();
        var title = "Lab 1: Consensus";
        var policyId = PolicyId.Create();

        // Success Path
        var assessment = new Assessment(id, instId, offeringId, title, _testTime, _testTime.AddDays(7), policyId, _testTime);
        assessment.Id.Should().Be(id);
        assessment.InstitutionId.Should().Be(instId);
        assessment.CourseOfferingId.Should().Be(offeringId);
        assessment.Title.Should().Be(title);
        assessment.StartsAt.Should().Be(_testTime);
        assessment.EndsAt.Should().Be(_testTime.AddDays(7));
        assessment.PolicyId.Should().Be(policyId);
        assessment.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new Assessment(new AssessmentId(""), instId, offeringId, title, null, null, null, _testTime));
        Assert.Throws<ArgumentException>(() => new Assessment(id, instId, offeringId, "", null, null, null, _testTime));
        Assert.Throws<ArgumentException>(() => new Assessment(id, instId, offeringId, title, null, null, null, default));
    }

    [Fact]
    public void AssessmentPolicy_ShouldInitializeAndValidateCorrectly() {
        var id = PolicyId.Create();
        var instId = InstitutionId.Create();
        var name = "Strict Watcher Policy";

        // Success Path
        var policy = new AssessmentPolicy(id, instId, name, 30, 10, 300, true, true, _testTime);
        policy.Id.Should().Be(id);
        policy.InstitutionId.Should().Be(instId);
        policy.Name.Should().Be(name);
        policy.HeartbeatTargetSeconds.Should().Be(30);
        policy.HeartbeatGraceSeconds.Should().Be(10);
        policy.SignificantGapSeconds.Should().Be(300);
        policy.RequireRemoteReceipts.Should().BeTrue();
        policy.RequirePackageAnchor.Should().BeTrue();
        policy.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new AssessmentPolicy(new PolicyId(""), instId, name, 30, 10, 300, true, true, _testTime));
        Assert.Throws<ArgumentException>(() => new AssessmentPolicy(id, instId, "   ", 30, 10, 300, true, true, _testTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentPolicy(id, instId, name, -1, 10, 300, true, true, _testTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentPolicy(id, instId, name, 30, -5, 300, true, true, _testTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentPolicy(id, instId, name, 30, 10, -100, true, true, _testTime));
        Assert.Throws<ArgumentException>(() => new AssessmentPolicy(id, instId, name, 30, 10, 300, true, true, default));
    }

    [Fact]
    public void AssessmentSession_ShouldInitializeAndValidateCorrectly() {
        var sessionId = AssessmentSessionId.Create();
        var assessmentId = AssessmentId.Create();
        var studentId = UserId.Create();
        var packageId = "pkg-hash-value";

        // Success Path
        var session = new AssessmentSession(sessionId, assessmentId, studentId, packageId, TrustStatus.Verified, _testTime);
        session.Id.Should().Be(sessionId);
        session.AssessmentId.Should().Be(assessmentId);
        session.StudentUserId.Should().Be(studentId);
        session.PackageId.Should().Be(packageId);
        session.TrustStatus.Should().Be(TrustStatus.Verified);
        session.CreatedAt.Should().Be(_testTime);

        // Validation Failures
        Assert.Throws<ArgumentException>(() => new AssessmentSession(new AssessmentSessionId(""), assessmentId, studentId, null, TrustStatus.Unverified, _testTime));
        Assert.Throws<ArgumentException>(() => new AssessmentSession(sessionId, new AssessmentId(""), studentId, null, TrustStatus.Unverified, _testTime));
        Assert.Throws<ArgumentException>(() => new AssessmentSession(sessionId, assessmentId, studentId, null, TrustStatus.Unverified, default));
    }

    [Fact]
    public void Models_ShouldSerializeAndDeserializeJsonCorrectly() {
        var inst = new Institution(InstitutionId.Create(), "Uni Test", "uni-test", _testTime);
        var json = JsonSerializer.Serialize(inst);
        var deserialized = JsonSerializer.Deserialize<Institution>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Value.Should().Be(inst.Id.Value);
        deserialized.Name.Should().Be(inst.Name);
        deserialized.Slug.Should().Be(inst.Slug);
        deserialized.CreatedAt.Should().Be(inst.CreatedAt);

        var policy = new AssessmentPolicy(PolicyId.Create(), inst.Id, "Policy 1", 60, 20, 180, false, true, _testTime);
        var policyJson = JsonSerializer.Serialize(policy);
        var deserializedPolicy = JsonSerializer.Deserialize<AssessmentPolicy>(policyJson);

        deserializedPolicy.Should().NotBeNull();
        deserializedPolicy!.Id.Value.Should().Be(policy.Id.Value);
        deserializedPolicy.InstitutionId.Value.Should().Be(policy.InstitutionId.Value);
        deserializedPolicy.Name.Should().Be(policy.Name);
        deserializedPolicy.HeartbeatTargetSeconds.Should().Be(policy.HeartbeatTargetSeconds);
        deserializedPolicy.RequireRemoteReceipts.Should().BeFalse();
        deserializedPolicy.RequirePackageAnchor.Should().BeTrue();
    }
}
