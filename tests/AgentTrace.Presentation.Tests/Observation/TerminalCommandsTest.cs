using AgentTrace.Presentation.Observation;

namespace AgentTrace.Presentation.Tests.Observation;

public sealed class TerminalCommandsTest
{
    private static Func<string, bool> Only(params string[] available) => name => available.Contains(name);

    [Fact]
    public void WindowsPrefersWindowsTerminalAndFallsBackToCmd()
    {
        TerminalCommand wt = TerminalCommands.For(@"C:\p", true, false, Only("wt.exe"))!;
        Assert.Equal("wt.exe", wt.File);
        Assert.Equal(["-d", @"C:\p"], wt.Args);
        Assert.Equal("cmd.exe", TerminalCommands.For(@"C:\p", true, false, Only())!.File);
    }

    [Fact]
    public void MacOpensTerminalOnTheFolder()
    {
        TerminalCommand c = TerminalCommands.For("/p", false, true, Only())!;
        Assert.Equal("open", c.File);
        Assert.Equal(["-a", "Terminal", "/p"], c.Args);
    }

    [Fact]
    public void LinuxTakesTheFirstAvailableInOrderAndPassesTheFolder()
    {
        Assert.Equal("gnome-terminal", TerminalCommands.For("/p", false, false, Only("xterm", "gnome-terminal", "konsole"))!.File);
        TerminalCommand konsole = TerminalCommands.For("/p", false, false, Only("xterm", "konsole"))!;
        Assert.Equal(["--workdir", "/p"], konsole.Args);
        Assert.Empty(TerminalCommands.For("/p", false, false, Only("xterm"))!.Args);
        Assert.Null(TerminalCommands.For("/p", false, false, Only()));
    }

    [Fact]
    public void TheUsersChoiceWinsOnAnySystem()
    {
        TerminalCommand c = TerminalCommands.For("/p", true, false, Only("wt.exe"), " alacritty ")!;
        Assert.Equal("alacritty", c.File);
        Assert.Empty(c.Args);
    }
}
