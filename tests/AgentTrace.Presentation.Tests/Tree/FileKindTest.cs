using AgentTrace.Presentation.Tree;

namespace AgentTrace.Presentation.Tests.Tree;

public sealed class FileKindTest
{
    [Fact]
    public void ClassifiesByExtensionIgnoringCase()
    {
        Assert.Equal(FileKind.Code, FileKinds.Of("Main.java"));
        Assert.Equal(FileKind.Code, FileKinds.Of("SCRIPT.PY"));
        Assert.Equal(FileKind.Config, FileKinds.Of("application.yml"));
        Assert.Equal(FileKind.Markup, FileKinds.Of("README.md"));
        Assert.Equal(FileKind.Image, FileKinds.Of("logo.PNG"));
        Assert.Equal(FileKind.Archive, FileKinds.Of("app.jar"));
        Assert.Equal(FileKind.Binary, FileKinds.Of("Clase.class"));
        Assert.Equal(FileKind.Pdf, FileKinds.Of("manual.pdf"));
    }

    [Fact]
    public void WellKnownNamesWinOverTheirExtension()
    {
        Assert.Equal(FileKind.Config, FileKinds.Of("pom.xml"));
        Assert.Equal(FileKind.Config, FileKinds.Of(".gitignore"));
        Assert.Equal(FileKind.Config, FileKinds.Of("Dockerfile"));
        Assert.Equal(FileKind.Config, FileKinds.Of("Makefile"));
    }

    [Fact]
    public void UnknownOrMissingExtensionsAreText()
    {
        Assert.Equal(FileKind.Text, FileKinds.Of("notas.txt"));
        Assert.Equal(FileKind.Text, FileKinds.Of("LICENSE"));
        Assert.Equal(FileKind.Text, FileKinds.Of("file.unknown"));
        Assert.Equal(FileKind.Text, FileKinds.Of("sinextension"));
        Assert.Equal(FileKind.Text, FileKinds.Of("ends.with.dot."));
    }

    [Fact]
    public void OnlyTheLastExtensionCounts()
    {
        Assert.Equal(FileKind.Archive, FileKinds.Of("copia.tar.gz"));
        Assert.Equal(FileKind.Code, FileKinds.Of("test.min.js"));
    }
}
