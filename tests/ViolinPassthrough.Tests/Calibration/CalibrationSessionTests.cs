using ViolinPassthrough.Calibration;

namespace ViolinPassthrough.Tests.Calibration;

public class CalibrationSessionTests
{
    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void CountsDownEverySecondThenCaptures()
    {
        var session = new CalibrationSession(_time);
        session.Start(3);
        var steps = new List<CalibrationStep>();

        for (var i = 0; i < 40; i++)
        {
            var step = session.Update();
            if (step.Kind != CalibrationStepKind.None)
            {
                steps.Add(step);
            }

            _time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(
            new[]
            {
                new CalibrationStep(CalibrationStepKind.Tick, 3),
                new CalibrationStep(CalibrationStepKind.Tick, 2),
                new CalibrationStep(CalibrationStepKind.Tick, 1),
                new CalibrationStep(CalibrationStepKind.Capture),
            },
            steps);
        Assert.False(session.Active);
    }

    [Fact]
    public void Cancel_StopsTheCountdown()
    {
        var session = new CalibrationSession(_time);
        session.Start(5);
        session.Update();

        session.Cancel();
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.False(session.Active);
        Assert.Equal(CalibrationStepKind.None, session.Update().Kind);
    }

    [Fact]
    public void Restart_BeginsAgain()
    {
        var session = new CalibrationSession(_time);
        session.Start(5);
        _time.Advance(TimeSpan.FromSeconds(4.5));
        session.Update();

        session.Start(5);

        Assert.Equal(new CalibrationStep(CalibrationStepKind.Tick, 5), session.Update());
    }
}
