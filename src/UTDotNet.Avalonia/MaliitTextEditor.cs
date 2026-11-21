using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using UTDotNet.UbuntuTouch;

namespace UTDotNet.Avalonia;

/// <summary>Maps verified Qt/Maliit edits to Avalonia's normal editing events, preserving selection and undo.</summary>
public static class MaliitTextEditor
{
    public static void Commit(TextBox field, TextInputMethodClient client, CommitText commit)
    {
        if (field.IsReadOnly) return;
        if (commit.ReplacementLength < 0) throw new ArgumentOutOfRangeException(nameof(commit));
        var insertionStart = Math.Min(field.SelectionStart, field.SelectionEnd);
        client.SetPreeditText(null, null);
        if (commit.ReplacementStart != 0 || commit.ReplacementLength != 0)
        {
            // Qt removes the current selection before applying a replacement relative to the insertion point.
            if (field.SelectionStart != field.SelectionEnd) EditingKey(field, Key.Delete);
            var length = field.Text?.Length ?? 0;
            var start = (int)Math.Clamp((long)insertionStart + commit.ReplacementStart, 0, length);
            var end = (int)Math.Clamp((long)start + commit.ReplacementLength, start, length);
            field.SelectionStart = start; field.SelectionEnd = end;
        }
        if (commit.Text.Length > 0)
            field.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = commit.Text });
        else if (commit.ReplacementLength > 0) EditingKey(field, Key.Delete);
        if (commit.CursorPosition >= 0)
        {
            var cursor = (int)Math.Clamp((long)insertionStart + commit.ReplacementStart + commit.CursorPosition, 0, field.Text?.Length ?? 0);
            field.SelectionStart = field.SelectionEnd = cursor;
        }
    }

    public static void Preedit(TextBox field, TextInputMethodClient client, PreeditText preedit)
    {
        if (field.IsReadOnly) return;
        if (preedit.ReplacementStart != 0 || preedit.ReplacementLength != 0)
            Commit(field, client, new("", preedit.ReplacementStart, preedit.ReplacementLength, -1));
        client.SetPreeditText(preedit.Text, preedit.CursorPosition < 0 ? null : Math.Clamp(preedit.CursorPosition, 0, preedit.Text.Length));
        // Avalonia's public client supports one preedit span, not Maliit's individual format attributes.
    }

    public static bool HandleKey(TextBox field, InputKey input)
    {
        if (input.RequestType == 1) return true; // EventRequestSignalOnly: no edit.
        if (input.Type is not (6 or 7)) return false; // Qt KeyPress / KeyRelease.
        var key = input.Key switch
        {
            0x01000000 => global::Avalonia.Input.Key.Escape, 0x01000001 => global::Avalonia.Input.Key.Tab,
            0x01000003 => global::Avalonia.Input.Key.Back, 0x01000004 or 0x01000005 => global::Avalonia.Input.Key.Enter,
            0x01000007 => global::Avalonia.Input.Key.Delete, 0x01000010 => global::Avalonia.Input.Key.Home,
            0x01000011 => global::Avalonia.Input.Key.End, 0x01000012 => global::Avalonia.Input.Key.Left,
            0x01000013 => global::Avalonia.Input.Key.Up, 0x01000014 => global::Avalonia.Input.Key.Right,
            0x01000015 => global::Avalonia.Input.Key.Down, _ => global::Avalonia.Input.Key.None
        };
        var modifiers = KeyModifiers.None;
        if ((input.Modifiers & 0x02000000) != 0) modifiers |= KeyModifiers.Shift;
        if ((input.Modifiers & 0x04000000) != 0) modifiers |= KeyModifiers.Control;
        if ((input.Modifiers & 0x08000000) != 0) modifiers |= KeyModifiers.Alt;
        if ((input.Modifiers & 0x10000000) != 0) modifiers |= KeyModifiers.Meta;
        if (key == global::Avalonia.Input.Key.None)
        {
            if (input.Type == 6 && input.Text.Length > 0 && (modifiers & ~KeyModifiers.Shift) == 0)
                field.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = input.Text });
            return input.Text.Length > 0;
        }
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = input.Type == 6 ? InputElement.KeyDownEvent : InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
        return true;
    }

    private static void EditingKey(TextBox field, global::Avalonia.Input.Key key)
    {
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
    }
}
