using DreamPovnota.Cards;

namespace DreamPovnota.Tests;

public class StageTests
{
    [Theory]
    [InlineData("pending:initiated", Stage.Idea)]
    [InlineData("pending:defined", Stage.Defined)]
    [InlineData("pending:appraised", Stage.Appraised)]
    [InlineData("active:approved", Stage.Implementation)]
    [InlineData("active", Stage.Implementation)]
    [InlineData("complete", Stage.Completed)]
    [InlineData("cancelled", Stage.Closed)]
    [InlineData("cancelled:duplicate", Stage.Closed)]
    [InlineData("unsuccessful:lackOfFunds", Stage.Closed)]
    [InlineData("pending", Stage.Unknown)]
    [InlineData("activeX", Stage.Unknown)]
    [InlineData("", Stage.Unknown)]
    [InlineData(null, Stage.Unknown)]
    public void Status_maps_to_stage(string? status, Stage expected)
    {
        Assert.Equal(expected, Stages.FromStatus(status));
    }

    [Theory]
    [InlineData(Stage.Idea, true)]
    [InlineData(Stage.Completed, true)]
    [InlineData(Stage.Closed, false)]
    [InlineData(Stage.Unknown, false)]
    public void Only_open_stages_take_part_in_checks(Stage stage, bool open)
    {
        Assert.Equal(open, stage.IsOpen());
    }
}
