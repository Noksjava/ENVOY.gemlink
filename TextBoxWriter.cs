using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SipAiGateway;

public sealed class TextBoxWriter : TextWriter
{
    private readonly TextBox _textBox;

    public TextBoxWriter(TextBox textBox)
    {
        _textBox = textBox;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        AppendText(value.ToString());
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        AppendText(value);
    }

    public override void WriteLine(string? value)
    {
        AppendText(value + Environment.NewLine);
    }

    private void AppendText(string text)
    {
        if (_textBox.IsDisposed) return;

        if (_textBox.InvokeRequired)
        {
            _textBox.BeginInvoke(new Action<string>(AppendText), text);
            return;
        }

        _textBox.AppendText(text);
    }
}
