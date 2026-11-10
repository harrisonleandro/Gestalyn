using ProjectManager.Domain.Organizations;

namespace ProjectManager.UnitTests.Domain;

public sealed class OrganizationTests
{
    [Fact]
    public void Constructor_TrimsNameAndAssignsVersion7Id()
    {
        var organization = new Organization("  RiukDev  ");

        Assert.Equal("RiukDev", organization.Name);
        Assert.Equal(7, organization.Id.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsMissingName(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Organization(name!));
    }

    [Fact]
    public void Constructor_RejectsNameAboveMaxLength()
    {
        var name = new string('a', Organization.NameMaxLength + 1);

        Assert.Throws<ArgumentException>(() => new Organization(name));
    }

    [Fact]
    public void Rename_ValidatesNewName()
    {
        var organization = new Organization("Original");

        organization.Rename("Renomeada");

        Assert.Equal("Renomeada", organization.Name);
        Assert.ThrowsAny<ArgumentException>(() => organization.Rename(" "));
        Assert.Equal("Renomeada", organization.Name);
    }
}
