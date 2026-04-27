using System.Runtime.InteropServices;
using System.Windows.Forms;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Agent;

internal sealed class InputController
{
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint InputKeyboard = 1;
    private const uint KeyEventUnicode = 0x0004;
    private readonly object _coordinateLock = new();
    private int _frameWidth;
    private int _frameHeight;

    public void UpdateFrameSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        lock (_coordinateLock)
        {
            _frameWidth = width;
            _frameHeight = height;
        }
    }

    public void Apply(InputCommand command)
    {
        switch (command.Event)
        {
            case "mouseMove":
                MoveCursor(command);
                break;
            case "mouseDown":
                MoveCursor(command);
                mouse_event(ButtonFlag(command.Button, isDown: true), 0, 0, 0, UIntPtr.Zero);
                break;
            case "mouseUp":
                MoveCursor(command);
                mouse_event(ButtonFlag(command.Button, isDown: false), 0, 0, 0, UIntPtr.Zero);
                break;
            case "mouseClick":
                MoveCursor(command);
                var downFlag = ButtonFlag(command.Button, isDown: true);
                var upFlag = ButtonFlag(command.Button, isDown: false);
                mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
                mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
                break;
            case "wheel":
                MoveCursor(command);
                var wheelDelta = command.DeltaY.GetValueOrDefault() > 0 ? -120 : 120;
                mouse_event(MouseEventWheel, 0, 0, wheelDelta, UIntPtr.Zero);
                break;
            case "keyDown":
                SendKey(command.KeyCode, isDown: true);
                break;
            case "keyUp":
                SendKey(command.KeyCode, isDown: false);
                break;
            case "text":
                SendText(command.Text);
                break;
        }
    }

    private void MoveCursor(InputCommand command)
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1, 1);
        var source = GetCoordinateSource(bounds);
        var scaledX = source.Width > 0 ? command.X * bounds.Width / source.Width : command.X;
        var scaledY = source.Height > 0 ? command.Y * bounds.Height / source.Height : command.Y;
        var x = bounds.Left + ClampCoordinate(scaledX, bounds.Width);
        var y = bounds.Top + ClampCoordinate(scaledY, bounds.Height);
        SetCursorPos(x, y);
    }

    private ScreenSize GetCoordinateSource(System.Drawing.Rectangle bounds)
    {
        lock (_coordinateLock)
        {
            return _frameWidth > 0 && _frameHeight > 0
                ? new ScreenSize(_frameWidth, _frameHeight)
                : new ScreenSize(bounds.Width, bounds.Height);
        }
    }

    private static int ClampCoordinate(double value, int length)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp((int)Math.Round(value), 0, Math.Max(0, length - 1));
    }

    private static uint ButtonFlag(string? button, bool isDown)
    {
        return button switch
        {
            "right" => isDown ? MouseEventRightDown : MouseEventRightUp,
            "middle" => isDown ? MouseEventMiddleDown : MouseEventMiddleUp,
            _ => isDown ? MouseEventLeftDown : MouseEventLeftUp
        };
    }

    private static void SendKey(int? keyCode, bool isDown)
    {
        if (keyCode is null or < 0 or > 255)
        {
            return;
        }

        keybd_event((byte)keyCode.Value, 0, isDown ? 0 : KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void SendText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var character in text)
        {
            SendUnicodeCharacter(character);
        }
    }

    private static void SendUnicodeCharacter(char character)
    {
        var inputs = new[]
        {
            KeyboardInput(character, keyUp: false),
            KeyboardInput(character, keyUp: true)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input KeyboardInput(char character, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = 0,
                    ScanCode = (ushort)character,
                    Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;

        [FieldOffset(0)]
        public HardwareInputData Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInputData
    {
        public uint Message;
        public ushort LowParam;
        public ushort HighParam;
    }
}
