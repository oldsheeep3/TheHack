namespace Switcher.Contracts;

/// <summary>
/// The colour each tally state is shown in — per bus, so PGM1 and PGM2 can be told apart at a glance.
///
/// One definition drives everything: the operator window's frames, the multiview cell borders and the
/// RGB LEDs on the physical modules. The controller does not carry its own palette; it is sent these
/// exact RGB values in the HID output report, so what the operator sees on screen and what the camera
/// operator sees on the panel can never disagree.
///
/// Values are plain 8-bit RGB (<see cref="BacklightColor"/>), which is what the module LEDs take.
/// </summary>
/// <param name="Pgm1">Live on program bus 1.</param>
/// <param name="Pgm2">Live on program bus 2.</param>
/// <param name="Pvw1">Staged on preview 1.</param>
/// <param name="Pvw2">Staged on preview 2.</param>
/// <param name="Idle">Bound to a source but neither live nor staged — "you can select this".</param>
public sealed record TallyColors(
    BacklightColor Pgm1,
    BacklightColor Pgm2,
    BacklightColor Pvw1,
    BacklightColor Pvw2,
    BacklightColor Idle)
{
    /// <summary>
    /// Broadcast convention as the starting point: program red, preview green. The second bus is offset
    /// in hue rather than given an unrelated colour, so it still reads as "program"/"preview" at a
    /// glance while remaining distinguishable from bus 1.
    /// </summary>
    public static TallyColors CreateDefault() => new(
        Pgm1: new BacklightColor(255, 0, 0),
        Pgm2: new BacklightColor(255, 96, 0),
        Pvw1: new BacklightColor(0, 255, 0),
        Pvw2: new BacklightColor(0, 190, 160),
        Idle: new BacklightColor(24, 24, 24));

    /// <summary>The program colour for <paramref name="bus"/>.</summary>
    public BacklightColor ProgramFor(ProgramBus bus) => bus == ProgramBus.Pgm2 ? Pgm2 : Pgm1;

    /// <summary>The preview colour for <paramref name="bus"/>.</summary>
    public BacklightColor PreviewFor(ProgramBus bus) => bus == ProgramBus.Pgm2 ? Pvw2 : Pvw1;
}
