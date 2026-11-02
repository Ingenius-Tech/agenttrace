using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Git;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Text;

public sealed class ReasonTextTest
{
    [Fact]
    public void EveryUnavailableReasonHasATextInBothLanguages()
    {
        foreach (UnavailableReason reason in Enum.GetValues<UnavailableReason>())
        {
            string en = ReasonText.Describe(reason);
            using var _ = CultureScope.Spanish();
            string es = ReasonText.Describe(reason);
            Assert.False(string.IsNullOrWhiteSpace(en), reason + " in English");
            Assert.False(string.IsNullOrWhiteSpace(es), reason + " in Spanish");
            Assert.NotEqual(en, es);
        }
    }

    [Fact]
    public void ReasonsKeepTheOldSpanishWordsUnderEs()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("binario", ReasonText.Describe(UnavailableReason.Binary)); // lang-ok
        Assert.Equal("demasiado grande", ReasonText.Describe(UnavailableReason.TooLarge)); // lang-ok
        Assert.Equal("sin línea base", ReasonText.Describe(UnavailableReason.NoBaseline)); // lang-ok
        Assert.Equal("submódulo o carpeta", ReasonText.Describe(UnavailableReason.SubmoduleOrFolder)); // lang-ok
        Assert.Equal("sin git: no hay línea base con la que comparar", ReasonText.Describe(UnavailableReason.NoRepository)); // lang-ok
    }

    [Fact]
    public void ReasonsAreEnglishByDefault()
    {
        Assert.Equal("binary", ReasonText.Describe(UnavailableReason.Binary));
        Assert.Equal("too large", ReasonText.Describe(UnavailableReason.TooLarge));
    }

    [Fact]
    public void ContentNotesCarryTheirDetailInTheRightLanguage()
    {
        Assert.Equal("The file no longer exists", ReasonText.Describe(ContentNote.Missing, null));
        Assert.Equal("Cannot read it: boom", ReasonText.Describe(ContentNote.CannotRead, "boom"));
        Assert.Equal("Cannot read it right now: locked", ReasonText.Describe(ContentNote.CannotReadNow, "locked"));
        using var _ = CultureScope.Spanish();
        Assert.Equal("El archivo ya no existe", ReasonText.Describe(ContentNote.Missing, null)); // lang-ok
        Assert.Equal("Es una carpeta", ReasonText.Describe(ContentNote.IsFolder, null)); // lang-ok
        Assert.Equal("No se puede leer ahora: locked", ReasonText.Describe(ContentNote.CannotReadNow, "locked")); // lang-ok
        Assert.Equal("El archivo no es UTF-8: se muestra como ISO-8859-1", ReasonText.Describe(ContentNote.NotUtf8, null)); // lang-ok
    }

    [Fact]
    public void EveryContentNoteHasText()
    {
        foreach (ContentNote note in Enum.GetValues<ContentNote>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ReasonText.Describe(note, "detail")), note.ToString());
        }
    }

    [Fact]
    public void OnlyTheBomLabelIsTranslated()
    {
        Assert.Equal("UTF-8 (with BOM)", ReasonText.Encoding(FileContentReader.Utf8BomLabel));
        Assert.Equal("ISO-8859-1", ReasonText.Encoding("ISO-8859-1"));
        using var _ = CultureScope.Spanish();
        Assert.Equal("UTF-8 (con BOM)", ReasonText.Encoding(FileContentReader.Utf8BomLabel)); // lang-ok
    }

    [Fact]
    public void TheMainLaneIsCalledMainAgent()
    {
        var main = new FlowGraph.Lane(FlowGraph.MainLaneId, null, null, null, null, -1);
        var sub = new FlowGraph.Lane("s1", "s1", "call-1", "research", null, 0);
        Assert.Equal("Main agent", ReasonText.LaneTitle(main));
        Assert.Equal("research", ReasonText.LaneTitle(sub));
        using var _ = CultureScope.Spanish();
        Assert.Equal("Agente principal", ReasonText.LaneTitle(main)); // lang-ok
    }

    [Fact]
    public void DubiousOwnershipGetsAnActionableHintAndOtherErrorsKeepTheirMessage()
    {
        var dubious = new GitException("status", 128, "fatal: detected dubious ownership in repository at 'C:/x'");
        Assert.Contains("safe.directory", ReasonText.ErrorMessage(dubious));
        Assert.Contains("does not trust", ReasonText.ErrorMessage(dubious));
        var other = new GitException("status", 1, "boom");
        Assert.Equal(other.Message, ReasonText.ErrorMessage(other));
        Assert.Equal("plain", ReasonText.ErrorMessage(new IOException("plain")));
        using var _ = CultureScope.Spanish();
        Assert.Contains("no confía", ReasonText.ErrorMessage(dubious)); // lang-ok
    }

    [Fact]
    public void TheSpanishSatelliteIsLoadedFromTheResourceManager()
    {
        Assert.Equal("Close", CommonStrings.Close);
        using var _ = CultureScope.Spanish();
        Assert.Equal("Cerrar", CommonStrings.Close); // lang-ok
    }
}
