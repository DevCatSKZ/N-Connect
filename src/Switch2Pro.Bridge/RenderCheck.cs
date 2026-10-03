using System.Drawing;
using System.Drawing.Imaging;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Prüfhilfe (<c>--render &lt;Ordner&gt;</c>): zeichnet jede Controller-Art einmal in Ruhe und einmal mit gedrückten
/// Tasten/ausgelenkten Sticks und speichert die Bilder als PNG. So lassen sich Grafiken ohne Hardware prüfen.
/// </summary>
internal static class RenderCheck
{
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var kinds = new (ControllerKind Kind, WiiExtension Ext, string Name)[]
        {
            (ControllerKind.Pro2, WiiExtension.None, "pro2"),
            (ControllerKind.GameCube2, WiiExtension.None, "gamecube"),
            (ControllerKind.SnesController, WiiExtension.None, "snes"),
            (ControllerKind.NesController, WiiExtension.None, "nes"),
            (ControllerKind.N64Controller, WiiExtension.None, "n64"),
            (ControllerKind.MegaDrive, WiiExtension.None, "megadrive"),
            (ControllerKind.WiiRemote, WiiExtension.None, "wii"),
            (ControllerKind.WiiRemote, WiiExtension.Nunchuk, "wii_nunchuk"),
            (ControllerKind.WiiRemote, WiiExtension.Classic, "wii_classic"),
            (ControllerKind.WiiUPro, WiiExtension.None, "wiiupro"),
        };
        foreach (var (kind, ext, name) in kinds)
        {
            foreach (bool pressed in new[] { false, true })
            {
                using var view = new InputView { Size = new Size(580, 410), WiiExtension = ext };
                var input = new PadInput
                {
                    Kind = kind,
                    Buttons = pressed ? ProButtons.A | ProButtons.Up | ProButtons.L | ProButtons.Plus | ProButtons.ZL : ProButtons.None,
                    LeftX = pressed ? 0.8f : 0, LeftY = pressed ? 0.5f : 0, RightX = pressed ? 1 : 0, RightY = 0,
                    LeftTrigger = pressed ? 0.6f : 0, RightTrigger = 0,
                };
                var gamepad = Mapping.ToGamepad(input, new Settings());
                view.Show(input, gamepad);
                using var bmp = new Bitmap(580, 410);
                view.DrawToBitmap(bmp, new Rectangle(0, 0, 580, 410));
                bmp.Save(Path.Combine(folder, $"{name}{(pressed ? "_pressed" : "")}.png"), ImageFormat.Png);
            }
        }
    }
}
